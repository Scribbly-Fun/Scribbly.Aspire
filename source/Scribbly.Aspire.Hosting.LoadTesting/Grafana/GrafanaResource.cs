using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire.Grafana;

public sealed class GrafanaResource(string name, InfluxResource database, K6ServerResource server) : ContainerResource(name)
{
    internal InfluxResource Database { get; } = database;
    
    public K6ServerResource Parent { get; } = server;

    internal const string PrimaryEndpointName = "http";

    private EndpointReference? _primaryEndpoint;
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);
}