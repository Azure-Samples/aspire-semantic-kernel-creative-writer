// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.ServiceDefaults.Contracts;
using ChatApp.WebApi.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using System.Text;
using Microsoft.SemanticKernel.PromptTemplates.Liquid;
using Microsoft.SemanticKernel.Prompty;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterSession(Kernel kernel, Kernel bingKernel, Kernel vectorSearchKernel)
{
    private const string ResearcherName = "Researcher";
    private const string MarketingName = "Marketing";
    private const string WriterName = "Writer";
    private const string EditorName = "Editor";

    internal async IAsyncEnumerable<AIChatCompletionDelta> ProcessStreamingRequest(CreateWriterRequest createWriterRequest)
    {
        ChatCompletionAgent researcherAgent = new(ReadPromptyFileForTemplateConfig("./Agents/Prompts/researcher.prompty"), new LiquidPromptTemplateFactory())
        {
            Name = ResearcherName,
            Kernel = bingKernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = bingKernel.LoggerFactory
        };

        ChatCompletionAgent marketingAgent = new(ReadPromptyFileForTemplateConfig("./Agents/Prompts/marketing.prompty"), new LiquidPromptTemplateFactory())
        {
            Name = MarketingName,
            Kernel = vectorSearchKernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = vectorSearchKernel.LoggerFactory
        };

        StringBuilder sbResearchResults = new();
        await foreach (ChatMessageContent response in researcherAgent.InvokeAsync([], new() { { "research_context", createWriterRequest.Research } }))
        {
            sbResearchResults.AppendLine(response.Content);
            yield return new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.Assistant,
                Context = new AIChatAgentInfo(ResearcherName),
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
                Context = new AIChatAgentInfo(MarketingName),
                Content = response.Content,
            });
        }

        ChatCompletionAgent writerAgent = new(ReadPromptyFileForTemplateConfig("./Agents/Prompts/writer.prompty"), new LiquidPromptTemplateFactory())
        {
            Name = WriterName,
            Kernel = kernel,
            Arguments = [],
            LoggerFactory = kernel.LoggerFactory
        };

        ChatCompletionAgent editorAgent = new(ReadPromptyFileForTemplateConfig("./Agents/Prompts/editor.prompty"), new LiquidPromptTemplateFactory())
        {
            Name = EditorName,
            Kernel = kernel,
            LoggerFactory = kernel.LoggerFactory
        };

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
    }

    private static PromptTemplateConfig ReadFileForPromptTemplateConfig(string fileName)
    {
        string yaml = File.ReadAllText(fileName);
        return KernelFunctionYaml.ToPromptTemplateConfig(yaml);
    }
        private static PromptTemplateConfig ReadPromptyFileForTemplateConfig(string fileName)
    {
        string prompty = File.ReadAllText(fileName);
        #pragma warning disable SKEXP0040 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        return KernelFunctionPrompty.ToPromptTemplateConfig(prompty);
        #pragma warning restore SKEXP0040 // Type is for evaluation purposes only and is subject to change or removal in future updates.
    }

    private static KernelArguments CreateFunctionChoiceAutoBehavior()
    {
        return new KernelArguments(new AzureOpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Required() });
    }

    private sealed class NoFeedbackLeftTerminationStrategy : TerminationStrategy
    {
        // Terminate when the final message contains the term "Article accepted, no further rework necessary." - all done
        protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
        {
            if (agent.Name != EditorName)
                return Task.FromResult(false);

            return Task.FromResult(history[history.Count - 1].Content?.Contains("Article accepted", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
