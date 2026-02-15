// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.SemanticKernel.Agents.AzureAI;
using Azure.AI.Projects;
using System.Text.Json.Serialization;

namespace ChatApp.WebApi.Agents;

/// <summary>
/// Tool definition for Azure AI Search capability
/// </summary>
public class AzureAISearchToolDefinition : ToolDefinition
{
    /// <summary>
    /// Gets the type of the tool.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; } = "azure_ai_search";

    /// <summary>
    /// Creates a new instance of the <see cref="AzureAISearchToolDefinition"/> class.
    /// </summary>
    /// <param name="connectionList">Tool connection list</param>
    public AzureAISearchToolDefinition(ToolConnectionList connectionList)
    {
        this.ConnectionList = connectionList;
    }

    /// <summary>
    /// Gets or sets the connection list.
    /// </summary>
    [JsonPropertyName("connectionList")]
    public ToolConnectionList ConnectionList { get; set; }
}