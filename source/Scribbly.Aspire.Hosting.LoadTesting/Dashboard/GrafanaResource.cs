using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire.Grafana;

public sealed class GrafanaResource(string name, LoadTesterResource parent) : ContainerResource(name), IResourceWithParent<LoadTesterResource>
{
    internal const string PrimaryEndpointName = "http";

    private EndpointReference? _primaryEndpoint;
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <inheritdoc />
    public LoadTesterResource Parent { get; } = parent;
}