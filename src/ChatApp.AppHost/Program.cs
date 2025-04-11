using Aspire.Hosting.Azure;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Search;
using Microsoft.IdentityModel.Tokens;

var builder = DistributedApplication.CreateBuilder(args);

var azureDeployment = Environment.GetEnvironmentVariable("AzureDeployment") ?? "chatdeploymentnew";
var embeddingModelDeployment = Environment.GetEnvironmentVariable("EmbeddingModelDeployment") ?? "text-embedding-3-large";
var azureEndpoint = Environment.GetEnvironmentVariable("AzureEndpoint");

var vectorStoreCollectionName = Environment.GetEnvironmentVariable("VectorStoreCollectionName") ?? "products";

var agentModelBingDeployment = builder.AddBicepTemplate("aoiabing", "./BicepTemplates/openAi_bingSearch.module.bicep")
    .WithParameter(AzureBicepResource.KnownParameters.KeyVaultName)
    .WithParameter(AzureBicepResource.KnownParameters.PrincipalId)
    .WithParameter(AzureBicepResource.KnownParameters.PrincipalType);

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

var backend = builder.AddProject<Projects.ChatApp_WebApi>("backend")
    .WithReference(vectorSearch)
    .WithEnvironment("AzureDeployment", azureDeployment)
    .WithEnvironment("EmbeddingModelDeployment", embeddingModelDeployment)
    .WithEnvironment("AzureEndpoint", azureEndpoint)
    .WithEnvironment("VectorStoreCollectionName", vectorStoreCollectionName)
    .WithEnvironment("ConnectionStrings__openAi", agentModelBingDeployment.GetOutput("connectionString"))
    .WithEnvironment("ModelDeployment", agentModelBingDeployment.GetOutput("modelDeployment"))
    .WithEnvironment("AIProjectConnectionString", agentModelBingDeployment.GetOutput("aiProjectConnectionString"))
    .WithExternalHttpEndpoints();

var frontend = builder.AddNpmApp("frontend", "../ChatApp.React")
    .WithReference(backend)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "VITE_PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
