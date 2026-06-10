var builder = DistributedApplication.CreateBuilder(args);

// Secrets / parameters — set via the AppHost's user-secrets:
//   dotnet user-secrets set Parameters:JenkinsApiToken <token>
//   dotnet user-secrets set Parameters:JenkinsUrl http://<jenkins>:8080
var jenkinsToken = builder.AddParameter("JenkinsApiToken", secret: true);
var jenkinsUrl = builder.AddParameter("JenkinsUrl");

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

var deployment = builder.AddProject<Projects.Deployment_Api>("deployment-api")
    .WithReference(deploymentDb)
    .WaitFor(sql)
    .WithEnvironment("Database__AutoMigrate", "true");

var jenkins = builder.AddProject<Projects.Jenkins_Api>("jenkins-api")
    .WithReference(deployment)
    .WaitFor(deployment)
    .WithEnvironment("Database__AutoMigrate", "true")
    .WithEnvironment("Deployment__ApiBaseUrl", deployment.GetEndpoint("http"))
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl);

builder.AddProject<Projects.cicd_web_admin>("web-admin")
    .WithReference(deployment)
    .WithReference(jenkins)
    .WaitFor(jenkins)
    .WithEnvironment("Deployment__Api__BaseUrl", deployment.GetEndpoint("http"))
    .WithEnvironment("JenkinsApi__BaseUrl", jenkins.GetEndpoint("http"))
    .WithEnvironment("Jenkins__ApiToken", jenkinsToken)
    .WithEnvironment("Jenkins__Url", jenkinsUrl);

builder.Build().Run();
