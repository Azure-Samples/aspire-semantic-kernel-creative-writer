// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.ServiceDefaults.Contracts;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using Azure.AI.Projects; // For AgentsClient
using MSKAgent = Microsoft.SemanticKernel.Agents.Agent; // Create alias to disambiguate

namespace ChatApp.WebApi.Agents;

public class CreativeWriterSession(Kernel kernel, AgentsClient agentsClient, MSKAgent researcherAgent, MSKAgent marketingAgent, MSKAgent writerAgent, MSKAgent editorAgent)
{
    internal async IAsyncEnumerable<AIChatCompletionDelta> ProcessStreamingRequest(CreateWriterRequest createWriterRequest)
    {
        // Create thread for AzureAIAgent if needed
        string? threadId = null;
        if (researcherAgent is AzureAIAgent azureAIResearcher)
        {
            // create a conversation Thread with the Researcher agent
            Azure.Response<AgentThread> threadResponse = await agentsClient.CreateThreadAsync();
            AgentThread thread = threadResponse.Value;
            threadId = thread.Id;
        }

        StringBuilder sbResearchResults = new();
        
        // Handle different agent types
        if (researcherAgent is AzureAIAgent azureAIAgent)
        {
            var prompt = $"Research context: {createWriterRequest.Research}";
            KernelArguments args = new KernelArguments();
            if (!string.IsNullOrEmpty(threadId))
            {
                args["threadId"] = threadId;
            }
            
            await foreach (var response in azureAIAgent.InvokeAsync(prompt, args))
            {
                sbResearchResults.AppendLine(response.Content);
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.ResearcherName),
                    Content = response.Content,
                });
            }
        }
        else if (researcherAgent is ChatCompletionAgent chatCompletionAgent)
        {
            ChatHistory researcherChatHistory = new ChatHistory();
            researcherChatHistory.AddUserMessage($"Research context: {createWriterRequest.Research}");
            
            await foreach (var response in chatCompletionAgent.InvokeAsync(researcherChatHistory))
            {
                sbResearchResults.AppendLine(response.Content);
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.ResearcherName),
                    Content = response.Content,
                });
            }
        }

        StringBuilder sbProductResults = new();
        
        // Handle different agent types for marketing agent
        if (marketingAgent is AzureAIAgent azureAIMarketingAgent)
        {
            var prompt = $"Product context: {createWriterRequest.Products}";
            await foreach (var response in azureAIMarketingAgent.InvokeAsync(prompt))
            {
                sbProductResults.AppendLine(response.Content);
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.MarketingName),
                    Content = response.Content,
                });
            }
        }
        else if (marketingAgent is ChatCompletionAgent chatCompletionMarketingAgent)
        {
            ChatHistory marketingChatHistory = new ChatHistory();
            marketingChatHistory.AddUserMessage($"Product context: {createWriterRequest.Products}");
            
            await foreach (var response in chatCompletionMarketingAgent.InvokeAsync(marketingChatHistory))
            {
                sbProductResults.AppendLine(response.Content);
                yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
                {
                    Role = AIChatRole.Assistant,
                    Context = new AIChatAgentInfo(CreativeWriterApp.MarketingName),
                    Content = response.Content,
                });
            }
        }

        // Set the arguments on the writer agent if it's a ChatCompletionAgent
        if (writerAgent is ChatCompletionAgent chatCompletionWriterAgent)
        {
            chatCompletionWriterAgent.Arguments["research_context"] = createWriterRequest.Research;
            chatCompletionWriterAgent.Arguments["research_results"] = sbResearchResults.ToString();
            chatCompletionWriterAgent.Arguments["product_context"] = createWriterRequest.Products;
            chatCompletionWriterAgent.Arguments["product_results"] = sbProductResults.ToString();
            chatCompletionWriterAgent.Arguments["assignment"] = createWriterRequest.Writing;
        }

        AgentGroupChat chat = new(writerAgent, editorAgent)
        {
            LoggerFactory = kernel.LoggerFactory,
            ExecutionSettings = new AgentGroupChatSettings
            {
                SelectionStrategy = new SequentialSelectionStrategy() { InitialAgent = writerAgent },
                TerminationStrategy = new NoFeedbackLeftTerminationStrategy()
            }
        };
        
        // Initialize with a prompt directly through the initial agent (writerAgent)
        var writerContextMessage = $@"
Research Context: {createWriterRequest.Research}
Research Results: {sbResearchResults}
Product Context: {createWriterRequest.Products}
Product Results: {sbProductResults}
Assignment: {createWriterRequest.Writing}
";

        // Start the chat with a message to the writer agent
        ChatHistory writerChatHistory = new ChatHistory();
        writerChatHistory.AddUserMessage(writerContextMessage);

        // Just use the chat as is - no need for initial message setup
        await foreach (ChatMessageContent response in chat.InvokeAsync())
        {
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(response.AuthorName ?? ""),
                Content = response.Content,
            });
        }
    }

    private sealed class NoFeedbackLeftTerminationStrategy : TerminationStrategy
    {
        // Terminate when the final message contains the term "Article accepted, no further rework necessary." - all done
        protected override Task<bool> ShouldAgentTerminateAsync(MSKAgent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
        {
            if (agent.Name != CreativeWriterApp.EditorName)
                return Task.FromResult(false);

            return Task.FromResult(history[history.Count - 1].Content?.Contains("Article accepted", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    public async Task CleanupSessionAsync() {
        // delete all Agents from the session that are AzureAIAgents
        // otherwise they will not be deleted on the service/backend of Azure AI Agents Service
        
        if (researcherAgent is AzureAIAgent azureAIResearcher)
        {
            await agentsClient.DeleteAgentAsync(azureAIResearcher.Id);
        }
        
        if (marketingAgent is AzureAIAgent azureAIMarketing)
        {
            await agentsClient.DeleteAgentAsync(azureAIMarketing.Id);
        }
        
        if (writerAgent is AzureAIAgent azureAIWriter)
        {
            await agentsClient.DeleteAgentAsync(azureAIWriter.Id);
        }
        
        if (editorAgent is AzureAIAgent azureAIEditor)
        {
            await agentsClient.DeleteAgentAsync(azureAIEditor.Id);
        }
    }
}
