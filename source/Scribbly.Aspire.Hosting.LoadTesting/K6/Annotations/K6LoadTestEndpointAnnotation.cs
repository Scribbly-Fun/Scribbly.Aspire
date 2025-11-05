using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire;

internal sealed class K6LoadTestScriptAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Gets the script used to create the database.
    /// </summary>
    public string Script { get; }

    public string ApiResourceName { get; }

    public EndpointReference Endpoint { get; }
    
    public K6LoadTestScriptAnnotation(string script, IResourceBuilder<IResourceWithEndpoints> source, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(script);
        
        var endpoint = source.GetEndpoint(endpointName);
        Script = script;
        ApiResourceName = source.Resource.Name;
        Endpoint = endpoint;
    }
}