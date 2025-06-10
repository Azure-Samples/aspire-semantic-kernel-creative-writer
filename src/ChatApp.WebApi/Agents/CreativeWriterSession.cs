// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.ServiceDefaults.Contracts;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.AzureAI;
using System.Text;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterSession(Kernel kernel, Azure.AI.Projects.AgentsClient agentsClient, AzureAIAgent researcherAgent, ChatCompletionAgent marketingAgent, ChatCompletionAgent writerAgent, ChatCompletionAgent editorAgent)
{

    internal async IAsyncEnumerable<AIChatCompletionDelta> ProcessStreamingRequest(CreateWriterRequest createWriterRequest)
    {
        // If user feedback is provided, handle user feedback flow
        if (!string.IsNullOrEmpty(createWriterRequest.UserFeedback) && !string.IsNullOrEmpty(createWriterRequest.PreviousArticle))
        {
            await foreach (var delta in ProcessUserFeedback(createWriterRequest))
            {
                yield return delta;
            }
            yield break;
        }

        // Original flow: research, marketing, and writer-editor collaboration
        // create an conversation Thread with the Researcher agent
        Azure.Response<Azure.AI.Projects.AgentThread> threadResponse = await agentsClient.CreateThreadAsync();
        Azure.AI.Projects.AgentThread thread = threadResponse.Value;

        StringBuilder sbResearchResults = new();
        await foreach (ChatMessageContent response in researcherAgent.InvokeAsync(thread.Id, new KernelArguments() { { "research_context", createWriterRequest.Research } }))
        {
            sbResearchResults.AppendLine(response.Content);
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.ResearcherName),
                Content = response.Content,
            });
        }

        StringBuilder sbProductResults = new();
        await foreach (ChatMessageContent response in marketingAgent.InvokeAsync([], new() { { "product_context", createWriterRequest.Products } }))
        {
            sbProductResults.AppendLine(response.Content);
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.MarketingName),
                Content = response.Content,
            });
        }

        writerAgent.Arguments["research_context"] = createWriterRequest.Research;
        writerAgent.Arguments["research_results"] = sbResearchResults.ToString();
        writerAgent.Arguments["product_context"] = createWriterRequest.Products;
        writerAgent.Arguments["product_results"] = sbProductResults.ToString();
        writerAgent.Arguments["assignment"] = createWriterRequest.Writing;

        AgentGroupChat chat = new(writerAgent, editorAgent)
        {
            LoggerFactory = kernel.LoggerFactory,
            ExecutionSettings = new AgentGroupChatSettings
            {
                SelectionStrategy = new SequentialSelectionStrategy() { InitialAgent = writerAgent },
                TerminationStrategy = new NoFeedbackLeftTerminationStrategy()
            }
        };

        await foreach (ChatMessageContent response in chat.InvokeAsync())
        {
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(response.AuthorName ?? ""),
                Content = response.Content,
            });
        }

        // Signal that initial phase is complete and user can provide feedback
        yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
        {
            Role = AIChatRole.System,
            Content = "Initial article complete. You can now provide additional feedback to further improve the article.",
        });
    }

    private async IAsyncEnumerable<AIChatCompletionDelta> ProcessUserFeedback(CreateWriterRequest createWriterRequest)
    {
        // Set up the writer agent with minimal context for user feedback
        // Clear any previous arguments and set basic context
        writerAgent.Arguments.Clear();
        writerAgent.Arguments["research_context"] = createWriterRequest.Research;
        writerAgent.Arguments["product_context"] = createWriterRequest.Products;
        writerAgent.Arguments["assignment"] = createWriterRequest.Writing;

        // Create a simple chat history with the previous article and user feedback
        var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
        
        chatHistory.AddAssistantMessage(createWriterRequest.PreviousArticle!);
        chatHistory.AddUserMessage($"Please revise the article based on this feedback: {createWriterRequest.UserFeedback}");

        // Invoke the writer agent directly with the chat history
        await foreach (ChatMessageContent response in writerAgent.InvokeAsync(chatHistory))
        {
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(CreativeWriterApp.WriterName),
                Content = response.Content,
            });
        }

        // Signal that user can provide more feedback if needed
        yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
        {
            Role = AIChatRole.System,
            Content = "Article revised based on your feedback. You can provide additional feedback if needed.",
        });
    }

    private sealed class NoFeedbackLeftTerminationStrategy : TerminationStrategy
    {
        // Terminate when the final message contains the term "Article accepted, no further rework necessary." - all done
        protected override Task<bool> ShouldAgentTerminateAsync(Microsoft.SemanticKernel.Agents.Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
        {
            if (agent.Name != CreativeWriterApp.EditorName)
                return Task.FromResult(false);

            return Task.FromResult(history[history.Count - 1].Content?.Contains("Article accepted", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    public async Task CleanupSessionAsync() {
        // delete all Agents from the session, otherwise they will not be deleted on the service/backend of Azure AI Agents Service
        await agentsClient.DeleteAgentAsync(researcherAgent.Id);
    }
}
