using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire;

public class K6ServerResource : ContainerResource, IResourceWithConnectionString
{
    internal const string DefaultScriptDirectory = "./k6_scripts";
    
    private readonly Dictionary<string, K6ScriptResource.Context> _scripts = new (StringComparer.OrdinalIgnoreCase);
    
    /// <inheritdoc />
    public K6ServerResource(string name, ParameterResource? scriptDirectory) : base(name)
    {
        ScriptDirectoryParameter = scriptDirectory;
    }
    
    public ReferenceExpression ScriptDirectoryReference =>
        ScriptDirectoryParameter is not null ?
            ReferenceExpression.Create($"{ScriptDirectoryParameter}") :
            ReferenceExpression.Create($"{DefaultScriptDirectory}");
    
    private ReferenceExpression ConnectionString =>
        ReferenceExpression.Create(
            $"{ScriptDirectoryReference}");

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression
    {
        get
        {
            if (this.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var connectionStringAnnotation))
            {
                return connectionStringAnnotation.Resource.ConnectionStringExpression;
            }

            return ConnectionString;
        }
    }
    
    public ParameterResource? ScriptDirectoryParameter { get; set; }
    
    internal void AddScript(K6ScriptResource.Context script)
    {
        _scripts.TryAdd(script.Name, script);
    }

    internal bool TryGetScript(string name, [NotNullWhen(true)] out K6ScriptResource.Context? script) =>
        _scripts.TryGetValue(name, out script!);
}