using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.Grafana;

namespace Scribbly.Aspire.K6;

public class K6ServerResource : ContainerResource
{
    internal const string DefaultScriptDirectory = "./k6_scripts";
    
    private readonly Dictionary<string, K6ScriptResource.Context> _scripts = new (StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointReference> _endpoints = new (StringComparer.OrdinalIgnoreCase);
    
    private readonly List<K6ScriptResource> _scriptResources = [];
    internal IReadOnlyList<K6ScriptResource> ScriptResources => _scriptResources;

    public string ScriptDirectory { get; set; }
    
    /// <inheritdoc />
    public K6ServerResource(string name, string scriptDirectory) : base(name)
    {
        ScriptDirectory = scriptDirectory;
    }
    
    public K6ScriptResource? SelectedScript { get; private set; }

    internal void SelectScript(string name)
    {
        SelectedScript = _scriptResources.FirstOrDefault(s => s.Name == name);
    } 
    
    internal void AddScript(K6ScriptResource.Context script)
    {
        _scripts.TryAdd(script.Name, script);
    }
    
    internal void AddScript(K6ScriptResource resource)
    {
        _scriptResources.Add(resource);
    }
    
    internal void AddEndpoint(EndpointReference endpoint, string script)
    {
        _endpoints.TryAdd(script, endpoint);
    }

    internal bool TryGetScript(string name, [NotNullWhen(true)] out K6ScriptResource.Context? script) =>
        _scripts.TryGetValue(name, out script!);
    
    internal bool TryGetEndpoint(string script, [NotNullWhen(true)] out EndpointReference? endpoint) =>
        _endpoints.TryGetValue(script, out endpoint!);
    
    public InfluxResource? OutputDatabase { get; private set; }

    internal void AddInfluxDatabase(InfluxResource database)
    {
        OutputDatabase = database;
    }
}