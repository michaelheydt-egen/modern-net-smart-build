namespace Cicd.Web.Admin.Services.Deployment;

/// <summary>
/// Options for the Deployment.Api typed HttpClient. Bound from configuration
/// section <c>"Deployment:Api"</c>. <see cref="BaseUrl"/> defaults to
/// <c>http://localhost:5000</c> for local dev.
/// </summary>
public sealed class DeploymentApiOptions
{
    public const string SectionName = "Deployment:Api";

    public string BaseUrl { get; set; } = "http://localhost:9601";
}
