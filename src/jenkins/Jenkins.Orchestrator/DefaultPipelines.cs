namespace Jenkins.Orchestrator;

public static class DefaultPipelines
{
    /// <summary>
    /// The CI build + publish chain run by the orchestrator: build, then publish the
    /// NuGet package and the container image to Nexus. Run sequentially; each
    /// downstream pulls SOURCE_BUILD_NUMBER from its declared upstream.
    ///
    /// Deployment is deliberately NOT part of this chain. Promoting a container into
    /// GAR and rolling it out to Cloud Run / GKE is owned by the deployment service
    /// (decision #6), driven by the CI→deployment handoff — not by Jenkins jobs. The
    /// former <c>cicd-publish-gcp-gar</c> / <c>cicd-publish-gcp-gcr</c> stages were
    /// removed here; the orchestrator's job ends at "published to Nexus".
    /// </summary>
    public static IReadOnlyList<PipelineStep> CicdMain() => new[]
    {
        new PipelineStep("cicd-build"),
        new PipelineStep("cicd-publish-nexus-nuget",  UpstreamJob: "cicd-build"),
        new PipelineStep("cicd-publish-nexus-docker", UpstreamJob: "cicd-build"),
    };
}
