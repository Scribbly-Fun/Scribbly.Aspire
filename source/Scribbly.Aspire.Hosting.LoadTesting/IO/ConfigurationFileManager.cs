using Scribbly.Aspire.Dashboard;

namespace Scribbly.Aspire;

/// <summary>
/// Ensure the configuration files required are created and stored in the correct locations.
/// </summary>
/// <remarks>All default files are stored in the manifest resource stream and mutated with Aspire data.</remarks>
internal sealed class ConfigurationFileManager
{
    internal record ConfigContext(string Resource, string TargetFile, string? TargetDirectory = null);
    
    internal const string Namespace = "Scribbly.Aspire.cfg";
    
    private readonly string _scriptsDirectory;
    
    private readonly SemaphoreSlim _lock = new (1);
    
    private readonly IReadOnlyCollection<ConfigContext> _grafanaConfigurationFiles =
    [
        new ("grafana-dashboard.json", "dashboard.json", "grafana"),
        new ("grafana-dashboard.yaml", "dashboard.yaml", "grafana"),
        new ("grafana-datasource.yaml", "datasource.yaml", "grafana"),
    ];
    
    internal ConfigurationFileManager(string scriptsDirectory)
    {
        _scriptsDirectory = scriptsDirectory;
    }

    internal async ValueTask CopyGrafanaConfigurationFiles(Func<ConfigContext, string, string> mutation, CancellationToken cancellation)
    {
        await _lock.WaitAsync(cancellation);

        try
        {
            foreach (var file in _grafanaConfigurationFiles)
            {
                await CopyConfigurationFile(file, mutation, cancellation);
            }
        }
        finally
        {
            _lock.Release();
        } 
    }
    
    internal async ValueTask CopyConfigurationFile(ConfigContext context, Func<ConfigContext, string, string>? mutation = null, CancellationToken cancellation = default)
    {
        var (resourceName, target, targetDirectory) = context;

        var directory = string.IsNullOrEmpty(targetDirectory)
            ? _scriptsDirectory
            : Path.Combine(_scriptsDirectory, targetDirectory);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var path = Path.Combine(directory, target);

        if (File.Exists(path))
        {
            return;
        }
    
        var assembly = typeof(LoadTesterResource).Assembly;
        await using var stream = assembly.GetManifestResourceStream($"{Namespace}.{resourceName}");

        if (stream is null)
        {
            throw new FileLoadException(resourceName);
        }
    
        using var reader = new StreamReader(stream);
        var fileData = await reader.ReadToEndAsync(cancellation);

        if (mutation is not null)
        {
            var updatedFile = mutation.Invoke(context, fileData);
            await File.WriteAllTextAsync(path, updatedFile, cancellation);
            return;
        }
        
        await File.WriteAllTextAsync(path, fileData, cancellation);
    }
    
    internal static string MutateDataSourceFile(string datasourceFile, InfluxResource influxResource)
    {
        var endpoint = influxResource.PrimaryEndpoint;
        return datasourceFile.Replace("http://influxdb:8086", $"{endpoint.EndpointName}://{influxResource.Name}:{endpoint.TargetPort}");
    }
}