using System.Text.Json.Serialization;
using Jenkins.Api.Endpoints;
using Jenkins.Application;
using Jenkins.Infrastructure;
using Jenkins.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

// Enums on the wire are strings (BuildStatusDto = "Succeeded", not 2) — matches the
// deployment API's shape and the UI client's expectations.
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Application layer: FluentValidation registrations + use-case services.
builder.Services.AddJenkinsApplication();

// Infrastructure layer: EF Core (SQLite) DbContext + the deployment Releases client.
// ConnectionStrings:JenkinsCi + Deployment:ApiBaseUrl.
builder.Services.AddJenkinsInfrastructure(builder.Configuration);

// Build-sync background worker (Jenkins -> CI model). No-op when Jenkins is
// unconfigured. Jenkins:Url + Jenkins:ApiToken + Jenkins:Sync.
builder.Services.AddJenkinsBuildSync(builder.Configuration);

// Wolverine: CQRS dispatcher + in-process bus. Handlers in Features/* are discovered
// by convention from the Application + Infrastructure assemblies. EF-transaction
// enrolment + a durable outbox are wired in when handlers land.
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Jenkins.Application.DependencyInjection).Assembly);
    opts.Discovery.IncludeAssembly(typeof(JenkinsCiDbContext).Assembly);
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Apply EF migrations at startup when Database:AutoMigrate is set (compose/dev
// convenience). SQLite is local, so no retry needed.
if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<JenkinsCiDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Jenkins.Api",
    status = "ready",
}));

app.MapRepositoryEndpoints();
app.MapBuildEndpoints();
app.MapHandoffEndpoints();

app.Run();
