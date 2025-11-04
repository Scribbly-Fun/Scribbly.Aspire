using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
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
            .WithIconName("TopSpeed");
        
        return resourceBuilder.AddK6ContainerResource($"{name}-k6", scriptDirectory, options);
    }
}