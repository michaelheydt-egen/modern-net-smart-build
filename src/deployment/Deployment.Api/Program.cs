using System.Text.Json.Serialization;
using Cicd.Messaging;
using Deployment.Api.Endpoints;
using Deployment.Application;
using Deployment.Infrastructure;
using Deployment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(opts => opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDeploymentApplication();
builder.Services.AddDeploymentInfrastructure(builder.Configuration);

// Wolverine: CQRS dispatcher + in-process bus + durable cross-service messaging.
// Handlers (the ContainerPublished consumer, the run executor, the success translator) are
// discovered from Application + Infrastructure.
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Deployment.Application.DependencyInjection).Assembly);
    opts.Discovery.IncludeAssembly(typeof(DeploymentDbContext).Assembly);

    // The repositories, step executors, and the directly-invoked RequestDeploymentHandler all resolve
    // to *internal* concrete types (Infrastructure persistence/steps). Wolverine's generated handler
    // code can't `new` up an internal type, so without this it throws InvalidServiceLocationException
    // (ServiceLocationPolicy.NotAllowed) and the DeploymentRunRequested/ContainerPublished handlers
    // never compile — leaving deployment runs stuck Pending. Tell Wolverine to resolve these from the
    // container at runtime instead (mirrors the jenkins service's IDeploymentReleaseClient handling).
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Domain.Runs.IDeploymentRunRepository>();
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Domain.Mappings.IDeploymentMappingRepository>();
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Domain.Services.IServiceRepository>();
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Domain.Containers.IKnownContainerRepository>();
    // Service-located (not inlined) on purpose: the registry's constructor takes the executor
    // collection, and letting Wolverine inline `new StepExecutorRegistry(IEnumerable<…>)` would
    // re-trigger the codegen bug it exists to avoid (a mis-materialised executor collection).
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Application.Abstractions.IStepExecutorRegistry>();
    opts.CodeGeneration.AlwaysUseServiceLocationFor<Deployment.Application.Features.Runs.RequestDeploymentHandler>();

    opts.UseEntityFrameworkCoreTransactions();

    var connection = builder.Configuration.GetConnectionString("Deployment");
    if (!string.IsNullOrEmpty(connection))
        opts.PersistMessagesWithSqlServer(connection);

    // Consume CI container-published facts; publish service-deployed facts.
    opts.AddCicdMessaging(builder.Configuration, topology => topology
        .Publish<Cicd.IntegrationEvents.Deployment.ServiceDeployed>("deployment.events")
        .Subscribe("ci.events", subscriber: "deployment"));
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DeploymentDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try { await db.Database.MigrateAsync(); break; }
        catch (Exception ex) when (attempt < 12)
        {
            app.Logger.LogWarning(ex, "DB migrate attempt {Attempt} failed; retrying in 5s…", attempt);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { name = "Deployment.Api", status = "ready" }));

app.MapServiceEndpoints();
app.MapEnvironmentEndpoints();
app.MapMappingEndpoints();
app.MapRunEndpoints();

app.Run();
