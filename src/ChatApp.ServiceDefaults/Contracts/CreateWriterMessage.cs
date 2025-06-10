// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace ChatApp.ServiceDefaults.Contracts;

public sealed class CreateWriterRequest
{
    public required string Research { get; init; }
    public required string Products { get; init; }
    public required string Writing { get; init; }
    public string? UserFeedback { get; init; }
    public string? PreviousArticle { get; init; }
}
