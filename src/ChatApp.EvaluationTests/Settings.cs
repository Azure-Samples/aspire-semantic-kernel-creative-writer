// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;

namespace ChatApp.EvaluationTests;

public class Settings
{
    public readonly Uri OpenAIEndpoint;
    public readonly string DeploymentName;
    public readonly string ModelName;
    public readonly string StorageRootPath;

    public Settings(IConfiguration config)
    {
        OpenAIEndpoint = new Uri(config.GetValue<string>("OpenAIEndpoint")!);
        DeploymentName = config.GetValue<string>("DeploymentName") ?? throw new ArgumentNullException(nameof(DeploymentName));
        ModelName = config.GetValue<string>("ModelName") ?? throw new ArgumentNullException(nameof(ModelName));
        StorageRootPath = config.GetValue<string>("StorageRootPath") ?? throw new ArgumentNullException(nameof(StorageRootPath));
    }

    public static Settings GetCurrentSettings(Uri? openAIEndpoint, string deploymentName)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"OpenAIEndpoint", openAIEndpoint?.ToString()},
                {"DeploymentName", deploymentName}
            })
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        IConfigurationRoot config = builder.Build();

        return new Settings(config);
    }
}
