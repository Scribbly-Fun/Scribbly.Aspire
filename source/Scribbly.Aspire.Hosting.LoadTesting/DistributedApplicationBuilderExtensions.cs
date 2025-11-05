using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire;

/// <summary>
/// Aspire Application builder extensions for the <see cref="LoadTesterResource"/>
/// </summary>
public static class DistributedApplicationBuilderExtensions
{
    /// <summary>
    /// Adds a load testing resource to your application.
    /// This allows Aspire endpoint resources to bind to K6 scripts and run load tests.
    /// </summary>
    /// <param name="builder">The application builder</param>
    /// <param name="name">The resource name <remarks>defaults to load-tester</remarks></param>
    /// <param name="scriptDirectory">
    /// The directory that will be used for all script resources
    /// <remarks>
    ///     This directory will be created if it doesn't exist and a default example-test.js file will be copied.
    ///     When using the grafana dashboard a subdirectory /grafana will be created and container configuration will be mounted to this directory
    /// </remarks>
    /// </param>
    /// <param name="optionsCallback">Load test options used to enable dashboards and other configuration choices.</param>
    /// <returns>The load test builder used to create and bind scripts to endpoints.</returns>
    /// <remarks>The dashboard will NOT be enabled by default, use the optionsCallback to enable dashboard displays.</remarks>
    public static IResourceBuilder<LoadTesterResource> AddLoadTesting(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "load-tester",
        string scriptDirectory = K6ServerResource.DefaultScriptDirectory,
        Action<LoadTesterResourceOptions>? optionsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        
        var resource = new LoadTesterResource(name, scriptDirectory);
        
        var resourceBuilder = builder
            .AddResource(resource)
            .WithIconName("TopSpeed")
            .WithInitialState(new CustomResourceSnapshot 
            {
                ResourceType = "LoadTest", 
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Waiting, 
                Properties =
                [
                    new ResourcePropertySnapshot(CustomResourceKnownProperties.Source, "Load Test"),
                    new ResourcePropertySnapshot(CustomResourceKnownProperties.ConnectionString, scriptDirectory),
                ]
            });
        
        resourceBuilder.AddK6ContainerResource($"{name}-k6", scriptDirectory, optionsCallback);

        return resourceBuilder;
    }
}