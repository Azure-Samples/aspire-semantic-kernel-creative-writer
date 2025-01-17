// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using ChatApp.ServiceDefaults.Converters;

namespace ChatApp.ServiceDefaults.Contracts;

[JsonConverter(typeof(JsonCamelCaseEnumConverter<AIChatRole>))]
public enum AIChatRole
{
    System,
    Assistant,
    User
}
