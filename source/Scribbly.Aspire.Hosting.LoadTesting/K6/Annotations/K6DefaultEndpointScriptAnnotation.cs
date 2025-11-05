using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.K6.Annotations;

internal sealed class K6DefaultEndpointScriptAnnotation : IResourceAnnotation
{
    public string ApiResourceName { get; }

    public EndpointReference Endpoint { get; }
    
    public K6DefaultEndpointScriptAnnotation(IResourceBuilder<IResourceWithEndpoints> source, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(endpointName);
        
        var endpoint = source.GetEndpoint(endpointName);
        ApiResourceName = source.Resource.Name;
        Endpoint = endpoint;
    }
}