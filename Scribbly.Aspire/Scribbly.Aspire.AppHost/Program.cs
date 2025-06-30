var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Scribbly_Aspire_ApiService>("apiservice");

builder.AddProject<Projects.Scribbly_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
