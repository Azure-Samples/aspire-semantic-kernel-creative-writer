// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Identity;
using ChatApp.ServiceDefaults.Contracts;
using ChatApp.WebApi.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using System.Text;
using Azure.AI.Projects;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterSession(Kernel kernel, string aiProjectConnectionString, string aiModelDeployment, Kernel vectorSearchKernel)
{
    private const string ResearcherName = "Researcher";
    private const string MarketingName = "Marketing";
    private const string WriterName = "Writer";
    private const string EditorName = "Editor";

    internal async IAsyncEnumerable<AIChatCompletionDelta> ProcessStreamingRequest(CreateWriterRequest createWriterRequest)
    {
        var clientOptions = new AIProjectClientOptions();
        //clientOptions.AddPolicy(new CustomHeadersPolicy(), HttpPipelinePosition.PerCall);
        var aIProjectClient = new AIProjectClient(aiProjectConnectionString, new DefaultAzureCredential(), clientOptions);

        AgentsClient agentsClient = aIProjectClient.GetAgentsClient();
        
        var bingConnection = await aIProjectClient.GetConnectionsClient().GetConnectionAsync("bingGrounding");
        var connectionId = bingConnection.Value.Id;

        ToolConnectionList connectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(connectionId) }
        };
        BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);
        var researcherTemplate = ReadFileForPromptTemplateConfig("./Agents/Prompts/researcher.yaml");
        
        Azure.AI.Projects.Agent model = await agentsClient.CreateAgentAsync(
            model: aiModelDeployment,
            name: researcherTemplate.Name,
            description: researcherTemplate.Description,
            instructions: researcherTemplate.Template,
            tools: new List<ToolDefinition> { bingGroundingTool }
        );

        AzureAIAgent researcherAgent = new(model,
                                           agentsClient,
                                           templateFactory: new KernelPromptTemplateFactory(),
                                           templateFormat: PromptTemplateConfig.SemanticKernelTemplateFormat)  {
            Name = ResearcherName,
            Kernel = kernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = kernel.LoggerFactory,
        };

        Azure.Response<AgentThread> threadResponse = await agentsClient.CreateThreadAsync();
        AgentThread thread = threadResponse.Value;

        ChatCompletionAgent marketingAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/marketing.yaml"))
        {
            Name = MarketingName,
            Kernel = vectorSearchKernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = vectorSearchKernel.LoggerFactory
        };

        StringBuilder sbResearchResults = new();
        await foreach (ChatMessageContent response in researcherAgent.InvokeAsync(thread.Id, new KernelArguments() { { "research_context", createWriterRequest.Research } }))
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

        ChatCompletionAgent writerAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/writer.yaml"))
        {
            Name = WriterName,
            Kernel = kernel,
            Arguments = [],
            LoggerFactory = kernel.LoggerFactory
        };

        ChatCompletionAgent editorAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/editor.yaml"))
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

    private static KernelArguments CreateFunctionChoiceAutoBehavior()
    {
        return new KernelArguments(new AzureOpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Required() });
    }

    private sealed class NoFeedbackLeftTerminationStrategy : TerminationStrategy
    {
        // Terminate when the final message contains the term "Article accepted, no further rework necessary." - all done
        protected override Task<bool> ShouldAgentTerminateAsync(Microsoft.SemanticKernel.Agents.Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
        {
            if (agent.Name != EditorName)
                return Task.FromResult(false);

            return Task.FromResult(history[history.Count - 1].Content?.Contains("Article accepted", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
