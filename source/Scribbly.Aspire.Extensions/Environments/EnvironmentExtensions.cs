
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.Extensions;

/// <summary>
/// Extensions that effect hosting environments. 
/// </summary>
public static class EnvironmentExtensions
{
    /// <summary>
    /// Based on commentary by James Montemagno this pointless extension method is used to make this make sense for him.
    /// The WithEnvironmentVar extension since renames the function.
    /// </summary>
    /// <typeparam name="TResourceType">The resource the variable is being is applied to.</typeparam>
    /// <param name="resourceBuilder"></param>
    /// <param name="key">The key for the environment variable.</param>
    /// <param name="value">The value for the environment variable.</param>
    /// <returns>The resource with the variable applied.</returns>
    public static IResourceBuilder<TResourceType> WithEnvironmentVar<TResourceType>(
        this IResourceBuilder<TResourceType> resourceBuilder, 
        string key, 
        string value) 
        where TResourceType : IResourceWithEnvironment =>
        resourceBuilder.WithEnvironment(key, value);
}