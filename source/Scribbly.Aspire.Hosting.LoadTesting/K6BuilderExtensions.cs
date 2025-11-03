using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scribbly.Aspire.Grafana;
using Scribbly.Aspire.K6;

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
    public static IResourceBuilder<K6ServerResource> AddLoadTesting(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? scriptDirectory = null,
        Action<K6ResourceOptions>? optionsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var options = new K6ResourceOptions();
        optionsCallback?.Invoke(options);
        
        var path = scriptDirectory ?? K6ServerResource.DefaultScriptDirectory;
        
        var k6Server = new K6ServerResource(name, path);

        builder.Eventing.Subscribe<InitializeResourceEvent>(k6Server, (@event, ct) =>
        {
            var serverResource = (K6ServerResource)@event.Resource;
            var directory = serverResource.ScriptDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            return Task.CompletedTask;
        });

        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(k6Server, async (@event, token) =>
        {
            if (@event.Resource is not K6ServerResource server)
            {
                return;
            }
            if (server is { SelectedScript: not null })
            {
                return;
            }

            if (server.Initialized == false)
            {
                server.Initialized = true;
                return;
            }
#pragma warning disable ASPIREINTERACTION001
            var interaction = @event.Services.GetRequiredService<IInteractionService>();
            if (interaction.IsAvailable)
            { 
                await interaction.PromptNotificationAsync(
                    "No Script Selected",
                    "Please select a load test from the available script resources to execute",
                    new NotificationInteractionOptions{ ShowDismiss = true, Intent = MessageIntent.Warning }
                    , cancellationToken: token);
            }
#pragma warning restore ASPIREINTERACTION001
        });
        
        var healthCheckKey = $"{name}_check";
        
        builder.Services.AddHealthChecks().AddCheck(healthCheckKey, token => HealthCheckResult.Healthy());
        
        var resourceBuilder = builder
            .AddResource(k6Server)
            .WithIconName("TopSpeed")
            .WithImage(K6ContainerImageTags.Image, K6ContainerImageTags.Tag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithDataBindMount(path)
            .WithArgs(ct =>
            {
                if (ct.Resource is not K6ServerResource server || server.SelectedScript is null)
                {
                    return;
                }
                
                ct.Args.Add("run"); 
                ct.Args.Add(server.SelectedScript.ScriptArg);
                
                ct.Args.Add("--vus"); 
                ct.Args.Add(server.SelectedScript.VirtualUsers); 
                
                ct.Args.Add("--duration"); 
                ct.Args.Add($"{server.SelectedScript.Duration}s"); 
            })
            .WithEnvironment(ct =>
            {
                if (ct.Resource is not K6ServerResource server || server.SelectedScript is null)
                {
                    return;
                }
                
                var annotation = server.Annotations
                    .OfType<K6LoadTestScriptAnnotation>()
                    .FirstOrDefault(a => a.Script == server.SelectedScript.ScriptName);
                
                if (annotation is null)
                {
                    // When there is no annotation we assume you want to use the value from the script
                    return;
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
        var scriptResource = new K6ScriptResource(context, builder.Resource, scriptParameter.Resource);
        
        builder.Resource.AddScript(scriptResource);

        var scriptResourceBuilder = builder.ApplicationBuilder
            .AddResource(scriptResource)
            // .WithArgs(Path.Combine(Directory.GetCurrentDirectory(), builder.Resource.ScriptDirectory))
            ;

        scriptResourceBuilder.WithCommand(
            context.Name, 
            "Options", 
            commandOptions: new CommandOptions{ IconName = "TopSpeed"}, 
            executeCommand: async cmdContext =>
        {
#pragma warning disable ASPIREINTERACTION001
            var applicationModel = cmdContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
            var interaction = cmdContext.ServiceProvider.GetRequiredService<IInteractionService>();
            var script = applicationModel.Resources.FirstOrDefault(r =>
                cmdContext.ResourceName.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase));

            if (script is not K6ScriptResource resource)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = "Unknown Script" };
            }
            
            var results = await interaction.PromptInputsAsync(
                "Select a Load Test",
                "Please select a load test from the available script resources to execute",
                [
                    new InteractionInput
                    {
                        Name = "Virtual Users",
                        Placeholder = "10",
                        Value = "10",
                        InputType = InputType.Number,
                    },
                    new InteractionInput
                    {
                        Name = "Duration",
                        Placeholder = "30 Seconds",
                        Value = "30",
                        InputType = InputType.Number,
                    },
                ], cancellationToken: cmdContext.CancellationToken);
                
            if (results.Canceled || results.Data.Count != 2)
            {
                return new ExecuteCommandResult { Success = true };
            }
            
            resource.VirtualUsers = int.Parse(results.Data[0].Value!);
            resource.Duration = int.Parse(results.Data[1].Value!);
            
            return new ExecuteCommandResult { Success = true };
#pragma warning restore ASPIREINTERACTION001
        });

        scriptResourceBuilder.WithExplicitStart();
        
        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(scriptResource, async (@event, ct) =>
        {
            if (@event.Resource is not K6ScriptResource script)
            {
                throw new DistributedApplicationException("Script resource started on non-script resource");
            }

            if (script.Initialized)
            {
                script.Parent.SelectScript(script.Name);
                
                var commandService = @event.Services.GetRequiredService<ResourceCommandService>();

                await commandService.ExecuteCommandAsync(script.Parent.Name, "resource-start", ct);
                
                return;
            }

            script.Initialized = true;
        });
        
        return scriptResourceBuilder;
    }
    
    
    private static IResourceBuilder<K6ServerResource> WithDataBindMount(this IResourceBuilder<K6ServerResource> builder, string source, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, "/scripts", isReadOnly);
    }
    
}