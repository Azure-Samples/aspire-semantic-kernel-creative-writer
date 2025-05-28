// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.AI.Projects;
using Azure.Identity;
using ChatApp.WebApi.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using System.Text.Json;
using Microsoft.Extensions.VectorData;
using ChatApp.ServiceDefaults.Contracts;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterApp
{
    public const string ResearcherName = "Researcher";
    public const string MarketingName = "Marketing";
    public const string WriterName = "Writer";
    public const string EditorName = "Editor";

    private Kernel? _vectorSearchKernel;
    private AIProjectClient _aIProjectClient;
    private AgentsClient _agentsClient;
    private Kernel defaultKernel;
    private IConfiguration configuration;
    private bool _useAzureAIAgents;

    public CreativeWriterApp(Kernel defaultKernel, IConfiguration configuration)
    {
        this.defaultKernel = defaultKernel;
        this.configuration = configuration;
        _useAzureAIAgents = configuration.GetValue<bool>("UseAzureAIAgents");
        var clientOptions = new AIProjectClientOptions();
        _aIProjectClient = new AIProjectClient(configuration.GetValue<string>("AIProjectConnectionString")!, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true }), clientOptions);
        _agentsClient = _aIProjectClient.GetAgentsClient();
    }

    internal void SetResponseForSession(HttpResponse response)
    {
        if (_vectorSearchKernel == null)
        {
            throw new InvalidOperationException("Session not initialized. Call CreateSessionAsync() first.");
        }
        _vectorSearchKernel.FunctionInvocationFilters.Clear();
        _vectorSearchKernel.FunctionInvocationFilters.Add(new FunctionInvocationFilter(response));
    }

    internal async Task<CreativeWriterSession> CreateSessionAsync()
    {
        _vectorSearchKernel = defaultKernel.Clone();
        await ConfigureVectorSearchKernel(_vectorSearchKernel);

        // Get Azure AI Search tool connection if available
        var azureAISearchToolConnection = await TryGetAzureAISearchConnectionAsync();

        // Create agent instances based on configuration
        if (_useAzureAIAgents)
        {
            // Use Azure AI Agents for all roles
            var researcherAgent = await CreateAzureAIAgentAsync(
                ResearcherName,
                "./Agents/Prompts/researcher.yaml",
                new List<ToolDefinition> { new BingGroundingToolDefinition(await GetBingConnectionListAsync()) }
            );

            var marketingAgent = await CreateAzureAIAgentAsync(
                MarketingName,
                "./Agents/Prompts/marketing.yaml", 
                azureAISearchToolConnection != null 
                    ? new List<ToolDefinition> { 
                        new AzureAISearchToolDefinition(new ToolConnectionList { ConnectionList = { azureAISearchToolConnection } }) 
                      } 
                    : new List<ToolDefinition>()
            );

            var writerAgent = await CreateAzureAIAgentAsync(
                WriterName,
                "./Agents/Prompts/writer.yaml",
                new List<ToolDefinition>()
            );

            var editorAgent = await CreateAzureAIAgentAsync(
                EditorName,
                "./Agents/Prompts/editor.yaml",
                new List<ToolDefinition>()
            );

            return new CreativeWriterSession(defaultKernel, _agentsClient, researcherAgent, marketingAgent, writerAgent, editorAgent);
        }
        else
        {
            // Default implementation - only Researcher as Azure AI Agent, others as ChatCompletionAgent
            var bingConnection = await _aIProjectClient.GetConnectionsClient().GetConnectionAsync("bingGrounding");
            var connectionId = bingConnection.Value.Id;

            ToolConnectionList connectionList = new ToolConnectionList
            {
                ConnectionList = { new ToolConnection(connectionId) }
            };
            BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);
            var researcherTemplate = ReadFileForPromptTemplateConfig("./Agents/Prompts/researcher.yaml");

            // for the ease of the demo, we are creating an Agent in Azure AI Agent Service for every session and deleting it after the session finished
            // for production, you can want to create an agent once and reuse them
            var rAgent = await _agentsClient.CreateAgentAsync(
                model: configuration.GetValue<string>("ModelDeployment")!,
                name: researcherTemplate.Name,
                description: researcherTemplate.Description,
                instructions: researcherTemplate.Template,
                tools: new List<ToolDefinition> { bingGroundingTool }
            );

            AzureAIAgent researcherAgent = new(rAgent,
                                               _agentsClient,
                                               templateFactory: new KernelPromptTemplateFactory(),
                                               templateFormat: PromptTemplateConfig.SemanticKernelTemplateFormat)
            {
                Name = ResearcherName,
                Kernel = defaultKernel,
                Arguments = CreateFunctionChoiceAutoBehavior(),
                LoggerFactory = defaultKernel.LoggerFactory,
            };

            ChatCompletionAgent marketingAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/marketing.yaml"), templateFactory: new KernelPromptTemplateFactory())
            {
                Name = MarketingName,
                Kernel = _vectorSearchKernel,
                Arguments = CreateFunctionChoiceAutoBehavior(),
                LoggerFactory = _vectorSearchKernel.LoggerFactory
            };

            ChatCompletionAgent writerAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/writer.yaml"), templateFactory: new KernelPromptTemplateFactory())
            {
                Name = WriterName,
                Kernel = defaultKernel,
                Arguments = [],
                LoggerFactory = defaultKernel.LoggerFactory
            };

            ChatCompletionAgent editorAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/editor.yaml"), templateFactory: new KernelPromptTemplateFactory())
            {
                Name = EditorName,
                Kernel = defaultKernel,
                LoggerFactory = defaultKernel.LoggerFactory
            };

            return new CreativeWriterSession(defaultKernel, _agentsClient, researcherAgent, marketingAgent, writerAgent, editorAgent);
        }
    }

    private async Task ConfigureVectorSearchKernel(Kernel vectorSearchKernel)
    {
        IVectorStore vectorStore = vectorSearchKernel.GetRequiredService<IVectorStore>();

        // Get and create collection if it doesn't exist.
        IVectorStoreRecordCollection<string, ProductDataModel> recordCollection = vectorStore.GetCollection<string, ProductDataModel>(configuration["VectorStoreCollectionName"]!);
        await recordCollection.CreateCollectionIfNotExistsAsync();

        ITextEmbeddingGenerationService textEmbeddingGeneration = vectorSearchKernel.GetRequiredService<ITextEmbeddingGenerationService>();

        VectorStoreTextSearch<ProductDataModel> vectorTextSearch = new(recordCollection, textEmbeddingGeneration);
        KernelPlugin vectorSearchPlugin = vectorTextSearch.CreateWithGetTextSearchResults("ProductSearchPlugin");
        vectorSearchKernel.Plugins.Add(vectorSearchPlugin);
    }

    internal sealed class FunctionInvocationFilter(HttpResponse response) : IFunctionInvocationFilter
    {
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            var delta = new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.System,
                Content = $"{context.Function.Name}: {JsonSerializer.Serialize(context.Arguments)}  \n",
            });

            await response.WriteAsync($"{JsonSerializer.Serialize(delta)}\r\n");
            await response.Body.FlushAsync();

            await next(context);

            var metadata = context.Result?.Metadata;
            //if (metadata is not null && metadata.ContainsKey("Usage"))
            //{
            //    this._output.WriteLine($"Token usage: {metadata["Usage"]?.AsJson()}");
            //}
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

    private async Task<ToolConnectionList> GetBingConnectionListAsync()
    {
        var bingConnection = await _aIProjectClient.GetConnectionsClient().GetConnectionAsync("bingGrounding");
        var connectionId = bingConnection.Value.Id;

        return new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(connectionId) }
        };
    }

    private async Task<ToolConnection?> TryGetAzureAISearchConnectionAsync()
    {
        try
        {
            var connectionsClient = _aIProjectClient.GetConnectionsClient();
            var searchConnection = await connectionsClient.GetConnectionAsync("azureAISearch");
            return new ToolConnection(searchConnection.Value.Id);
        }
        catch
        {
            // If connection doesn't exist, return null
            return null;
        }
    }

    private async Task<AzureAIAgent> CreateAzureAIAgentAsync(string name, string promptPath, List<ToolDefinition> tools)
    {
        var templateConfig = ReadFileForPromptTemplateConfig(promptPath);
        
        var agent = await _agentsClient.CreateAgentAsync(
            model: configuration.GetValue<string>("ModelDeployment")!,
            name: templateConfig.Name,
            description: templateConfig.Description,
            instructions: templateConfig.Template,
            tools: tools
        );

        return new AzureAIAgent(agent,
                             _agentsClient,
                             templateFactory: new KernelPromptTemplateFactory(),
                             templateFormat: PromptTemplateConfig.SemanticKernelTemplateFormat)
        {
            Name = name,
            Kernel = name == MarketingName ? _vectorSearchKernel! : defaultKernel,
            Arguments = name == WriterName ? new KernelArguments() : CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = defaultKernel.LoggerFactory,
        };
    }
}
