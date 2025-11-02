// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using Aspire;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Scribbly.Aspire;

public sealed class K6ResourceOptions
{
    public bool UseGrafanaDashboard { get; set; } = true;

    public string DashboardContainerName { get; set; } = "grafana";
    public string DatabaseContainerName { get; set; } = "influx";
    
    internal static string? ScriptToRun { get; set; }
}

public static class K6BuilderExtensions
{

    public static IResourceBuilder<K6ServerResource> AddLoadTesting(this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? scriptDirectory = null,
        Action<K6ResourceOptions>? optionsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var options = new K6ResourceOptions();
        optionsCallback?.Invoke(options);

        
        var path = scriptDirectory ?? K6ServerResource.DefaultScriptDirectory;
        var scriptDirectoryParameter = builder.AddParameter("k6-scripts", path);
        
        builder.Services.AddSingleton(new ScriptExecutionContext(path));
        
        var scriptParameterResource = scriptDirectoryParameter.Resource;

        var k6Server = new K6ServerResource(name, scriptParameterResource);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(k6Server, async (@event, ct) =>
        {
            connectionString = await k6Server.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString == null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{k6Server.Name}' resource but the connection string was null.");
            }

            if (!Directory.Exists(connectionString))
            {
                Directory.CreateDirectory(connectionString);
            }
        });

        builder.Eventing.Subscribe<ResourceReadyEvent>(k6Server, (@event, ct) =>
        {
            return Task.CompletedTask;
        });

        var healthCheckKey = $"{name}_check";
        
        builder.Services.AddHealthChecks().AddCheck(healthCheckKey, token =>
        {
            return HealthCheckResult.Healthy();
        });
        
        
        var resourceBuilder = builder
            .AddResource(k6Server)
            .WithIconName("TopSpeed")
            .WithImage(K6ContainerImageTags.Image, K6ContainerImageTags.Tag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithDataBindMount(path)
            .WithArgs(ct =>
            {
                var executionContext = ct.ExecutionContext.ServiceProvider.GetRequiredService<ScriptExecutionContext>();
                
                if (executionContext.ScriptPath is null)
                {
                    return;
                }

                ct.Args.Add("run"); 
                ct.Args.Add(executionContext.ScriptPath);
            })
            .WithEnvironment(ct =>
            {
                var executionContext = ct.ExecutionContext.ServiceProvider.GetRequiredService<ScriptExecutionContext>();
                
                if (executionContext.ScriptName is null)
                {
                    return;
                }
                
                var applicationModel = ct.ExecutionContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                
                var serverResource = applicationModel.Resources.FirstOrDefault(r =>
                    ct.Resource.Name.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase));
                
                if (serverResource is not K6ServerResource server)
                {
                    throw new InvalidOperationException("Failed to find K6 Resource");
                }
                
                var annotation = server.Annotations.OfType<K6LoadTestScriptAnnotation>().FirstOrDefault(a => a.Script == executionContext.ScriptName);
                
                if (annotation is null)
                {
                    throw new InvalidOperationException("Unable to find script context");
                }

                var url = $"{annotation.Endpoint.Scheme}://host.docker.internal:{annotation.Endpoint.Port}";
                
                ct.EnvironmentVariables.Add("ASPIRE_RESOURCE", url);
            })
            .WithHealthCheck(healthCheckKey)
            .ExcludeFromManifest()
            .WithExplicitStart();
        
        var scripts = new DirectoryInfo(path).GetFiles("*.js");

        foreach (var fileInfo in scripts)
        {
            var scriptContext = new K6ScriptResource.Context(path, fileInfo);
            
            k6Server.AddScript(scriptContext);
            
            resourceBuilder.WithScriptResource(scriptContext, options);
            
            resourceBuilder.WithCommand(scriptContext.Name, $"Execute {scriptContext.Name}", async context =>
            {
                var applicationModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                var executionContext = context.ServiceProvider.GetRequiredService<ScriptExecutionContext>();
                
                var serverResource = applicationModel.Resources.FirstOrDefault(r =>
                    context.ResourceName.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase));
                
                if (serverResource is not K6ServerResource server)
                {
                    return new ExecuteCommandResult { Success = false, ErrorMessage = $"No K6 Resource Found to Execute the Script {K6ResourceOptions.ScriptToRun}" };
                }

                if (!server.TryGetScript(scriptContext.Name, out var executionCtx))
                {
                    return new ExecuteCommandResult { Success = false, ErrorMessage = $"Failed to Find Script to Run {scriptContext.Name}" };
                }

                executionContext.ScriptPath = executionCtx.Path;
                
                return await context.ServiceProvider.StartResource(server.Name);
            });
        }
        
        if (options.UseGrafanaDashboard)
        {
            var grafana = resourceBuilder.WithGrafanaDashboard(options);

            resourceBuilder.WithEnvironment(context =>
            {
                var applicationModel = context.ExecutionContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                var influxDbResource = applicationModel.Resources.FirstOrDefault(r =>
                    r.Name.Contains(options.DatabaseContainerName, StringComparison.OrdinalIgnoreCase));
                
                if (influxDbResource is IResourceWithEndpoints influxDatabase)
                {
                    var httpEndpoint = influxDatabase.GetEndpoint("http");
                    context.EnvironmentVariables.Add("K6_OUT", $"influxdb=http://{influxDbResource.Name}:{httpEndpoint.TargetPort}/k6");
                }
            });

            resourceBuilder.WaitFor(grafana);
        }

        return resourceBuilder;
    }
    
    public static IResourceBuilder<K6ServerResource> WithApiResourceForScript(
        this IResourceBuilder<K6ServerResource> builder, 
        string script, 
        IResourceBuilder<IResourceWithServiceDiscovery> source, 
        string endpointName = "http")
    {
        builder.WithAnnotation(new K6LoadTestScriptAnnotation(script, source, endpointName));
        builder.WithReference(source);
        return builder;
    }

    private static IResourceBuilder<K6ScriptResource> WithScriptResource(
        this IResourceBuilder<K6ServerResource> builder, K6ScriptResource.Context context, K6ResourceOptions options)
    {
        var scriptParameter = builder.ApplicationBuilder.AddParameter(context.ParameterName, context.Path);
        var scriptResource = new K6ScriptResource(context, builder.Resource.Name, scriptParameter.Resource);

        var scriptResourceBuilder = builder.ApplicationBuilder.AddResource(scriptResource);

        scriptResourceBuilder.WithCommand(context.Name, "Execute Test", async context =>
        {
            var applicationModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
            var script = applicationModel.Resources.FirstOrDefault(r =>
                r.Name.Contains(context.ResourceName, StringComparison.OrdinalIgnoreCase));

            if (script is not K6ScriptResource resource)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = "Unknown Script" };
            }
            
            var k6Resource = applicationModel.Resources.FirstOrDefault(r =>
                r.Name.Contains(resource.ContainerName, StringComparison.OrdinalIgnoreCase));

            if (k6Resource is not K6ServerResource server)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = $"No K6 Resource Found to Execute the Script {resource.ScriptFileParameter}" };
            }
            
            return await context.ServiceProvider.StartResource(server.Name);
        });

        scriptResourceBuilder.WithExplicitStart();
        return scriptResourceBuilder;
    }
    
    private static IResourceBuilder<InfluxResource> WithInfluxDatabase(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        var influx = new InfluxResource(options.DatabaseContainerName);
        var influxContainerBuilder = builder.ApplicationBuilder
            .AddResource(influx)
            .WithIconName("Clock")
            .WithImage(K6ContainerImageTags.InfluxImage, K6ContainerImageTags.InfluxTag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 8086, name: "http")
            .WithEnvironment("INFLUXDB_DB", "k6")
            .ExcludeFromManifest();
        
        influxContainerBuilder.WithRelationship(builder.Resource, "datastream");

        // influxContainerBuilder.WithHttpHealthCheck();

        return influxContainerBuilder;
    }
    
    private static IResourceBuilder<GrafanaResource> WithGrafanaDashboard(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var influxBuilder = builder.WithInfluxDatabase(options);
        
        var grafana = new GrafanaResource(options.DashboardContainerName);
        var grafanaContainerBuilder = builder.ApplicationBuilder
            .AddResource(grafana)
            .WithIconName("Dashboard")
            .WithImage(K6ContainerImageTags.GrafanaImage, K6ContainerImageTags.Tag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 3000, name: "http")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_BASIC_ENABLED", "false")
            .WithEnvironment("GF_SERVER_SERVE_FROM_SUB_PATH", "true")
            .WithBindMount("./scripts/grafana","/var/lib/grafana/dashboards")
            // .WithBindMount("./scripts/grafana-dashboard.yaml","/etc/grafana/provisioning/dashboards/dashboard.yaml")
            .WithBindMount("./scripts/grafana-datasource.yaml","/etc/grafana/provisioning/datasources/datasource.yaml")
            .ExcludeFromManifest();
        
        grafanaContainerBuilder
            .WithRelationship(builder.Resource, "dashboard")
            .WithReferenceRelationship(influxBuilder);

        grafanaContainerBuilder.WithHttpHealthCheck();

        grafanaContainerBuilder.WaitFor(influxBuilder);
        
        return grafanaContainerBuilder;
    }
    
    
    private static IResourceBuilder<K6ServerResource> WithDataBindMount(this IResourceBuilder<K6ServerResource> builder, string source, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, "/scripts", isReadOnly);
    }
    
}