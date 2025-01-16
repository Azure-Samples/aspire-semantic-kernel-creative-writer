using Aspire.Hosting.Azure;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Search;
using Microsoft.IdentityModel.Tokens;

var builder = DistributedApplication.CreateBuilder(args);

var azureDeployment = Environment.GetEnvironmentVariable("AzureDeployment") ?? "chatdeploymentnew";
var embeddingModelDeployment = Environment.GetEnvironmentVariable("EmbeddingModelDeployment") ?? "text-embedding-3-large";
var azureEndpoint = Environment.GetEnvironmentVariable("AzureEndpoint");

var vectorStoreCollectionName = Environment.GetEnvironmentVariable("VectorStoreCollectionName") ?? "products";

var exisitingOpenAi = !builder.Configuration.GetSection("ConnectionStrings")["openAi"].IsNullOrEmpty();
var openAi = !builder.ExecutionContext.IsPublishMode && exisitingOpenAi
    ? builder.AddConnectionString("openAi")
    : builder.AddAzureOpenAI("openAi")
        .AddDeployment(new AzureOpenAIDeployment(azureDeployment, "gpt-4o", "2024-05-13", "Standard", 10))
        .AddDeployment(new AzureOpenAIDeployment(embeddingModelDeployment, "text-embedding-3-large", "1"));

var exisitingVectorSearch = !builder.Configuration.GetSection("ConnectionStrings")["vectorSearch"].IsNullOrEmpty();
var vectorSearch = !builder.ExecutionContext.IsPublishMode && exisitingVectorSearch
    ? builder.AddConnectionString("vectorSearch")
    : builder.AddAzureSearch("vectorSearch")
    .ConfigureInfrastructure(infra =>
    {
        var resources = infra.GetProvisionableResources();

        var searchService = resources.OfType<SearchService>().Single();
        searchService.Identity = new ManagedServiceIdentity
        {
            ManagedServiceIdentityType = ManagedServiceIdentityType.SystemAssigned
        };
    });

var bingSearch = builder.AddBicepTemplate("bingSearch", "./BicepTemplates/bingSearch.bicep")
    .WithParameter(AzureBicepResource.KnownParameters.KeyVaultName);
var bingAPIKey = bingSearch.GetSecretOutput("bingAPIKey");

var backend = builder.AddProject<Projects.ChatApp_WebApi>("backend")
    .WithReference(openAi)
    .WithReference(vectorSearch)
    .WithEnvironment("AzureDeployment", azureDeployment)
    .WithEnvironment("EmbeddingModelDeployment", embeddingModelDeployment)
    .WithEnvironment("AzureEndpoint", azureEndpoint)
    .WithEnvironment("BingAPIKey", bingAPIKey)
    .WithEnvironment("VectorStoreCollectionName", vectorStoreCollectionName)
    .WithExternalHttpEndpoints();

var frontend = builder.AddNpmApp("frontend", "../ChatApp.React")
    .WithReference(backend)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "VITE_PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
