using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Scribbly.Aspire;

/// <summary>
/// 
/// </summary>
public static class ResourceCommandExtensions
{
    private const string StartCommand = "resource-start";
    private const string StopCommand = "resource-stop";

    /// <summary>
    /// Uses the service provider to obtain access to the specified resource and starts it
    /// </summary>
    /// <param name="provider">The service provider from the Aspire App Host project.</param>
    /// <param name="resourceName">The name of the resource you want to start</param>
    /// <returns>The result of the issued start command</returns>
    /// <exception cref="InvalidOperationException">Throws when the resource can't be located or an unsupported version of Aspire is used</exception>
    public static async Task<ExecuteCommandResult> StartResource(this IServiceProvider provider, string resourceName)
    {
        var applicationModel = provider.GetRequiredService<DistributedApplicationModel>();
        var resources = applicationModel.Resources;
        var resource = resources.FirstOrDefault(r => resourceName.Contains(r.Name, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            throw new InvalidOperationException($"Unknown Resource Name {resourceName}");
        }

        if (!resource.TryGetAnnotationsOfType<ResourceCommandAnnotation>(out var commands))
        {
            throw new InvalidOperationException($"Cannot get {nameof(ResourceCommandAnnotation)}, use at least Aspire 9.1");
        }

        // TODO: Determine if the resource is already started

        var resourceCommandAnnotation = commands.First(a => a.Name == StartCommand);

        var result = await resourceCommandAnnotation.ExecuteCommand(new ExecuteCommandContext
        {
            ServiceProvider = provider,
            ResourceName = resourceName,
            CancellationToken = CancellationToken.None,
        });

        return result;
    }

    /// <summary>
    /// Uses the service provider to obtain access to the specified resource and stops it
    /// </summary>
    /// <param name="provider">The service provider from the Aspire App Host project.</param>
    /// <param name="resourceName">The name of the resource you want to stop</param>
    /// <returns>The result of the issued stop command</returns>
    /// <exception cref="InvalidOperationException">Throws when the resource can't be located or an unsupported version of Aspire is used</exception>
    public static async Task<ExecuteCommandResult> StopResource(this IServiceProvider provider, string resourceName)
    {
        var applicationModel = provider.GetRequiredService<DistributedApplicationModel>();
        var resources = applicationModel.Resources;
        var resource = resources.FirstOrDefault(r => resourceName.Contains(r.Name, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            throw new InvalidOperationException($"Unknown Resource Name {resourceName}");
        }

        if (!resource.TryGetAnnotationsOfType<ResourceCommandAnnotation>(out var commands))
        {
            throw new Exception($"Cannot get {nameof(ResourceCommandAnnotation)}, use at least Aspire 9.1");
        }

        // TODO: Determine if the resource is already stopped

        var resourceCommandAnnotation = commands.First(a => a.Name == StopCommand);

        var result = await resourceCommandAnnotation.ExecuteCommand(new ExecuteCommandContext
        {
            ServiceProvider = provider,
            ResourceName = resourceName,
            CancellationToken = CancellationToken.None
        });

        return result;
    }
}

