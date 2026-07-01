namespace Jenkins.Domain.SourceRepositories;

/// <summary>
/// How a repository is built. <see cref="Standard"/> is the default cicd-build chain; <see cref="Aspire"/>
/// is a .NET Aspire app built with Aspir8 (the cicd-aspire-publish job) — images + a Kustomize manifest
/// artifact. Selects the pipeline default and drives the Aspire CI→deploy handoff.
/// </summary>
public enum BuildKind
{
    Standard = 0,
    Aspire = 1,
}
