// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.VectorData;
using System.Text.Json.Serialization;

namespace ChatApp.WebApi.Model;

internal sealed class ProductDataModel
{
    [VectorStoreRecordKey]
    public required string Key { get; set; }

    [VectorStoreRecordData]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [VectorStoreRecordData]
    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [VectorStoreRecordVector(3072)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}