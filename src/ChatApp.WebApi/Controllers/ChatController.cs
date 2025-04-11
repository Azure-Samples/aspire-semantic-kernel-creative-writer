// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChatApp.ServiceDefaults.Contracts;
using ChatApp.WebApi.Agents;
using ChatApp.WebApi.Model;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ChatApp.WebApi.Controllers;

[ApiController, Route("[controller]")]
public class ChatController : ControllerBase
{
    private readonly CreativeWriterApp _creativeWriterApp;
    private readonly IDeserializer _yamlDeserializer;

    public ChatController(CreativeWriterApp creativeWriterApp)
    {
        _creativeWriterApp = creativeWriterApp;

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    [HttpPost("stream")]
    [Consumes("application/json")]
    public async Task ProcessStreamingMessage(AIChatRequest request)
    {
        var response = Response;
        response.Headers.Append("Content-Type", "application/x-ndjson");

        var session = await _creativeWriterApp.CreateSessionAsync();

        try
        {
            var userInput = request.Messages.Last();
            CreateWriterRequest createWriterRequest = _yamlDeserializer.Deserialize<CreateWriterRequest>(userInput.Content);

            await foreach (var delta in session.ProcessStreamingRequest(createWriterRequest))
            {
                await response.WriteAsync($"{JsonSerializer.Serialize(delta)}\r\n");
                await response.Body.FlushAsync();
            }
        }
        catch (YamlException ex)
        {
            var delta = new AIChatCompletionDelta(Delta: new AIChatMessageDelta
            {
                Role = AIChatRole.System,
                Content = "Error: Invalid YAML format, Details:  \n" + ex,
            });
            await response.WriteAsync($"{JsonSerializer.Serialize(delta)}\r\n");
            await response.Body.FlushAsync();
        }
        finally
        {
            // cleanup the session. e.g. delete the AI Agent Service Agent
            await session.CleanupSessionAsync();
        }
    }
}
