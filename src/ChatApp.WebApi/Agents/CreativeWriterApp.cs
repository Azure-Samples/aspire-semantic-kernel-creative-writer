// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using ChatApp.WebApi.Model;
using ChatApp.ServiceDefaults.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterApp
{
    public const string ResearcherName = "Researcher";
    public const string MarketingName = "Marketing";
    public const string WriterName = "Writer";
    public const string EditorName = "Editor";

    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly AIProjectClient _aIProjectClient;
    private readonly AgentsClient _agentsClient;
    private readonly SearchClient _searchClient;
    private readonly IConfiguration _configuration;

    public CreativeWriterApp(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        SearchClient searchClient,
        IConfiguration configuration)
    {
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
        _searchClient = searchClient;
        _configuration = configuration;
        var clientOptions = new AIProjectClientOptions();
        _aIProjectClient = new AIProjectClient(
            configuration.GetValue<string>("AIProjectConnectionString")!,
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true }),
            clientOptions);
        _agentsClient = _aIProjectClient.GetAgentsClient();
    }

    internal void SetResponseForSession(HttpResponse response)
    {
        // TODO: Implement function invocation monitoring for Agent Framework
        // In Agent Framework, middleware can be used to intercept function calls
    }

    internal async Task<CreativeWriterSession> CreateSessionAsync()
    {
        // Get Bing connection for researcher agent
        var bingConnection = await _aIProjectClient.GetConnectionsClient().GetConnectionAsync("bingGrounding");
        var connectionId = bingConnection.Value.Id;

        ToolConnectionList connectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(connectionId) }
        };
        BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);
        
        // Read prompt templates from YAML files
        var researcherInstructions = ReadInstructionsFromYaml("./Agents/Prompts/researcher.yaml");
        var marketingInstructions = ReadInstructionsFromYaml("./Agents/Prompts/marketing.yaml");
        var writerInstructions = ReadInstructionsFromYaml("./Agents/Prompts/writer.yaml");
        var editorInstructions = ReadInstructionsFromYaml("./Agents/Prompts/editor.yaml");

        // Create researcher agent in Azure AI Agent Service
        // For the ease of the demo, we are creating an Agent in Azure AI Agent Service for every session
        // For production, you may want to create an agent once and reuse them
        var rAgent = await _agentsClient.CreateAgentAsync(
            model: _configuration.GetValue<string>("ModelDeployment")!,
            name: ResearcherName,
            description: "Expert researcher that helps writers by formulating queries and providing information",
            instructions: researcherInstructions,
            tools: new List<ToolDefinition> { bingGroundingTool }
        );

        // Create researcher agent using Agent Framework
        var researcherAgent = new ChatClientAgent(
            _chatClient,
            new ChatClientAgentOptions(instructions: researcherInstructions)
            {
                Name = ResearcherName
            }
        );

        // Create marketing agent with vector search capability
        var marketingAgent = CreateMarketingAgent(marketingInstructions);

        // Create writer agent
        var writerAgent = new ChatClientAgent(
            _chatClient,
            new ChatClientAgentOptions(instructions: writerInstructions)
            {
                Name = WriterName
            }
        );

        // Create editor agent
        var editorAgent = new ChatClientAgent(
            _chatClient,
            new ChatClientAgentOptions(instructions: editorInstructions)
            {
                Name = EditorName
            }
        );

        return new CreativeWriterSession(
            _chatClient,
            _agentsClient,
            rAgent.Value.Id,
            researcherAgent,
            marketingAgent,
            writerAgent,
            editorAgent);
    }

    private ChatClientAgent CreateMarketingAgent(string instructions)
    {
        // Create marketing agent with vector search function
        var marketingChatClient = new FunctionInvokingChatClient(_chatClient);
        
        // Add vector search function
        var vectorSearchFunction = AIFunctionFactory.Create(
            async (string query) =>
            {
                // Generate embedding for the query
                var embedding = await _embeddingGenerator.GenerateAsync(query);
                
                // Search for similar products
                var searchOptions = new Azure.Search.Documents.SearchOptions
                {
                    VectorSearch = new()
                    {
                        Queries = { new Azure.Search.Documents.Models.VectorizedQuery(embedding.Vector.ToArray()) { KNearestNeighborsCount = 5 } }
                    },
                    Size = 5
                };

                var searchResults = await _searchClient.SearchAsync<ProductDataModel>("*", searchOptions);
                var products = new List<ProductDataModel>();
                
                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    products.Add(result.Document);
                }

                return JsonSerializer.Serialize(products);
            },
            name: "SearchProducts",
            description: "Searches for products in the vector database based on a query"
        );

        marketingChatClient.AdditionalTools?.Add(vectorSearchFunction);

        return new ChatClientAgent(
            marketingChatClient,
            new ChatClientAgentOptions(instructions: instructions)
            {
                Name = MarketingName,
                ChatOptions = new ChatOptions
                {
                    ToolMode = ChatToolMode.Auto
                }
            }
        );
    }

    private static string ReadInstructionsFromYaml(string fileName)
    {
        string yaml = File.ReadAllText(fileName);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        
        var yamlObject = deserializer.Deserialize<Dictionary<string, object>>(yaml);
        
        if (yamlObject.ContainsKey("template"))
        {
            return yamlObject["template"].ToString() ?? string.Empty;
        }
        
        throw new InvalidOperationException($"No template found in {fileName}");
    }
}
