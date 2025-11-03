using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire.Grafana;

internal static class DashboardBuilderExtensions
{
    internal static IResourceBuilder<InfluxResource> WithInfluxDatabase(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        var influx = new InfluxResource(options.DatabaseContainerName, builder.Resource);
        var influxContainerBuilder = builder.ApplicationBuilder
            .AddResource(influx)
            .WithIconName("Clock")
            .WithImage(K6ContainerImageTags.InfluxImage, K6ContainerImageTags.InfluxTag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 8086, name: "http")
            .WithEnvironment("INFLUXDB_DB", "k6")
            .ExcludeFromManifest();
        
        influxContainerBuilder.WithRelationship(builder.Resource, "datastream");
        
        builder.Resource.AddInfluxDatabase(influx);
        
        return influxContainerBuilder;
    }
    
    internal static IResourceBuilder<GrafanaResource> WithGrafanaDashboard(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ApplicationBuilder.Services.AddSingleton(new GrafanaConfigurationManager(builder.Resource.ScriptDirectory));
        
        var influxBuilder = builder.WithInfluxDatabase(options);
        
        var grafana = new GrafanaResource(options.DashboardContainerName, influxBuilder.Resource, builder.Resource);
        var grafanaContainerBuilder = builder.ApplicationBuilder
            .AddResource(grafana)
            .WithIconName("Dashboard")
            .WithImage(K6ContainerImageTags.GrafanaImage, K6ContainerImageTags.Tag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 3000, name: "http")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_BASIC_ENABLED", "false")
            .WithEnvironment("GF_SERVER_SERVE_FROM_SUB_PATH", "true")
            .WithBindMount($"{builder.Resource.ScriptDirectory}/grafana","/var/lib/grafana/dashboards")
            .WithBindMount($"{builder.Resource.ScriptDirectory}/grafana/dashboard.yaml","/etc/grafana/provisioning/dashboards/dashboard.yaml")
            .WithBindMount($"{builder.Resource.ScriptDirectory}/grafana/datasource.yaml","/etc/grafana/provisioning/datasources/datasource.yaml")
            .ExcludeFromManifest();
        
        grafanaContainerBuilder
            .WithRelationship(builder.Resource, "dashboard")
            .WithReferenceRelationship(influxBuilder);

        grafanaContainerBuilder.WithHttpHealthCheck();

        grafanaContainerBuilder.WaitFor(influxBuilder);
        
        builder.ApplicationBuilder.Eventing.Subscribe<InitializeResourceEvent>(builder.Resource, async (@event, ct) =>
        {
            var manager = @event.Services.GetRequiredService<GrafanaConfigurationManager>();
            await manager.CopyConfigurationFiles((context, data) =>
            {
                if (!context.Resource.Contains("datasource", StringComparison.InvariantCultureIgnoreCase))
                {
                    return data;
                }
                if (@event.Resource is K6ServerResource { OutputDatabase: not null } server)
                {
                    return GrafanaConfigurationManager.MutateDataSourceFile(data, server.OutputDatabase);
                }
                return data;
            }, ct);
        });
        
        return grafanaContainerBuilder;
    }
}