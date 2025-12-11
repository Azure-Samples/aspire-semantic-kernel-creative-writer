// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.ServiceDefaults.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using System.Text;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterSession
{
    private readonly IChatClient _chatClient;
    private readonly Azure.AI.Projects.AgentsClient _agentsClient;
    private readonly string _researcherAgentId;
    private readonly ChatClientAgent _researcherAgent;
    private readonly ChatClientAgent _marketingAgent;
    private readonly ChatClientAgent _writerAgent;
    private readonly ChatClientAgent _editorAgent;
    private readonly List<ChatMessage> _chatHistory = new();

    public CreativeWriterSession(
        IChatClient chatClient,
        Azure.AI.Projects.AgentsClient agentsClient,
        string researcherAgentId,
        ChatClientAgent researcherAgent,
        ChatClientAgent marketingAgent,
        ChatClientAgent writerAgent,
        ChatClientAgent editorAgent)
    {
        _chatClient = chatClient;
        _agentsClient = agentsClient;
        _researcherAgentId = researcherAgentId;
        _researcherAgent = researcherAgent;
        _marketingAgent = marketingAgent;
        _writerAgent = writerAgent;
        _editorAgent = editorAgent;
    }

    internal async IAsyncEnumerable<AIChatCompletionDelta> ProcessStreamingRequest(CreateWriterRequest createWriterRequest)
    {
        // Step 1: Research Phase
        StringBuilder sbResearchResults = new();
        var researchPrompt = $"Research Context: {createWriterRequest.Research}\n\nPlease provide comprehensive research on this topic.";
        
        var researchMessages = new List<ChatMessage>
        {
            new(ChatRole.User, researchPrompt)
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(researchMessages))
        {
            var content = update.Text ?? string.Empty;
            sbResearchResults.Append(content);
            
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.ResearcherName),
                Content = content,
            });
        }

        // Step 2: Marketing/Product Search Phase
        StringBuilder sbProductResults = new();
        var productPrompt = $"Product Context: {createWriterRequest.Products}\n\nSearch for relevant product information using the available tools.";
        
        var marketingMessages = new List<ChatMessage>
        {
            new(ChatRole.User, productPrompt)
        };

        var marketingOptions = new ChatOptions
        {
            ToolMode = ChatToolMode.Auto
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(marketingMessages, marketingOptions))
        {
            var content = update.Text ?? string.Empty;
            sbProductResults.Append(content);
            
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.MarketingName),
                Content = content,
            });
        }

        // Step 3: Writing Phase
        var writerPrompt = $@"
Research Context: {createWriterRequest.Research}
Research Results: {sbResearchResults}
Product Context: {createWriterRequest.Products}
Product Results: {sbProductResults}
Assignment: {createWriterRequest.Writing}

Please write an article based on the above information.";

        var writerMessages = new List<ChatMessage>
        {
            new(ChatRole.User, writerPrompt)
        };

        StringBuilder articleContent = new();
        await foreach (var update in _chatClient.GetStreamingResponseAsync(writerMessages))
        {
            var content = update.Text ?? string.Empty;
            articleContent.Append(content);
            
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.WriterName),
                Content = content,
            });
        }

        // Step 4: Editing Loop
        bool articleAccepted = false;
        int maxIterations = 5;
        int iteration = 0;

        var editorMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an editor. Review the article and provide feedback. If satisfactory, end with 'Article accepted, no further rework necessary.'"),
            new(ChatRole.User, $"Please review this article:\n\n{articleContent}")
        };

        while (!articleAccepted && iteration < maxIterations)
        {
            StringBuilder editorFeedback = new();
            
            await foreach (var update in _chatClient.GetStreamingResponseAsync(editorMessages))
            {
                var content = update.Text ?? string.Empty;
                editorFeedback.Append(content);
                
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.EditorName),
                    Content = content,
                });
            }

            var feedback = editorFeedback.ToString();
            
            // Check if article is accepted
            if (feedback.Contains("Article accepted", StringComparison.OrdinalIgnoreCase))
            {
                articleAccepted = true;
                break;
            }

            // If not accepted, writer revises
            editorMessages.Add(new(ChatRole.Assistant, feedback));
            
            var revisionPrompt = $"Please revise the article based on this feedback:\n\n{feedback}";
            editorMessages.Add(new(ChatRole.User, revisionPrompt));

            StringBuilder revisedContent = new();
            await foreach (var update in _chatClient.GetStreamingResponseAsync(editorMessages))
            {
                var content = update.Text ?? string.Empty;
                revisedContent.Append(content);
                
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.WriterName),
                    Content = content,
                });
            }

            editorMessages.Add(new(ChatRole.Assistant, revisedContent.ToString()));
            iteration++;
        }
    }

    public async Task CleanupSessionAsync()
    {
        // Delete the Azure AI Agent Service agent
        await _agentsClient.DeleteAgentAsync(_researcherAgentId);
    }
}
