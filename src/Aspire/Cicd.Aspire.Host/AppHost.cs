var builder = DistributedApplication.CreateBuilder(args);

// Secrets / parameters — set via the AppHost's user-secrets:
//   dotnet user-secrets set Parameters:JenkinsApiToken <token>
//   dotnet user-secrets set Parameters:JenkinsUrl http://<jenkins>:8080
var jenkinsToken = builder.AddParameter("JenkinsApiToken", secret: true);
var jenkinsUrl = builder.AddParameter("JenkinsUrl");

// Nexus — required by the CI service's artifact-reconcile loop (JenkinsBuildSyncService):
// it polls Nexus for each tracked build's pushed docker image and, on a match, raises
// ContainerPublished — which is what populates the publisher inventory. The reconcile reader
// is only registered when BOTH Url and Password are non-empty, so a blank password silently
// disables auto-population. Override via the AppHost's user-secrets (defaults assume the
// docker-network hostnames the build pipeline uses; use localhost:8081/8082 if Nexus's ports
// are published to the host instead):
//   dotnet user-secrets set Parameters:NexusUrl http://<nexus>:8081
//   dotnet user-secrets set Parameters:NexusPassword <password>
//   dotnet user-secrets set Parameters:NexusDockerHost <nexus>:8082
var nexusUrl = builder.AddParameter("NexusUrl", builder.Configuration["Parameters:NexusUrl"] ?? "http://nexus:8081");
var nexusPassword = builder.AddParameter("NexusPassword", builder.Configuration["Parameters:NexusPassword"] ?? "", secret: true);
var nexusDockerHost = builder.AddParameter("NexusDockerHost", builder.Configuration["Parameters:NexusDockerHost"] ?? "nexus:8082");
// The hosted docker repo the build pipeline pushes to (via the :8082 connector). This project's
// Nexus names it "docker-private" — NOT the generic "docker-hosted" the options classes default
// to, so the reconcile/UI must be pointed here or they search an empty repo.
var nexusDockerRepo = builder.AddParameter("NexusDockerRepository", builder.Configuration["Parameters:NexusDockerRepository"] ?? "docker-private");

// SQL Server (container) + the deployment DB. The database resource name
// "Deployment" becomes ConnectionStrings__Deployment on referencing services.
//
// The sa password is an EXPLICIT, pinned parameter (Parameters:sql-password in
// user-secrets) rather than Aspire's auto-generated one. SQL Server bakes the
// password into the data volume on first init and never updates it, so an
// auto-generated value that drifts leaves the volume's sa password mismatched
// ("Login failed for user 'sa'"). Pinning it keeps the volume and the passed
// password aligned across runs.
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", password: sqlPassword).WithDataVolume();
var deploymentDb = sql.AddDatabase("Deployment");
var jenkinsDb = sql.AddDatabase("JenkinsCi");
var publisherDb = sql.AddDatabase("Publisher");

// RabbitMQ broker for the cross-service event bus. Ephemeral (no data volume) — Wolverine's
// per-service SQL outbox/inbox provides durability, so the broker itself is disposable. The
// resource name "messaging" surfaces as ConnectionStrings__messaging on referencing services
// (consumed by Cicd.Messaging's provider switch).
var rabbit = builder.AddRabbitMQ("messaging").WithManagementPlugin();

var deployment = builder.AddProject<Projects.Deployment_Api>("deployment-api")
    .WithReference(deploymentDb)
    .WaitFor(sql)
    .WithReference(rabbit)
    .WaitFor(rabbit)
    .WithEnvironment("Database__AutoMigrate", "true");

var jenkins = builder.AddProject<Projects.Jenkins_Api>("jenkins-api")
    .WithReference(deployment)
    .WaitFor(deployment)
    .WithReference(jenkinsDb)
    .WaitFor(sql)
    .WithReference(rabbit)
    .WaitFor(rabbit)
    .WithEnvironment("Database__AutoMigrate", "true")
    .WithEnvironment("Deployment__ApiBaseUrl", deployment.GetEndpoint("http"))
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl)
    // Nexus reconcile (option b): populates the publisher inventory by detecting pushed images.
    .WithEnvironment("Nexus__Url", nexusUrl)
    .WithEnvironment("Nexus__Password", nexusPassword)
    .WithEnvironment("Nexus__DockerRegistryHost", nexusDockerHost)
    .WithEnvironment("Nexus__DockerRepository", nexusDockerRepo);

// Publisher: moves containers from local Nexus to remote registries (GAR for now). Consumes the
// CI ContainerPublished bus event to keep a local inventory; exposes an API to tag containers
// publishable under a stable channel name.
var publisher = builder.AddProject<Projects.Publisher_Api>("publisher-api")
    .WithReference(publisherDb)
    .WaitFor(sql)
    .WithReference(rabbit)
    .WaitFor(rabbit)
    .WithEnvironment("Database__AutoMigrate", "true");

builder.AddProject<Projects.cicd_web_admin>("web-admin")
    .WithReference(deployment)
    .WithReference(jenkins)
    .WaitFor(jenkins)
    .WithReference(publisher)
    .WaitFor(publisher)
    .WithEnvironment("Deployment__Api__BaseUrl", deployment.GetEndpoint("http"))
    .WithEnvironment("JenkinsApi__BaseUrl", jenkins.GetEndpoint("http"))
    .WithEnvironment("PublisherApi__BaseUrl", publisher.GetEndpoint("http"))
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl)
    // Same Nexus config as jenkins-api — the admin UI browses the docker registry (Docker page,
    // and the inventory "Add container" dialog builds pull refs from Nexus:DockerRegistryHost).
    // NOTE: web-admin's NexusOptions key is DockerHostedRepository (jenkins uses DockerRepository).
    .WithEnvironment("Nexus__Url", nexusUrl)
    .WithEnvironment("Nexus__Password", nexusPassword)
    .WithEnvironment("Nexus__DockerRegistryHost", nexusDockerHost)
    .WithEnvironment("Nexus__DockerHostedRepository", nexusDockerRepo);

builder.Build().Run();
