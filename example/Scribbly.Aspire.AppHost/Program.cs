
using Scribbly.Aspire.Extensions;
using Scribbly.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Scribbly_Aspire_ApiService>("apiservice");

builder.AddProject<Projects.Scribbly_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

if (!builder.ExecutionContext.IsPublishMode)
{
    builder
        .AddLoadTesting("k6server", "./scripts")
        .WithApiResourceForScript("weather-test", apiService)
        .WithApiResourceForScript("loadtest", apiService);
}

builder.Build().Run();
