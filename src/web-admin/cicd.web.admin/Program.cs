using Jenkins.Client;
using Jenkins.Orchestrator;
using Cicd.Web.Admin.Components;
using Cicd.Web.Admin.Services;
using Cicd.Web.Admin.Services.Builds;
using Cicd.Web.Admin.Services.Ci;
using Cicd.Web.Admin.Services.Deployment;
using Cicd.Web.Admin.Services.Gcp;
using Cicd.Web.Admin.Services.Nexus;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Jenkins config — read once at startup from configuration / env vars.
// Required: Jenkins:ApiToken (env: Jenkins__ApiToken)
var jenkinsApiToken = builder.Configuration["Jenkins:ApiToken"];
if (string.IsNullOrEmpty(jenkinsApiToken))
    throw new InvalidOperationException(
        "Jenkins:ApiToken is not configured. Set env var Jenkins__ApiToken (double underscore) or set Jenkins:ApiToken in appsettings.");

var jenkinsOptions = new JenkinsOptions(
    BaseUrl:  builder.Configuration["Jenkins:Url"]  ?? "http://jenkins:8080",
    User:     builder.Configuration["Jenkins:User"] ?? "admin",
    ApiToken: jenkinsApiToken);

builder.Services.AddSingleton(jenkinsOptions);
builder.Services.AddSingleton<IJenkinsClient>(sp => new JenkinsClient(sp.GetRequiredService<JenkinsOptions>()));
builder.Services.AddSingleton<IPipelineOrchestrator, PipelineOrchestrator>();
builder.Services.AddSingleton<IPipelineRunner, PipelineRunner>();

// Build history reads come straight from Jenkins (live mode). Persisted build
// history is now owned by the Jenkins CI service (Jenkins.Api) — consumed by the
// CI pages — so the old local SQLite build mirror (BuildSync) has been retired.
builder.Services.AddSingleton<JenkinsLiveBuildStore>();
builder.Services.AddSingleton<IBuildStore>(sp => sp.GetRequiredService<JenkinsLiveBuildStore>());

// Health probe settings — configurable via Jenkins:Health:{PollIntervalSeconds,ProbeTimeoutSeconds}.
var healthOptions = builder.Configuration.GetSection("Jenkins:Health").Get<JenkinsHealthOptions>()
                    ?? new JenkinsHealthOptions();
if (healthOptions.PollIntervalSeconds <= 0)
    throw new InvalidOperationException("Jenkins:Health:PollIntervalSeconds must be > 0.");
if (healthOptions.ProbeTimeoutSeconds <= 0)
    throw new InvalidOperationException("Jenkins:Health:ProbeTimeoutSeconds must be > 0.");
builder.Services.AddSingleton(healthOptions);

// Same singleton serves three roles: the BackgroundService loop, the IJenkinsHealth
// snapshot exposed to UI components, and the concrete type (rarely needed but handy).
// It owns its own JenkinsClient (separate HttpClient from the orchestrator's) so the
// 30-second health pings don't share a connection pool with long-running build polls.
builder.Services.AddSingleton<JenkinsHealthService>();
builder.Services.AddSingleton<IJenkinsHealth>(sp => sp.GetRequiredService<JenkinsHealthService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<JenkinsHealthService>());

// Nexus — Url + repository name come from configuration; Password MUST come from
// env var (Nexus__Password) per project security policy. Missing creds don't fail
// startup; the Nuget page surfaces the configuration error.
var nexusOptions = builder.Configuration.GetSection("Nexus").Get<NexusOptions>() ?? new NexusOptions();
builder.Services.AddSingleton(nexusOptions);
builder.Services.AddSingleton<INexusClient, NexusClient>();

// Google Cloud — projects list comes from configuration; the GcpClient resolves
// Application Default Credentials at construction. If creds are missing, the
// client records the error but doesn't fail startup (the Google page surfaces it).
var gcpOptions = builder.Configuration.GetSection("Google").Get<GcpOptions>() ?? new GcpOptions();
builder.Services.AddSingleton(gcpOptions);
builder.Services.AddSingleton<IGcpClient, GcpClient>();

// Deployment.Api — typed HttpClient. BaseUrl from config (Deployment:Api:BaseUrl).
// The API runs as a separate service (compose: deployment-api); 5000 is the dev default.
var deploymentApiOptions = builder.Configuration.GetSection(DeploymentApiOptions.SectionName).Get<DeploymentApiOptions>()
                           ?? new DeploymentApiOptions();
builder.Services.AddSingleton(deploymentApiOptions);
builder.Services.AddHttpClient<DeploymentApiClient>(c =>
{
    c.BaseAddress = new Uri(deploymentApiOptions.BaseUrl.EndsWith('/')
        ? deploymentApiOptions.BaseUrl
        : deploymentApiOptions.BaseUrl + "/");
    c.Timeout = TimeSpan.FromSeconds(30);
});

// Jenkins CI service (Jenkins.Api) — typed HttpClient. BaseUrl from config
// (JenkinsApi:BaseUrl); separate service from the direct Jenkins-server connection.
var jenkinsApiOptions = builder.Configuration.GetSection(JenkinsApiOptions.SectionName).Get<JenkinsApiOptions>()
                        ?? new JenkinsApiOptions();
builder.Services.AddSingleton(jenkinsApiOptions);
builder.Services.AddHttpClient<JenkinsApiClient>(c =>
{
    c.BaseAddress = new Uri(jenkinsApiOptions.BaseUrl.EndsWith('/')
        ? jenkinsApiOptions.BaseUrl
        : jenkinsApiOptions.BaseUrl + "/");
    c.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// Aspire defaults: /alive (liveness — also what the Dockerfile HEALTHCHECK hits) + /health.
app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
