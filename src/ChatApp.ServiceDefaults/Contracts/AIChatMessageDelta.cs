// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace ChatApp.ServiceDefaults.Contracts;

public struct AIChatMessageDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("role")]
    public AIChatRole? Role { get; set; }

    [JsonPropertyName("context"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AIChatAgentInfo? Context { get; set; }
}

public struct AIChatAgentInfo(string Name)
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = Name;

}