// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using Aspire.Hosting;
using Azure.AI.OpenAI;
using Azure.Identity;
using ChatApp.ServiceDefaults.Clients.Backend;
using ChatApp.ServiceDefaults.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage.Disk;
using Microsoft.ML.Tokenizers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ChatApp.EvaluationTests;

public class EvaluationTests : IAsyncLifetime
{
    private DistributedApplication _app = null;
    private Settings _settings = null;
    private BackendClient? backendClient = null;

    private static ISerializer? yamlSerializer = null;
    private static readonly string ExecutionName = $"{DateTime.UtcNow:yyyyMMddTHHmmss}";

    public async Task InitializeAsync()
    {
        yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Init and start Aspire app for testing
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ChatApp_AppHost>();
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        _app = await appHost.BuildAsync();
        var resourceNotificationService = _app.Services.GetRequiredService<ResourceNotificationService>();
        await _app.StartAsync();

        // Retrieve config settings from the backend resource
        var httpClient = _app.CreateHttpClient("backend");
        await resourceNotificationService.WaitForResourceAsync("backend", KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));
        var response = await httpClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var backend = (IResourceWithEnvironment)appHost.Resources.Single(static r => r.Name == "backend");
        var backendEnvVars = await backend.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        // Configure the clients
        var openAiEndpoint = await _app.GetEndpointfromConnectionStringAsync("openAi");
        var deploymentName = backendEnvVars["AzureDeployment"];
        _settings = Settings.GetCurrentSettings(openAiEndpoint, deploymentName);
        backendClient = new BackendClient(httpClient);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    static EvalQuestion[] LoadEvaluationQuestions()
    {
        var questionDataPath = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "EvalQuestionsJsonPath").Value!;
        var test = AppContext.BaseDirectory;
        if (!File.Exists(questionDataPath))
        {
            throw new FileNotFoundException("Questions not found. Ensure the data ingestor has run.", questionDataPath);
        }
        var questionsJson = File.ReadAllText(questionDataPath);
        return JsonSerializer.Deserialize<EvalQuestion[]>(questionsJson)!;
    }

    public static TheoryData<EvalQuestion> EvalQuestions => [.. LoadEvaluationQuestions().OrderBy(a => a.ScenarioId).Take(5)];

    [Theory]
    [MemberData(nameof(EvalQuestions))]
    public async Task EvaluateQuestionsWithMemberData(EvalQuestion question)
    {
        var reportingConfiguration = GetReportingConfiguration();
        for (int i = 0; i < 1; i++)
        {
            var result = await EvaluateQuestion(question, reportingConfiguration, i, CancellationToken.None);

            // NOTE: Samples if you want to check the metrics during the test:

            // var coherenceMetric = result.Get<NumericMetric>(CoherenceEvaluator.CoherenceMetricName);
            // Assert.False(coherenceMetric.Interpretation!.Failed,
            //     $"{coherenceMetric.Interpretation.Reason} {string.Join(", ", coherenceMetric.Diagnostics.Select(d => d.Message))}");

            // var groundednessMetric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
            // Assert.False(groundednessMetric.Interpretation!.Failed,
            //     $"{groundednessMetric.Interpretation.Reason} {string.Join(", ", groundednessMetric.Diagnostics.Select(d => d.Message))}");
        }
    }

    private ReportingConfiguration GetReportingConfiguration()
    {
        var azureClient = new AzureOpenAIClient(_settings.OpenAIEndpoint, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true }));

        IChatClient chatClient = azureClient.AsChatClient(_settings.DeploymentName);
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel(_settings.ModelName);

        var chatConfig = new ChatConfiguration(chatClient, tokenizer.ToTokenCounter(6000));

        return DiskBasedReportingConfiguration.Create(
                storageRootPath: _settings.StorageRootPath,
                chatConfiguration: chatConfig,
                evaluators: GetEvaluators(),
                executionName: ExecutionName);
    }

    private static IEnumerable<IEvaluator> GetEvaluators()
    {
        // Measures the extent to which the model's generated responses are pertinent and directly related to the given queries.
        yield return new RelevanceTruthAndCompletenessEvaluator(
                new RelevanceTruthAndCompletenessEvaluator.Options(includeReasoning: true));

        // Measures how well the language model can produce output that flows smoothly, reads naturally, and resembles human-like language.
        yield return new CoherenceEvaluator();

        // Measures the grammatical proficiency of a generative AI's predicted answer.
        yield return new FluencyEvaluator();

        // Measures how well the model's generated answers align with information from the source data
        yield return new GroundednessEvaluator();
    }

    private async Task<EvaluationResult> EvaluateQuestion(EvalQuestion question, ReportingConfiguration reportingConfiguration, int i, CancellationToken cancellationToken)
    {
        await using ScenarioRun scenario = await reportingConfiguration
            .CreateScenarioRunAsync($"Scenario_{question.ScenarioId}", $"Iteration {i + 1}", cancellationToken: cancellationToken);

        CreateWriterRequest createWriterRequest = new()
        {
            Research = question.ResearchContext,
            Products = question.ProductContext,
            Writing = question.AssignmentContext
        };

        var responseItems = backendClient!.ChatAsync(new AIChatRequest(
            [
                new AIChatMessage{ Content = yamlSerializer!.Serialize(createWriterRequest) }
            ]), cancellationToken);


        var researchContext = "";
        var productsContext = "";
        var finalAnswer = "";
        await foreach (var item in responseItems)
        {
            if (item.Delta.Role == AIChatRole.Assistant && item.Delta.Context?.Name == "Researcher")
            {
                researchContext = item.Delta.Content;
            }
            if (item.Delta.Role == AIChatRole.Assistant && item.Delta.Context?.Name == "Marketing")
            {
                productsContext = item.Delta.Content;
            }
            if (item.Delta.Role == AIChatRole.Assistant && item.Delta.Context?.Name == "Writer")
            {
                finalAnswer = item.Delta.Content;
            }
        }

        EvaluationResult evalResult = await scenario.EvaluateAsync(
            [
                new ChatMessage(ChatRole.User, "research_context: " + question.ResearchContext),
                new ChatMessage(ChatRole.User, "product_context: " + question.ProductContext),
                new ChatMessage(ChatRole.User, "assignment_context: " + question.AssignmentContext),
            ],
            new ChatMessage(ChatRole.Assistant, finalAnswer),
            additionalContext: [
                new GroundednessEvaluator.Context(JsonSerializer.Serialize(new { research = researchContext, products = productsContext }))
            ],
            cancellationToken);

        foreach (var metricResult in evalResult.Metrics.Values)
        {
            if (metricResult.Interpretation == null)
            {
                Console.WriteLine($"Metric {metricResult.Name} did not have an interpretation. Diagnostics: {string.Join(", ", metricResult.Diagnostics.Select(d => d.Message))}");
            }
        }

        // Assert that the evaluator was able to successfully generate an analysis
        Assert.False(evalResult.Metrics.Values.Any(m => m.Interpretation!.Rating == EvaluationRating.Inconclusive), "Model response was inconclusive");

        // Assert that the evaluators did not report any diagnostic errors
        Assert.False(evalResult.ContainsDiagnostics(d => d.Severity == EvaluationDiagnosticSeverity.Error), "Evaluation had errors.");

        return evalResult;
    }
}
