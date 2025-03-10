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

public class CreativeWriterApp(Kernel defaultKernel, IConfiguration configuration)
{
    public const string ResearcherName = "Researcher";
    public const string MarketingName = "Marketing";
    public const string WriterName = "Writer";
    public const string EditorName = "Editor";

    private Kernel? _vectorSearchKernel;

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
        
        var clientOptions = new AIProjectClientOptions();
        //clientOptions.AddPolicy(new CustomHeadersPolicy(), HttpPipelinePosition.PerCall);
        var aIProjectClient = new AIProjectClient(configuration.GetConnectionString("aiProject")!, new DefaultAzureCredential(), clientOptions);

        AgentsClient agentsClient = aIProjectClient.GetAgentsClient();
        
        var bingConnection = await aIProjectClient.GetConnectionsClient().GetConnectionAsync("bingGrounding");
        var connectionId = bingConnection.Value.Id;

        ToolConnectionList connectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(connectionId) }
        };
        BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);
        var researcherTemplate = ReadFileForPromptTemplateConfig("./Agents/Prompts/researcher.yaml");
        
        // curious on when to delete 
        Azure.AI.Projects.Agent rAgent = await agentsClient.CreateAgentAsync(
            model: configuration.GetValue<string>("OPENAI_MODEL_DEPLOYMENT")!,
            name: researcherTemplate.Name,
            description: researcherTemplate.Description,
            instructions: researcherTemplate.Template,
            tools: new List<ToolDefinition> { bingGroundingTool }
        );

        AzureAIAgent researcherAgent = new(rAgent,
                                           agentsClient,
                                           templateFactory: new KernelPromptTemplateFactory(),
                                           templateFormat: PromptTemplateConfig.SemanticKernelTemplateFormat)  {
            Name = ResearcherName,
            Kernel = defaultKernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = defaultKernel.LoggerFactory,
        };

        ChatCompletionAgent marketingAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/marketing.yaml"))
        {
            Name = MarketingName,
            Kernel = _vectorSearchKernel,
            Arguments = CreateFunctionChoiceAutoBehavior(),
            LoggerFactory = _vectorSearchKernel.LoggerFactory
        };

        ChatCompletionAgent writerAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/writer.yaml"))
        {
            Name = WriterName,
            Kernel = defaultKernel,
            Arguments = [],
            LoggerFactory = defaultKernel.LoggerFactory
        };

        ChatCompletionAgent editorAgent = new(ReadFileForPromptTemplateConfig("./Agents/Prompts/editor.yaml"))
        {
            Name = EditorName,
            Kernel = defaultKernel,
            LoggerFactory = defaultKernel.LoggerFactory
        };

        return new CreativeWriterSession(defaultKernel, agentsClient, researcherAgent, marketingAgent, writerAgent, editorAgent);
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
}
