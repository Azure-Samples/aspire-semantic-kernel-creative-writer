// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Aspire.Hosting;
using Microsoft.Extensions.Hosting;

public static class DistributedApplicationHostingTestingExtensions
{
    private const string ConnectionStringEndpoint = "Endpoint";

    public static async Task<Uri> GetEndpointfromConnectionStringAsync(this DistributedApplication app, string resourceName, CancellationToken cancellationToken = default)
    {
        var connectionString = await app.GetConnectionStringFixedAsync(resourceName, cancellationToken);

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return uri;
        }
        else
        {
            var connectionBuilder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            if (connectionBuilder.ContainsKey(ConnectionStringEndpoint) && Uri.TryCreate(connectionBuilder[ConnectionStringEndpoint].ToString(), UriKind.Absolute, out var serviceUri))
            {
                return serviceUri;
            }

            throw new FormatException("ConnectionStringEndpointNotFound");
        }
    }

    /// <summary>
    /// Gets the connection string for the specified resource and workaround for bug https://github.com/dotnet/aspire/issues/7138
    /// </summary>
    public static ValueTask<string?> GetConnectionStringFixedAsync(this DistributedApplication app, string resourceName, CancellationToken cancellationToken = default)
    {
        var resource = GetResource(app, resourceName);
        if (resource is not IResourceWithConnectionString resourceWithConnectionString)
        {
            if (resource is not ParameterResource parameterResource)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Resource '{0}' does not expose a connection string.", resourceName), nameof(resourceName));
            }

            resourceWithConnectionString = new ResourceWithConnectionStringSurrogate(parameterResource, null);
        }

        return resourceWithConnectionString.GetConnectionStringAsync(cancellationToken);
    }

    static IResource GetResource(DistributedApplication app, string resourceName)
    {
        ThrowIfNotStarted(app);
        var applicationModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var resources = applicationModel.Resources;
        var resource = resources.SingleOrDefault(r => string.Equals(r.Name, resourceName, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "ResourceNotFoundExceptionMessage: {0}", resourceName), nameof(resourceName));
        }

        return resource;
    }

    static void ThrowIfNotStarted(DistributedApplication app)
    {
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        if (!lifetime.ApplicationStarted.IsCancellationRequested)
        {
            throw new InvalidOperationException("ApplicationNotStartedExceptionMessage");
        }
    }

    internal sealed class ResourceWithConnectionStringSurrogate(ParameterResource innerResource, string? environmentVariableName) : IResourceWithConnectionString
    {
        public string Name => innerResource.Name;

        public ResourceAnnotationCollection Annotations => innerResource.Annotations;

        public string? ConnectionStringEnvironmentVariable => environmentVariableName;

        public ReferenceExpression ConnectionStringExpression =>
            ReferenceExpression.Create($"{innerResource}");
    }
}