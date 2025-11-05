using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scribbly.Aspire.Dashboard;
using Scribbly.Aspire.K6.Annotations;

namespace Scribbly.Aspire.K6;

public static class K6BuilderExtensions
{
    internal static IResourceBuilder<K6ServerResource> AddK6ContainerResource(
        this IResourceBuilder<LoadTesterResource> builder,
        [ResourceName] string name,
        string path,
        Action<LoadTesterResourceOptions>? optionsCallback = null)
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
        
        var k6ContainerResource = builder.ApplicationBuilder
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
                
                var scriptBindingAnnotation = server.Annotations
                    .OfType<K6LoadTestScriptAnnotation>()
                    .FirstOrDefault(a => a.Script == server.SelectedScript.Name);
                
                if (scriptBindingAnnotation is null)
                {
                    var fallbackAnnotation = server.Parent.Annotations
                        .OfType<K6DefaultEndpointScriptAnnotation>()
                        .FirstOrDefault();

                    if (fallbackAnnotation is null)
                    {
                        // We assume you have hard coded URLs in the script.
                        return;
                    }
                    
                    var defaultUrl = $"{fallbackAnnotation.Endpoint.Scheme}://host.docker.internal:{fallbackAnnotation.Endpoint.Port}";
                    ct.EnvironmentVariables.Add("ASPIRE_RESOURCE", defaultUrl);
                    
                    return;
                }
                
                var url = $"{scriptBindingAnnotation.Endpoint.Scheme}://host.docker.internal:{scriptBindingAnnotation.Endpoint.Port}";
                ct.EnvironmentVariables.Add("ASPIRE_RESOURCE", url);
            })
            .WithHealthCheck(healthCheckKey)
            .ExcludeFromManifest()
            .WithExplicitStart();
        
        var options = new LoadTesterResourceOptions(k6ContainerResource);
        optionsCallback?.Invoke(options);

        var scripts = new DirectoryInfo(path).InitializeScriptDirectory();
        foreach (var fileInfo in scripts)
        {
            k6ContainerResource.WithScriptResource(fileInfo);
        }

        return k6ContainerResource;
    }

    /// <summary>
    /// Adds a grafana dashboard to the K6 resource used to display live data and results.
    /// </summary>
    /// <param name="options">The K6 container builder.</param>
    /// <param name="grafanaResourceName">The name to use for the dashboard resource</param>
    /// <returns>The container linked to a grafana dashboard resource.</returns>
    public static LoadTesterResourceOptions WithGrafanaDashboard(
        this LoadTesterResourceOptions options,
        [ResourceName] string grafanaResourceName = "dashboard")
    {
        options.K6ResourceBuilder
            .UseGrafanaDashboard(grafanaResourceName)
            .WithEnvironment(context =>
            {
                if (context.Resource is not GrafanaResource { Parent.Server.OutputDatabase: not null } dashboard)
                {
                    return;
                }

                var database = dashboard.Parent.Server.OutputDatabase;
                
                var httpEndpoint = database.GetEndpoint("http");
                context.EnvironmentVariables.Add("K6_OUT", $"influxdb=http://{database.Name}:{httpEndpoint.TargetPort}/k6");
            });

        return options;
    }
    
    /// <summary>
    /// Adds the k6 default dashboard to the K6 docker container.
    /// </summary>
    /// <param name="options">The load test resource options.</param>
    /// <returns>The k6 builder with a built-in dashboard</returns>
    public static LoadTesterResourceOptions WithBuiltInDashboard(
        this LoadTesterResourceOptions options)
    {
        options.K6ResourceBuilder
            .WithEnvironment("K6_WEB_DASHBOARD", "true")
            .WithEnvironment("K6_WEB_DASHBOARD_EXPORT", "loadtest.html")
            .WithEnvironment("K6_WEB_DASHBOARD_PERIOD", "3s")
            .WithHttpEndpoint(targetPort: 5665, name: "k6-dashboard")
            .WithUrl("/ui/?endpoint=/", "🎯 Load Test Results");

        return options;
    }

    /// <summary>
    /// Adds Open Telemetry to the K6 container and allows exported traces to be displayed on the Aspire dashboard.
    /// </summary>
    /// <param name="options">The k6 Container resource Builder</param>
    /// <returns>The k6 container resource with OTEL added.</returns>
    public static LoadTesterResourceOptions WithOtlpEnvironment(
        this LoadTesterResourceOptions options)
    {
        options.K6ResourceBuilder
            .WithOtlpExporter()
            .WithEnvironment(context =>
        {
            foreach (var (key, value) in context.EnvironmentVariables.ToList())
            {
                if (key.StartsWith("OTEL_"))
                {
                    context.EnvironmentVariables.TryAdd($"K6_{key}", value);
                }
            }
        });
        return options;
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
    
    /// <summary>
    /// Provides a default API resource to use for all scripts.
    /// Scribbly will determine the endpoint URL and inject it into your script using the ASPIRE_RESOURCE variable.
    /// To use an Aspire endpoint in you script you must access the ASPIRE_RESOURCE env variable.  See the example-test.js file.
    /// </summary>
    /// <param name="builder">The load test builder containing scripts.</param>
    /// <param name="source">The source API resource or resource with endpoints.</param>
    /// <param name="endpointName">
    /// The name of the endpoint
    /// <remarks>We default to HTTP assuming your request will use the docker network.</remarks>
    /// </param>
    /// <returns>The load tester with endpoint bindings.</returns>
    /// <remarks>
    ///     When no default API resource is provided Scribbly assumes you plan to use a hardcoded URL inside the script.
    ///     Scribbly will not inject the ASPIRE_RESOURCE url into your script.
    ///
    ///     Any script bound to an endpoint with the <see cref="WithApiResourceForScript"/> method will override this api resource.
    /// </remarks>
    public static IResourceBuilder<LoadTesterResource> WithDefaultApiResourceForScripts(
        this IResourceBuilder<LoadTesterResource> builder, 
        IResourceBuilder<IResourceWithEndpoints> source, 
        [EndpointName] string endpointName = "http")
    {
        builder.WithAnnotation(new K6DefaultEndpointScriptAnnotation(source, endpointName));
        return builder;
    }
    
    /// <summary>
    /// The with api resource for script binds the ASPIRE_RESOURCE environment variable to the API resources http endpoint.
    /// This enables to the script to target a specific API resource.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="script"></param>
    /// <param name="source"></param>
    /// <param name="endpointName"></param>
    /// <returns></returns>
    /// <remarks>
    ///     As of 11.5.2025 this is a 1:1 relationship but will be updated to support any combination of script to endpoint
    ///     Scripts that are discovered but not bound to a resource will use the default API resource.
    /// </remarks>
    public static IResourceBuilder<LoadTesterResource> WithApiResourceForScript(
        this IResourceBuilder<LoadTesterResource> builder, 
        string script, 
        IResourceBuilder<IResourceWithEndpoints> source, 
        string endpointName = "http")
    {
        builder.ApplicationBuilder
            .CreateResourceBuilder(builder.Resource.Server!)
            .WithAnnotation(new K6LoadTestScriptAnnotation(script, source, endpointName));
        
        return builder;
    }
}