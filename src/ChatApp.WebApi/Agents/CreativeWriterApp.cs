// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.WebApi.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using System.Text.Json;
using Microsoft.Extensions.VectorData;
using ChatApp.ServiceDefaults.Contracts;

namespace ChatApp.WebApi.Agents;

public class CreativeWriterApp(Kernel defaultKernel, IConfiguration configuration)
{
    internal async Task<CreativeWriterSession> CreateSessionAsync(HttpResponse response)
    {
        defaultKernel.FunctionInvocationFilters.Add(new FunctionInvocationFilter(response));

        Kernel bingKernel = defaultKernel.Clone();

        BingTextSearch textSearch = new(apiKey: configuration["BingAPIKey"]!);
        KernelPlugin searchPlugin = textSearch.CreateWithSearch("BingSearchPlugin");
        bingKernel.Plugins.Add(searchPlugin);

        Kernel vectorSearchKernel = defaultKernel.Clone();
        await ConfigureVectorSearchKernel(vectorSearchKernel);

        return new CreativeWriterSession(defaultKernel, bingKernel, vectorSearchKernel);
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
}
