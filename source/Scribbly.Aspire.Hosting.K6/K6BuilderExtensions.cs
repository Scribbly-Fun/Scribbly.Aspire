// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using Aspire;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Scribbly.Aspire;

public sealed class K6ResourceOptions
{
    public bool UseGrafanaDashboard { get; set; } = true;

    public string DashboardContainerName { get; set; } = "grafana";
    public string DatabaseContainerName { get; set; } = "influx";
    
    internal static string? ScriptToRun { get; set; }
}

public static class K6BuilderExtensions
{

    public static IResourceBuilder<K6ServerResource> AddLoadTesting(this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? scriptDirectory = null,
        Action<K6ResourceOptions>? optionsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var options = new K6ResourceOptions();
        optionsCallback?.Invoke(options);
        
        var path = scriptDirectory ?? K6ServerResource.DefaultScriptDirectory;
        var scriptDirectoryParameter = builder.AddParameter("k6-scripts", path);
        
        var scriptParameterResource = scriptDirectoryParameter.Resource;

        var k6Server = new K6ServerResource(name, scriptParameterResource);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(k6Server, async (@event, ct) =>
        {
            connectionString = await k6Server.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString == null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{k6Server.Name}' resource but the connection string was null.");
            }

            if (!Directory.Exists(connectionString))
            {
                Directory.CreateDirectory(connectionString);
            }
        });

        builder.Eventing.Subscribe<ResourceReadyEvent>(k6Server, (@event, ct) =>
        {
            return Task.CompletedTask;
        });

        var healthCheckKey = $"{name}_check";
        
        builder.Services.AddHealthChecks().AddCheck(healthCheckKey, token =>
        {
            return HealthCheckResult.Healthy();
        });
        
        
        var resourceBuilder = builder
            .AddResource(k6Server)
            .WithIconName("TopSpeed")
            .WithImage(K6ContainerImageTags.Image, K6ContainerImageTags.Tag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithDataBindMount(path)
            // .WithArgs("run", "/scripts/loadtest.js")
            .WithArgs(ct =>
            {
                
                if (K6ResourceOptions.ScriptToRun is null)
                {
                    return;
                }
                ct.Args.Add("run /scripts/loadtest.js");
                // ct.Args.Add(K6ResourceOptions.ScriptToRun);
            })
            .WithHealthCheck(healthCheckKey)
            .ExcludeFromManifest()
            .WithExplicitStart();
        
        var scripts = new DirectoryInfo(path).GetFiles("*.js");

        foreach (var fileInfo in scripts)
        {
            var scriptContext = new K6ScriptResource.Context(path, fileInfo);
            
            k6Server.AddScript(scriptContext);
            
            resourceBuilder.WithScriptResource(scriptContext, options);
            
            resourceBuilder.WithCommand(scriptContext.Name, $"Execute {scriptContext.Name}", async context =>
            {
                var applicationModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                var serverResource = applicationModel.Resources.FirstOrDefault(r =>
                    context.ResourceName.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase));
                
                if (serverResource is not K6ServerResource server)
                {
                    return new ExecuteCommandResult { Success = false, ErrorMessage = $"No K6 Resource Found to Execute the Script {K6ResourceOptions.ScriptToRun}" };
                }

                if (!server.TryGetScript(scriptContext.Name, out var executionCtx))
                {
                    return new ExecuteCommandResult { Success = false, ErrorMessage = $"Failed to Find Script to Run {scriptContext.Name}" };
                }
                
                K6ResourceOptions.ScriptToRun = executionCtx.Path;
                
                return await context.ServiceProvider.StartResource(server.Name);
            });
        }
        
        if (options.UseGrafanaDashboard)
        {
            var grafana = resourceBuilder.WithGrafanaDashboard(options);

            resourceBuilder.WithEnvironment(context =>
            {
                var applicationModel = context.ExecutionContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                var influxDbResource = applicationModel.Resources.FirstOrDefault(r =>
                    r.Name.Contains(options.DatabaseContainerName, StringComparison.OrdinalIgnoreCase));
                
                if (influxDbResource is IResourceWithEndpoints influxDatabase)
                {
                    var httpEndpoint = influxDatabase.GetEndpoint("http");
                    context.EnvironmentVariables.Add("K6_OUT", $"influxdb=http://{influxDbResource.Name}:{httpEndpoint.TargetPort}/k6");
                }
            });

            resourceBuilder.WaitFor(grafana);
        }

        return resourceBuilder;
    }

    private static IResourceBuilder<K6ScriptResource> WithScriptResource(
        this IResourceBuilder<K6ServerResource> builder, K6ScriptResource.Context context, K6ResourceOptions options)
    {
        var scriptParameter = builder.ApplicationBuilder.AddParameter(context.ParameterName, context.Path);
        var scriptResource = new K6ScriptResource(context, builder.Resource.Name, scriptParameter.Resource);

        var scriptResourceBuilder = builder.ApplicationBuilder.AddResource(scriptResource);

        scriptResourceBuilder.WithCommand(context.Name, "Execute Test", async context =>
        {
            var applicationModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
            var script = applicationModel.Resources.FirstOrDefault(r =>
                r.Name.Contains(context.ResourceName, StringComparison.OrdinalIgnoreCase));

            if (script is not K6ScriptResource resource)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = "Unknown Script" };
            }
            
            var k6Resource = applicationModel.Resources.FirstOrDefault(r =>
                r.Name.Contains(resource.ContainerName, StringComparison.OrdinalIgnoreCase));

            if (k6Resource is not K6ServerResource server)
            {
                return new ExecuteCommandResult { Success = false, ErrorMessage = $"No K6 Resource Found to Execute the Script {resource.ScriptFileParameter}" };
            }
            
            return await context.ServiceProvider.StartResource(server.Name);
        });

        scriptResourceBuilder.WithExplicitStart();
        return scriptResourceBuilder;
    }
    
    private static IResourceBuilder<InfluxResource> WithInfluxDatabase(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        var influx = new InfluxResource(options.DatabaseContainerName);
        var influxContainerBuilder = builder.ApplicationBuilder
            .AddResource(influx)
            .WithIconName("Clock")
            .WithImage(K6ContainerImageTags.InfluxImage, K6ContainerImageTags.InfluxTag)
            .WithImageRegistry(K6ContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 8086, name: "http")
            .WithEnvironment("INFLUXDB_DB", "k6")
            .ExcludeFromManifest();
        
        influxContainerBuilder.WithRelationship(builder.Resource, "datastream");

        // influxContainerBuilder.WithHttpHealthCheck();

        return influxContainerBuilder;
    }
    
    private static IResourceBuilder<GrafanaResource> WithGrafanaDashboard(this IResourceBuilder<K6ServerResource> builder, K6ResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var influxBuilder = builder.WithInfluxDatabase(options);
        
        var grafana = new GrafanaResource(options.DashboardContainerName);
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
            .WithBindMount("./scripts/grafana","/var/lib/grafana/dashboards")
            // .WithBindMount("./scripts/grafana-dashboard.yaml","/etc/grafana/provisioning/dashboards/dashboard.yaml")
            .WithBindMount("./scripts/grafana-datasource.yaml","/etc/grafana/provisioning/datasources/datasource.yaml")
            .ExcludeFromManifest();
        
        grafanaContainerBuilder
            .WithRelationship(builder.Resource, "dashboard")
            .WithReferenceRelationship(influxBuilder);

        grafanaContainerBuilder.WithHttpHealthCheck();

        grafanaContainerBuilder.WaitFor(influxBuilder);
        
        return grafanaContainerBuilder;
    }
    
    
    private static IResourceBuilder<K6ServerResource> WithDataBindMount(this IResourceBuilder<K6ServerResource> builder, string source, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, "/scripts", isReadOnly);
    }

//     /// <summary>
//     /// Adds a PostgreSQL database to the application model.
//     /// </summary>
//     /// <param name="builder">The PostgreSQL server resource builder.</param>
//     /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
//     /// <param name="databaseName">The name of the database. If not provided, this defaults to the same value as <paramref name="name"/>.</param>
//     /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
//     /// <remarks>
//     /// <para>
//     /// This resource includes built-in health checks. When this resource is referenced as a dependency
//     /// using the <see cref="ResourceBuilderExtensions.WaitFor{T}(IResourceBuilder{T}, IResourceBuilder{IResource})"/>
//     /// extension method then the dependent resource will wait until the Postgres database is available.
//     /// </para>
//     /// <para>
//     /// Note that calling <see cref="AddDatabase(IResourceBuilder{PostgresServerResource}, string, string?)"/>
//     /// will result in the database being created on the Postgres server when the server becomes ready.
//     /// The database creation happens automatically as part of the resource lifecycle.
//     /// </para>
//     /// </remarks>
//     public static IResourceBuilder<PostgresDatabaseResource> AddDatabase(this IResourceBuilder<PostgresServerResource> builder, [ResourceName] string name, string? databaseName = null)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentException.ThrowIfNullOrEmpty(name);
//
//         // Use the resource name as the database name if it's not provided
//         databaseName ??= name;
//
//         var postgresDatabase = new PostgresDatabaseResource(name, databaseName, builder.Resource);
//
//         builder.Resource.AddDatabase(postgresDatabase.Name, postgresDatabase.DatabaseName);
//
//         string? connectionString = null;
//
//         builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(postgresDatabase, async (@event, ct) =>
//         {
//             connectionString = await postgresDatabase.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);
//
//             if (connectionString == null)
//             {
//                 throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{name}' resource but the connection string was null.");
//             }
//         });
//
//         var healthCheckKey = $"{name}_check";
//         builder.ApplicationBuilder.Services.AddHealthChecks().AddNpgSql(sp => connectionString ?? throw new InvalidOperationException("Connection string is unavailable"), name: healthCheckKey);
//
//         return builder.ApplicationBuilder
//             .AddResource(postgresDatabase)
//             .WithHealthCheck(healthCheckKey);
//     }
//
//     /// <summary>
//     /// Adds a pgAdmin 4 administration and development platform for PostgreSQL to the application model.
//     /// </summary>
//     /// <remarks>
//     /// This version of the package defaults to the <inheritdoc cref="PostgresContainerImageTags.PgAdminTag"/> tag of the <inheritdoc cref="PostgresContainerImageTags.PgAdminImage"/> container image.
//     /// </remarks>
//     /// <param name="builder">The PostgreSQL server resource builder.</param>
//     /// <param name="configureContainer">Callback to configure PgAdmin container resource.</param>
//     /// <param name="containerName">The name of the container (Optional).</param>
//     /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<T> WithPgAdmin<T>(this IResourceBuilder<T> builder, Action<IResourceBuilder<PgAdminContainerResource>>? configureContainer = null, string? containerName = null)
//         where T : PostgresServerResource
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//
//         if (builder.ApplicationBuilder.Resources.OfType<PgAdminContainerResource>().SingleOrDefault() is { } existingPgAdminResource)
//         {
//             var builderForExistingResource = builder.ApplicationBuilder.CreateResourceBuilder(existingPgAdminResource);
//             configureContainer?.Invoke(builderForExistingResource);
//             return builder;
//         }
//         else
//         {
//             containerName ??= "pgadmin";
//
//             var pgAdminContainer = new PgAdminContainerResource(containerName);
//             var pgAdminContainerBuilder = builder.ApplicationBuilder.AddResource(pgAdminContainer)
//                                                  .WithImage(PostgresContainerImageTags.PgAdminImage, PostgresContainerImageTags.PgAdminTag)
//                                                  .WithImageRegistry(PostgresContainerImageTags.PgAdminRegistry)
//                                                  .WithHttpEndpoint(targetPort: 80, name: "http")
//                                                  .WithEnvironment(SetPgAdminEnvironmentVariables)
//                                                  .WithHttpHealthCheck("/browser")
//                                                  .ExcludeFromManifest();
//
//             pgAdminContainerBuilder.WithContainerFiles(
//                 destinationPath: "/pgadmin4",
//                 callback: async (context, cancellationToken) =>
//                 {
//                     var appModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
//                     var postgresInstances = builder.ApplicationBuilder.Resources.OfType<PostgresServerResource>();
//
//                     return [
//                         new ContainerFile
//                         {
//                             Name = "servers.json",
//                             Contents = await WritePgAdminServerJson(postgresInstances, cancellationToken).ConfigureAwait(false),
//                         },
//                     ];
//                 });
//
//             configureContainer?.Invoke(pgAdminContainerBuilder);
//
//             pgAdminContainerBuilder.WithRelationship(builder.Resource, "PgAdmin");
//
//             return builder;
//         }
//     }
//
//     /// <summary>
//     /// Configures the host port that the PGAdmin resource is exposed on instead of using randomly assigned port.
//     /// </summary>
//     /// <param name="builder">The resource builder for PGAdmin.</param>
//     /// <param name="port">The port to bind on the host. If <see langword="null"/> is used random port will be assigned.</param>
//     /// <returns>The resource builder for PGAdmin.</returns>
//     public static IResourceBuilder<PgAdminContainerResource> WithHostPort(this IResourceBuilder<PgAdminContainerResource> builder, int? port)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//
//         return builder.WithEndpoint("http", endpoint =>
//         {
//             endpoint.Port = port;
//         });
//     }
//
//     /// <summary>
//     /// Configures the host port that the pgweb resource is exposed on instead of using randomly assigned port.
//     /// </summary>
//     /// <param name="builder">The resource builder for pgweb.</param>
//     /// <param name="port">The port to bind on the host. If <see langword="null"/> is used random port will be assigned.</param>
//     /// <returns>The resource builder for pgweb.</returns>
//     public static IResourceBuilder<PgWebContainerResource> WithHostPort(this IResourceBuilder<PgWebContainerResource> builder, int? port)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//
//         return builder.WithEndpoint("http", endpoint =>
//         {
//             endpoint.Port = port;
//         });
//     }
//
//     /// <summary>
//     /// Adds an administration and development platform for PostgreSQL to the application model using pgweb.
//     /// This version of the package defaults to the <inheritdoc cref="PostgresContainerImageTags.PgWebTag"/> tag of the <inheritdoc cref="PostgresContainerImageTags.PgWebImage"/> container image.
//     /// </summary>
//     /// <param name="builder">The Postgres server resource builder.</param>
//     /// <param name="configureContainer">Configuration callback for pgweb container resource.</param>
//     /// <param name="containerName">The name of the container (Optional).</param>
//     /// <remarks>
//     /// <example>
//     /// Use in application host with a Postgres resource
//     /// <code lang="csharp">
//     /// var builder = DistributedApplication.CreateBuilder(args);
//     ///
//     /// var postgres = builder.AddPostgres("postgres")
//     ///    .WithPgWeb();
//     /// var db = postgres.AddDatabase("db");
//     ///
//     /// var api = builder.AddProject&lt;Projects.Api&gt;("api")
//     ///   .WithReference(db);
//     ///
//     /// builder.Build().Run();
//     /// </code>
//     /// </example>
//     /// </remarks>
//     /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithPgWeb(this IResourceBuilder<PostgresServerResource> builder, Action<IResourceBuilder<PgWebContainerResource>>? configureContainer = null, string? containerName = null)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//
//         if (builder.ApplicationBuilder.Resources.OfType<PgWebContainerResource>().SingleOrDefault() is { } existingPgWebResource)
//         {
//             var builderForExistingResource = builder.ApplicationBuilder.CreateResourceBuilder(existingPgWebResource);
//             configureContainer?.Invoke(builderForExistingResource);
//             return builder;
//         }
//         else
//         {
//             containerName ??= "pgweb";
//
//             var pgwebContainer = new PgWebContainerResource(containerName);
//             var pgwebContainerBuilder = builder.ApplicationBuilder.AddResource(pgwebContainer)
//                                                .WithImage(PostgresContainerImageTags.PgWebImage, PostgresContainerImageTags.PgWebTag)
//                                                .WithImageRegistry(PostgresContainerImageTags.PgWebRegistry)
//                                                .WithHttpEndpoint(targetPort: 8081, name: "http")
//                                                .WithArgs("--bookmarks-dir=/.pgweb/bookmarks")
//                                                .WithArgs("--sessions")
//                                                .ExcludeFromManifest();
//
//             configureContainer?.Invoke(pgwebContainerBuilder);
//
//             pgwebContainerBuilder.WithRelationship(builder.Resource, "PgWeb");
//
//             pgwebContainerBuilder.WithHttpHealthCheck();
//
//             pgwebContainerBuilder.WithContainerFiles(
//                 destinationPath: "/",
//                 callback: async (context, ct) =>
//                 {
//                     var appModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
//                     var postgresInstances = builder.ApplicationBuilder.Resources.OfType<PostgresDatabaseResource>();
//
//                     // Add the bookmarks to the pgweb container
//                     return [
//                         new ContainerDirectory
//                         {
//                             Name = ".pgweb",
//                             Entries = [
//                                 new ContainerDirectory
//                                 {
//                                     Name = "bookmarks",
//                                     Entries = await WritePgWebBookmarks(postgresInstances, ct).ConfigureAwait(false)
//                                 },
//                             ],
//                         },
//                     ];
//                 });
//
//             return builder;
//         }
//     }
//
//     private static void SetPgAdminEnvironmentVariables(EnvironmentCallbackContext context)
//     {
//         // Disables pgAdmin authentication.
//         context.EnvironmentVariables.Add("PGADMIN_CONFIG_MASTER_PASSWORD_REQUIRED", "False");
//         context.EnvironmentVariables.Add("PGADMIN_CONFIG_SERVER_MODE", "False");
//
//         // You need to define the PGADMIN_DEFAULT_EMAIL and PGADMIN_DEFAULT_PASSWORD or PGADMIN_DEFAULT_PASSWORD_FILE environment variables.
//         context.EnvironmentVariables.Add("PGADMIN_DEFAULT_EMAIL", "admin@domain.com");
//         context.EnvironmentVariables.Add("PGADMIN_DEFAULT_PASSWORD", "admin");
//
//         // When running in the context of Codespaces we need to set some additional environment
//         // variables so that PGAdmin will trust the forwarded headers that Codespaces port
//         // forwarding will send.
//         var config = context.ExecutionContext.ServiceProvider.GetRequiredService<IConfiguration>();
//         if (context.ExecutionContext.IsRunMode && config.GetValue<bool>("CODESPACES", false))
//         {
//             context.EnvironmentVariables["PGADMIN_CONFIG_PROXY_X_HOST_COUNT"] = "1";
//             context.EnvironmentVariables["PGADMIN_CONFIG_PROXY_X_PREFIX_COUNT"] = "1";
//         }
//     }
//
//     /// <summary>
//     /// Adds a named volume for the data folder to a PostgreSQL container resource.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="name">The name of the volume. Defaults to an auto-generated name based on the application and resource names.</param>
//     /// <param name="isReadOnly">A flag that indicates if this is a read-only volume.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithDataVolume(this IResourceBuilder<PostgresServerResource> builder, string? name = null, bool isReadOnly = false)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//
//         return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"),
//             "/var/lib/postgresql/data", isReadOnly);
//     }
//
//     /// <summary>
//     /// Adds a bind mount for the data folder to a PostgreSQL container resource.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="source">The source directory on the host to mount into the container.</param>
//     /// <param name="isReadOnly">A flag that indicates if this is a read-only mount.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithDataBindMount(this IResourceBuilder<PostgresServerResource> builder, string source, bool isReadOnly = false)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentException.ThrowIfNullOrEmpty(source);
//
//         return builder.WithBindMount(source, "/var/lib/postgresql/data", isReadOnly);
//     }
//
//     /// <summary>
//     /// Adds a bind mount for the init folder to a PostgreSQL container resource.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="source">The source directory on the host to mount into the container.</param>
//     /// <param name="isReadOnly">A flag that indicates if this is a read-only mount.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     [Obsolete("Use WithInitFiles instead.")]
//     public static IResourceBuilder<PostgresServerResource> WithInitBindMount(this IResourceBuilder<PostgresServerResource> builder, string source, bool isReadOnly = true)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentException.ThrowIfNullOrEmpty(source);
//
//         return builder.WithBindMount(source, "/docker-entrypoint-initdb.d", isReadOnly);
//     }
//
//     /// <summary>
//     /// Copies init files to a PostgreSQL container resource.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="source">The source directory or files on the host to copy into the container.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithInitFiles(this IResourceBuilder<PostgresServerResource> builder, string source)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentException.ThrowIfNullOrEmpty(source);
//
//         const string initPath = "/docker-entrypoint-initdb.d";
//
//         var importFullPath = Path.GetFullPath(source, builder.ApplicationBuilder.AppHostDirectory);
//
//         return builder.WithContainerFiles(initPath, importFullPath);
//     }
//
//     /// <summary>
//     /// Defines the SQL script used to create the database.
//     /// </summary>
//     /// <param name="builder">The builder for the <see cref="PostgresDatabaseResource"/>.</param>
//     /// <param name="script">The SQL script used to create the database.</param>
//     /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
//     /// <remarks>
//     /// The script can only contain SQL statements applying to the default database like CREATE DATABASE. Custom statements like table creation
//     /// and data insertion are not supported since they require a distinct connection to the newly created database.
//     /// <value>Default script is <code>CREATE DATABASE "&lt;QUOTED_DATABASE_NAME&gt;"</code></value>
//     /// </remarks>
//     public static IResourceBuilder<PostgresDatabaseResource> WithCreationScript(this IResourceBuilder<PostgresDatabaseResource> builder, string script)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentNullException.ThrowIfNull(script);
//
//         builder.WithAnnotation(new PostgresCreateDatabaseScriptAnnotation(script));
//
//         return builder;
//     }
//
//     /// <summary>
//     /// Configures the password that the PostgreSQL resource is used.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="password">The parameter used to provide the password for the PostgreSQL resource.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithPassword(this IResourceBuilder<PostgresServerResource> builder, IResourceBuilder<ParameterResource> password)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentNullException.ThrowIfNull(password);
//
//         builder.Resource.PasswordParameter = password.Resource;
//         return builder;
//     }
//
//     /// <summary>
//     /// Configures the user name that the PostgreSQL resource is used.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="userName">The parameter used to provide the user name for the PostgreSQL resource.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithUserName(this IResourceBuilder<PostgresServerResource> builder, IResourceBuilder<ParameterResource> userName)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         ArgumentNullException.ThrowIfNull(userName);
//
//         builder.Resource.UserNameParameter = userName.Resource;
//         return builder;
//     }
//
//     /// <summary>
//     /// Configures the host port that the PostgreSQL resource is exposed on instead of using randomly assigned port.
//     /// </summary>
//     /// <param name="builder">The resource builder.</param>
//     /// <param name="port">The port to bind on the host. If <see langword="null"/> is used random port will be assigned.</param>
//     /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
//     public static IResourceBuilder<PostgresServerResource> WithHostPort(this IResourceBuilder<PostgresServerResource> builder, int? port)
//     {
//         ArgumentNullException.ThrowIfNull(builder);
//         return builder.WithEndpoint(PostgresServerResource.PrimaryEndpointName, endpoint =>
//         {
//             endpoint.Port = port;
//         });
//     }
//
//     private static async Task<IEnumerable<ContainerFileSystemItem>> WritePgWebBookmarks(IEnumerable<PostgresDatabaseResource> postgresInstances, CancellationToken cancellationToken)
//     {
//         var bookmarkFiles = new List<ContainerFileSystemItem>();
//
//         foreach (var postgresDatabase in postgresInstances)
//         {
//             var user = postgresDatabase.Parent.UserNameParameter is null
//             ? "postgres"
//             : await postgresDatabase.Parent.UserNameParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);
//
//             var password = await postgresDatabase.Parent.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false) ?? "password";
//
//             // PgAdmin assumes Postgres is being accessed over a default Aspire container network and hardcodes the resource address
//             // This will need to be refactored once updated service discovery APIs are available
//             var fileContent = $"""
//                     host = "{postgresDatabase.Parent.Name}"
//                     port = {postgresDatabase.Parent.PrimaryEndpoint.TargetPort}
//                     user = "{user}"
//                     password = "{password}"
//                     database = "{postgresDatabase.DatabaseName}"
//                     sslmode = "disable"
//                     """;
//
//             bookmarkFiles.Add(new ContainerFile
//             {
//                 Name = $"{postgresDatabase.Name}.toml",
//                 Contents = fileContent,
//             });
//         }
//
//         return bookmarkFiles;
//     }
//
//     private static async Task<string> WritePgAdminServerJson(IEnumerable<PostgresServerResource> postgresInstances, CancellationToken cancellationToken)
//     {
//         using var stream = new MemoryStream();
//         using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
//
//         writer.WriteStartObject();
//         writer.WriteStartObject("Servers");
//
//         var serverIndex = 1;
//
//         foreach (var postgresInstance in postgresInstances)
//         {
//             var endpoint = postgresInstance.PrimaryEndpoint;
//             var userName = postgresInstance.UserNameParameter is null
//                 ? "postgres"
//                 : await postgresInstance.UserNameParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);
//             var password = await postgresInstance.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);
//
//             writer.WriteStartObject($"{serverIndex}");
//             writer.WriteString("Name", postgresInstance.Name);
//             writer.WriteString("Group", "Servers");
//             // PgAdmin assumes Postgres is being accessed over a default Aspire container network and hardcodes the resource address
//             // This will need to be refactored once updated service discovery APIs are available
//             writer.WriteString("Host", endpoint.Resource.Name);
//             writer.WriteNumber("Port", (int)endpoint.TargetPort!);
//             writer.WriteString("Username", userName);
//             writer.WriteString("SSLMode", "prefer");
//             writer.WriteString("MaintenanceDB", "postgres");
//             writer.WriteString("PasswordExecCommand", $"echo '{password}'"); // HACK: Generating a pass file and playing around with chmod is too painful.
//             writer.WriteEndObject();
//
//             serverIndex++;
//         }
//
//         writer.WriteEndObject();
//         writer.WriteEndObject();
//
//         writer.Flush();
//
//         return Encoding.UTF8.GetString(stream.ToArray());
//     }
//
//     private static async Task CreateDatabaseAsync(NpgsqlConnection npgsqlConnection, PostgresDatabaseResource npgsqlDatabase, IServiceProvider serviceProvider, CancellationToken cancellationToken)
//     {
//         var scriptAnnotation = npgsqlDatabase.Annotations.OfType<PostgresCreateDatabaseScriptAnnotation>().LastOrDefault();
//
//         var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(npgsqlDatabase.Parent);
//         logger.LogDebug("Creating database '{DatabaseName}'", npgsqlDatabase.DatabaseName);
//
//         try
//         {
//             var quotedDatabaseIdentifier = new NpgsqlCommandBuilder().QuoteIdentifier(npgsqlDatabase.DatabaseName);
//             using var command = npgsqlConnection.CreateCommand();
//             command.CommandText = scriptAnnotation?.Script ?? $"CREATE DATABASE {quotedDatabaseIdentifier}";
//             await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
//             logger.LogDebug("Database '{DatabaseName}' created successfully", npgsqlDatabase.DatabaseName);
//         }
//         catch (PostgresException p) when (p.SqlState == "42P04")
//         {
//             // Ignore the error if the database already exists.
//             logger.LogDebug("Database '{DatabaseName}' already exists", npgsqlDatabase.DatabaseName);
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, "Failed to create database '{DatabaseName}'", npgsqlDatabase.DatabaseName);
//         }
//     }
}
