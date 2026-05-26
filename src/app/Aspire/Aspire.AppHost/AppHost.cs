var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Api_Web_Weather_AppHost>("api");

builder.AddProject<Projects.Web_AppHost>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("Api__BaseUrl", api.GetEndpoint("https"));

builder.Build().Run();
