using Jenkins.Client;
using Jenkins.Orchestrator;
using Jenkins.WebUI.Components;
using Jenkins.WebUI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Health endpoint for the Dockerfile HEALTHCHECK.
app.MapGet("/alive", () => Results.Ok("ok"));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
