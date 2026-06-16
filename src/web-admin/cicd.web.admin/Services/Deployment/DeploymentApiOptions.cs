namespace Cicd.Web.Admin.Services.Deployment;

/// <summary>Options for the Deployment.Api typed HttpClient. Bound from <c>"Deployment:Api"</c>.</summary>
public sealed class DeploymentApiOptions
{
    public const string SectionName = "Deployment:Api";
    public string BaseUrl { get; set; } = "http://localhost:9601";
}
