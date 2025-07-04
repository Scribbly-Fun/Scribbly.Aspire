using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Scribbly.Aspire.Extensions;

public sealed class DotnetBuildOptions
{
    public BuildConfiguration Configuration { get; set; } = BuildConfiguration.Debug;

    public DirectoryInfo? Output { get; set; }

    public bool CreateOutput { get; set; } = false;

    public enum BuildConfiguration
    {
        Debug,
        Release
    }
}

public static class ExecutableResourceExtensions
{

    public static IResourceBuilder<ExecutableResource> WithDotnetBuild(this IResourceBuilder<ExecutableResource> resourceBuilder, string projectPath, Action<DotnetBuildOptions>? optionsAction = null)
    {
        return resourceBuilder.WithCommand("Build Application", "Builds the application", async context =>
        {
            var options = new DotnetBuildOptions();
            optionsAction?.Invoke(options);

            if (options is { CreateOutput: true, Output.Exists: false })
            {
                Directory.CreateDirectory(options.Output.FullName);
            }

            var stoppedResult = await context.ServiceProvider.StopResource(context.ResourceName);
            
            if (!stoppedResult.Success)
            {
                return new ExecuteCommandResult
                {
                    ErrorMessage = "Failed to stop the resource",
                    Success = false
                };
            }

            using var process = new Process();

            var output = options.Output is not null ? $"-o {options.Output.FullName}" : "";

            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments =
                $@"build {projectPath} -c {options.Configuration} {output}";

            process.Start();

            await process.WaitForExitAsync();

            var startedResult = await context.ServiceProvider.StartResource(context.ResourceName);


            if (!startedResult.Success)
            {
                return new ExecuteCommandResult
                {
                    ErrorMessage = "Failed to restart the resource",
                    Success = false
                };
            }
            return new ExecuteCommandResult()
            {
                Success = true
            };
        }, new CommandOptions { IconVariant = IconVariant.Regular, IconName = "AnimalRabbitOn"});
    }
}