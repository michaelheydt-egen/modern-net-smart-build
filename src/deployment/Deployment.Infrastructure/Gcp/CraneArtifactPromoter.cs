using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;

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
            throw new InvalidOperationException($"Could not start '{exe}' for image copy (installed and on PATH?): {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{exe} copy' exited {process.ExitCode}. {stderr.Trim()} {stdout.Trim()}".Trim());
    }
}
