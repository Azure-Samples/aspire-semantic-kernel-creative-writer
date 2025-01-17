// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit.Abstractions;

namespace ChatApp.EvaluationTests;

public class EvalQuestion : IXunitSerializable
{
    public required int ScenarioId { get; set; }

    public required string ResearchContext { get; set; }

    public required string ProductContext { get; set; }

    public required string AssignmentContext { get; set; }

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        ScenarioId = info.GetValue<int>("ScenarioId");
        ResearchContext = info.GetValue<string>("ResearchContext");
        ProductContext = info.GetValue<string>("ProductContext");
        AssignmentContext = info.GetValue<string>("AssignmentContext");
    }

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue("ScenarioId", ScenarioId);
        info.AddValue("ResearchContext", ResearchContext);
        info.AddValue("ProductContext", ProductContext);
        info.AddValue("AssignmentContext", AssignmentContext);
    }

    public override string ToString()
    {
        return $"Scenario = {ScenarioId}";
    }
}
