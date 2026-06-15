using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Secrets / parameters — set via the AppHost's user-secrets:
//   dotnet user-secrets set Parameters:JenkinsApiToken <token>
//   dotnet user-secrets set Parameters:JenkinsUrl http://<jenkins>:8080
var jenkinsToken = builder.AddParameter("JenkinsApiToken", secret: true);
var jenkinsUrl = builder.AddParameter("JenkinsUrl");

// Nexus — required by the CI service's artifact-reconcile loop (JenkinsBuildSyncService): it polls
// Nexus for each tracked build's pushed docker image and attaches the publication. Also used by
// the admin UI's Docker/NuGet pages. The reconcile reader is only registered when BOTH Url and
// Password are non-empty. These must be EAGER (fixed value) — a lazy secret AddParameter that
// Aspire can't auto-resolve becomes an interactive prompt that blocks the referencing resource in
// a headless `dotnet run`; builder.Configuration does NOT surface the parameter user-secrets, so we
// read them explicitly. Override via the AppHost's user-secrets:
//   dotnet user-secrets set Parameters:NexusUrl http://<nexus>:8081
//   dotnet user-secrets set Parameters:NexusPassword <password>
//   dotnet user-secrets set Parameters:NexusDockerHost <nexus>:8082
var paramSecrets = new ConfigurationBuilder()
    .AddUserSecrets("7e3b1a2c-9d4f-4a6b-8c1e-2f5a9b0c3d4e")
    .Build();
string NexusParam(string key, string fallback) =>
    builder.Configuration[$"Parameters:{key}"] is { Length: > 0 } v ? v
    : paramSecrets[$"Parameters:{key}"] is { Length: > 0 } s ? s
    : fallback;

var nexusUrl = builder.AddParameter("NexusUrl", NexusParam("NexusUrl", "http://nexus:8081"));
var nexusPassword = builder.AddParameter("NexusPassword", NexusParam("NexusPassword", ""), secret: true);
var nexusDockerHost = builder.AddParameter("NexusDockerHost", NexusParam("NexusDockerHost", "nexus:8082"));
var nexusDockerRepo = builder.AddParameter("NexusDockerRepository", NexusParam("NexusDockerRepository", "docker-private"));

// SQL Server (container) + the Jenkins CI database. The sa password is an EXPLICIT, pinned
// parameter (Parameters:sql-password in user-secrets) rather than Aspire's auto-generated one —
// SQL Server bakes it into the data volume on first init and never updates it, so a drifting
// auto-generated value leaves the volume's sa password mismatched ("Login failed for user 'sa'").
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", password: sqlPassword).WithDataVolume();
var jenkinsDb = sql.AddDatabase("JenkinsCi");

// RabbitMQ broker for the CI service's outbox/event publishing. Ephemeral (no data volume) —
// Wolverine's per-service SQL outbox provides durability, so the broker itself is disposable.
var rabbit = builder.AddRabbitMQ("messaging").WithManagementPlugin();

var jenkins = builder.AddProject<Projects.Jenkins_Api>("jenkins-api")
    .WithReference(jenkinsDb)
    .WaitFor(sql)
    .WithReference(rabbit)
    .WaitFor(rabbit)
    .WithEnvironment("Database__AutoMigrate", "true")
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl)
    // Nexus reconcile (option b): attaches each build's pushed docker image as a publication.
    .WithEnvironment("Nexus__Url", nexusUrl)
    .WithEnvironment("Nexus__Password", nexusPassword)
    .WithEnvironment("Nexus__DockerRegistryHost", nexusDockerHost)
    .WithEnvironment("Nexus__DockerRepository", nexusDockerRepo);

builder.AddProject<Projects.cicd_web_admin>("web-admin")
    .WithReference(jenkins)
    .WaitFor(jenkins)
    .WithEnvironment("JenkinsApi__BaseUrl", jenkins.GetEndpoint("http"))
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl)
    // Nexus config for the admin UI's Docker/NuGet pages.
    .WithEnvironment("Nexus__Url", nexusUrl)
    .WithEnvironment("Nexus__Password", nexusPassword)
    .WithEnvironment("Nexus__DockerRegistryHost", nexusDockerHost)
    .WithEnvironment("Nexus__DockerHostedRepository", nexusDockerRepo);

builder.Build().Run();
