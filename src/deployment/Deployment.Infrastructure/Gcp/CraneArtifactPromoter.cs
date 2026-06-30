using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;
using Deployment.Domain.Runs;

namespace Deployment.Infrastructure.Gcp;

/// <summary>
/// <see cref="IArtifactPromoter"/> backed by <c>crane copy</c> — digest-preserving, daemonless,
/// idempotent registry-to-registry copy. Must be authenticated to both registries (ambient docker
/// config / gcloud credential helper). Salvaged from the prior deployment service.
/// </summary>
internal sealed class CraneArtifactPromoter : IArtifactPromoter
{
    private readonly IOptionsMonitor<GoogleCloudRunOptions> _options;
    private readonly ILogger<CraneArtifactPromoter> _logger;

    public CraneArtifactPromoter(IOptionsMonitor<GoogleCloudRunOptions> options, ILogger<CraneArtifactPromoter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task EnsureCopiedAsync(string sourceRef, string destinationRef, CancellationToken cancellationToken = default)
    {
        var exe = _options.CurrentValue.CraneExecutable;
        if (string.IsNullOrWhiteSpace(exe)) exe = "crane";

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("copy");
        psi.ArgumentList.Add(sourceRef);
        psi.ArgumentList.Add(destinationRef);

        _logger.LogInformation("[promote] {Exe} copy {Source} {Dest}", exe, sourceRef, destinationRef);

        using var process = new Process { StartInfo = psi };
        try { process.Start(); }
        catch (Exception ex)
        {
            throw new DeploymentStepException(StepFailureKind.ToolMissing,
                $"Could not start '{exe}' for image copy (installed and on PATH?): {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var combined = $"{stderr.Trim()} {stdout.Trim()}".Trim();
            var kind = LooksLikeAuthFailure(combined) ? StepFailureKind.RegistryAuth : StepFailureKind.RegistryError;
            // Lead with the first non-empty line — crane's auth/registry errors are on the first line;
            // the rest is usually a stack-ish dump that only clutters the toast.
            var summary = FirstLine(combined);
            throw new DeploymentStepException(kind, $"crane copy exited {process.ExitCode}: {summary}");
        }
    }

    private static bool LooksLikeAuthFailure(string text) =>
        text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
        || text.Contains("denied", StringComparison.OrdinalIgnoreCase)
        || text.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
        || text.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || text.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || text.Contains("401", StringComparison.Ordinal)
        || text.Contains("403", StringComparison.Ordinal);

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "no output.";
        var nl = text.IndexOfAny(['\r', '\n']);
        return (nl >= 0 ? text[..nl] : text).Trim();
    }
}
