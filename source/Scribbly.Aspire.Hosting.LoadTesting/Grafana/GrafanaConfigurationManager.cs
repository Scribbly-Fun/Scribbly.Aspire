using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.Grafana;

internal sealed class GrafanaConfigurationManager
{
    internal record ConfigContext(string Resource, string Target);
    
    private const string Namespace = "Scribbly.Aspire.cfg";
    
    private readonly string _scriptsDirectory;
    
    private readonly IReadOnlyCollection<ConfigContext> _files =
    [
        new ("grafana-dashboard.json", "dashboard.json"),
        new ("grafana-dashboard.yaml", "dashboard.yaml"),
        new ("grafana-datasource.yaml", "datasource.yaml")
    ];
    
    internal GrafanaConfigurationManager(string scriptsDirectory)
    {
        _scriptsDirectory = scriptsDirectory;
    }

    internal async ValueTask CopyConfigurationFiles(Func<ConfigContext, string, string> mutation, CancellationToken cancellation)
    {
        foreach (var file in _files)
        {
            await CopyConfigurationFile(file, mutation, cancellation);
        }
    }
    
    private async ValueTask CopyConfigurationFile(ConfigContext context, Func<ConfigContext, string, string> mutation, CancellationToken cancellation)
    {
        var (resourceName, target) = context;
        
        var path = Path.Combine(_scriptsDirectory, "grafana", target);

        if (File.Exists(path))
        {
            return;
        }
        
        var assembly = typeof(GrafanaResource).Assembly;
        await using var stream = assembly.GetManifestResourceStream($"{Namespace}.{resourceName}");

        if (stream is null)
        {
            throw new FileLoadException(resourceName);
        }
        
        using var reader = new StreamReader(stream);
        var fileData = await reader.ReadToEndAsync(cancellation);
        var updatedFile = mutation.Invoke(context, fileData);

        await File.WriteAllTextAsync(path, updatedFile, cancellation);
    }
    
    internal static string MutateDataSourceFile(string datasourceFile, InfluxResource influxResource)
    {
        var endpoint = influxResource.PrimaryEndpoint;
        return datasourceFile.Replace("http://influxdb:8086", $"{endpoint.EndpointName}://{influxResource.Name}:{endpoint.TargetPort}");
    }
}