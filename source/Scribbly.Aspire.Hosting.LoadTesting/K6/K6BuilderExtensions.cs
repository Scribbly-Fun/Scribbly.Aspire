using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scribbly.Aspire.Dashboard;

namespace Scribbly.Aspire.K6;

public static class K6BuilderExtensions
{
    internal static IResourceBuilder<K6ServerResource> AddK6ContainerResource(
        this IResourceBuilder<LoadTesterResource> builder,
        [ResourceName] string name,
        string path,
        K6ResourceOptions options)
    {
        var k6Server = new K6ServerResource(name,path, builder.Resource);
        builder.Resource.AddK6Server(k6Server);
        
        builder.ApplicationBuilder.Services.AddSingleton(new ConfigurationFileManager(path));

        builder.ApplicationBuilder.Eventing.Subscribe<InitializeResourceEvent>(k6Server, async (@event, ct) =>
        {
            var serverResource = (K6ServerResource)@event.Resource;
            var directory = serverResource.ScriptDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var configManager = @event.Services.GetRequiredService<ConfigurationFileManager>();
            await configManager.CopyConfigurationFile(
                new ConfigurationFileManager.ConfigContext("execute-script.ps1", "execute-script.ps1"), 
                cancellation: ct);
            
        });

        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(k6Server, async (@event, token) =>
        {
            if (@event.Resource is not K6ServerResource server)
            {
                return;
            }
            if (server is { SelectedScript: not null })
            {
                return;
            }

            if (!server.HasResourceBeenInitialized())
            {
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
        
        builder.ApplicationBuilder.Services.AddHealthChecks().AddCheck(healthCheckKey, token => HealthCheckResult.Healthy());
        
        var resourceBuilder = builder.ApplicationBuilder
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
                    .FirstOrDefault(a => a.Script == server.SelectedScript.Name);
                
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

        var scripts = new DirectoryInfo(path).InitializeScriptDirectory();
        foreach (var fileInfo in scripts)
        {
            resourceBuilder.WithScriptResource(fileInfo);
        }
        
        if (options.UseGrafanaDashboard)
        {
            resourceBuilder.WithGrafanaDashboard(options);

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
        this IResourceBuilder<K6ServerResource> builder, FileInfo scriptFile)
    {
        var scriptResource = K6ScriptResource.CreateScriptResource(scriptFile, builder.Resource.Parent);
        
        builder.Resource.AddScript(scriptResource);

        var scriptResourceBuilder = builder.ApplicationBuilder
            .AddResource(scriptResource)
            .WithIconName("DocumentJavascript")
            .WithArgs(ct =>
            {
                ct.Args.Add("-ExecutionPolicy");
                ct.Args.Add("Bypass");
                ct.Args.Add("-File");
                ct.Args.Add("./execute-script.ps1");
                ct.Args.Add("-RelativePath");
                ct.Args.Add("..\\" + scriptResource.ScriptArg);
            });

        scriptResourceBuilder.WithCommand(
            scriptResource.Name, 
            "Options", 
            commandOptions: new CommandOptions{ IconName = "TopSpeed"}, 
            executeCommand: async cmdContext =>
        {
            var applicationModel = cmdContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
            var script = applicationModel.Resources.FirstOrDefault(r =>
                cmdContext.ResourceName.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase));

            if (script is not K6ScriptResource resource)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = "Unknown Script" };
            }
            
#pragma warning disable ASPIREINTERACTION001
            var interaction = cmdContext.ServiceProvider.GetRequiredService<IInteractionService>();
            var commandService = cmdContext.ServiceProvider.GetRequiredService<ResourceCommandService>();
            
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

            return await commandService.ExecuteCommandAsync(script.Name, "resource-start");
#pragma warning restore ASPIREINTERACTION001
        });

        scriptResourceBuilder.WithExplicitStart();
        
        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(scriptResource, async (@event, ct) =>
        {
            if (@event.Resource is not K6ScriptResource script || script.Parent.Server is null)
            {
                throw new DistributedApplicationException("Script resource started on non-script resource");
            }

            if (!script.HasResourceBeenInitialized())
            {
                return;
            }
            
            script.Server.SelectScript(script.Name);
            var commandService = @event.Services.GetRequiredService<ResourceCommandService>();
            await commandService.ExecuteCommandAsync(script.Parent.Server.Name, "resource-start", ct);
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