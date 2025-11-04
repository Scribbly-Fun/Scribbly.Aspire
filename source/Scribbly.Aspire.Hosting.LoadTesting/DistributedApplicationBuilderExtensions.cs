using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire;

public static class DistributedApplicationBuilderExtensions
{
    public static IResourceBuilder<K6ServerResource> AddLoadTesting(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string scriptDirectory = K6ServerResource.DefaultScriptDirectory,
        Action<K6ResourceOptions>? optionsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var options = new K6ResourceOptions();
        optionsCallback?.Invoke(options);
        
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
        
        return resourceBuilder.AddK6ContainerResource($"{name}-k6", scriptDirectory, options);
    }
}