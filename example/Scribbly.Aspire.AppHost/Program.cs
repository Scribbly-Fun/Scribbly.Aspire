
using Scribbly.Aspire.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Scribbly_Aspire_ApiService>("apiservice");

builder.AddProject<Projects.Scribbly_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithEnvironmentVar("hi", "James")
    .WaitFor(apiService);

builder.Build().Run();
