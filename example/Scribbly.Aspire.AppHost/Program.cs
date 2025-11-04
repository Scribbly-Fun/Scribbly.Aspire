
using Scribbly.Aspire;
using Scribbly.Aspire.K6;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Scribbly_Aspire_ApiService>("apiservice");

builder.AddProject<Projects.Scribbly_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

if (!builder.ExecutionContext.IsPublishMode)
{
    builder
        .AddLoadTesting("load-tester", "./scripts", ops =>
        {
            ops.ExplicateStartDashboard = true;
        })
        .WithApiResourceForScript("weather-test", apiService)
        .WithApiResourceForScript("loadtest", apiService);
}

builder.Build().Run();
