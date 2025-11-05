using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire;

/// <summary>
/// Options used to configure the Load Testing Resource
/// </summary>
public sealed class LoadTesterResourceOptions
{
    /// <summary>
    /// Provides access the K6 container builder so optional extension methods can be bolted on with exposing additional Aspire options to the caller.
    /// </summary>
    /// <remarks>See <see cref="K6BuilderExtensions"/> extension methods for available options.</remarks>
    /// <see cref="K6BuilderExtensions"/>
    internal IResourceBuilder<K6ServerResource> K6ResourceBuilder { get; }

    /// <summary>
    /// Creates a new options object with the K6 container resource.
    /// </summary>
    /// <param name="k6ResourceBuilder"></param>
    public LoadTesterResourceOptions(IResourceBuilder<K6ServerResource> k6ResourceBuilder)
    {
        K6ResourceBuilder = k6ResourceBuilder;
    }
}