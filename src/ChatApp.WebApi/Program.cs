// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Identity;
using ChatApp.ServiceDefaults.Contracts;
using ChatApp.WebApi.Agents;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddAzureOpenAIClient("openAi", configureSettings: settings =>
{
    settings.Credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true });
});

builder.AddAzureSearchClient("vectorSearch", configureSettings: settings =>
{
    settings.Credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true });
});

// Register IChatClient for Agent Framework
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var openAiClient = sp.GetRequiredService<Azure.AI.OpenAI.AzureOpenAIClient>();
    return new ChatClientBuilder(openAiClient
        .GetChatClient(builder.Configuration["AzureDeployment"]!)
        .AsIChatClient())
        .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
        .Build();
});

// Register embedding generation service
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var openAiClient = sp.GetRequiredService<Azure.AI.OpenAI.AzureOpenAIClient>();
    return openAiClient
        .GetEmbeddingClient(builder.Configuration["EmbeddingModelDeployment"]!)
        .AsEmbeddingGenerator();
});

// Register SearchClient for vector search
builder.Services.AddSingleton<Azure.Search.Documents.SearchClient>(sp =>
{
    var searchIndexClient = sp.GetRequiredService<Azure.Search.Documents.Indexes.SearchIndexClient>();
    return searchIndexClient.GetSearchClient(builder.Configuration["VectorStoreCollectionName"]!);
});

builder.Services.AddTransient<CreativeWriterApp>();

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<AIChatRole>(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

// app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.Run();
