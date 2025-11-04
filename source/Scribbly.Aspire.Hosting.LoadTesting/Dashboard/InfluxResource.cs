using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire.Dashboard;

public sealed class InfluxResource(string name, K6ServerResource parent) : ContainerResource(name), IResourceWithParent<K6ServerResource>
{
    internal const string PrimaryEndpointName = "http";

    private EndpointReference? _primaryEndpoint;
    
    /// <inheritdoc />
    public K6ServerResource Parent { get; } = parent;
    
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

}