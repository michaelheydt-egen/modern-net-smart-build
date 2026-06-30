using Microsoft.Extensions.Options;

namespace Deployment.Infrastructure.Gcp;

/// <summary>
/// Fails service startup (via <c>ValidateOnStart</c>) when <see cref="GoogleCloudRunOptions"/> is
/// mis-configured, rather than letting the first deploy fail at the GarPush step. The crane check is
/// the important one: a rooted <see cref="GoogleCloudRunOptions.CraneExecutable"/> must exist; a bare
/// name must resolve on PATH (honouring PATHEXT on Windows). Auth (Nexus/GAR/ADC) is deliberately NOT
/// validated here — it's ambient and verified at deploy time.
/// </summary>
internal sealed class GoogleCloudRunOptionsValidator : IValidateOptions<GoogleCloudRunOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleCloudRunOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CraneExecutable))
            failures.Add("Deployment:GoogleCloudRun:CraneExecutable must be set (a path to go-containerregistry's crane, or a bare name on PATH).");
        else if (!CraneIsResolvable(options.CraneExecutable))
            failures.Add(
                $"crane executable '{options.CraneExecutable}' was not found. Install go-containerregistry's crane and either " +
                "put it on PATH or set Deployment:GoogleCloudRun:CraneExecutable (under Aspire: user-secret Parameters:CraneExecutable) to its full path.");

        if (options.ReadinessTimeoutSeconds <= 0)
            failures.Add("Deployment:GoogleCloudRun:ReadinessTimeoutSeconds must be greater than 0.");
        if (options.ReadinessPollSeconds <= 0)
            failures.Add("Deployment:GoogleCloudRun:ReadinessPollSeconds must be greater than 0.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>True if the executable exists at a rooted path, or a bare name resolves on PATH (+PATHEXT).</summary>
    private static bool CraneIsResolvable(string exe)
    {
        if (Path.IsPathRooted(exe) || exe.Contains(Path.DirectorySeparatorChar) || exe.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(exe);

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // On Windows a bare "crane" may resolve as crane.exe/.cmd/.bat; elsewhere just the name.
        var pathext = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { string.Empty };

        foreach (var dir in dirs)
        {
            if (File.Exists(Path.Combine(dir, exe))) return true;
            foreach (var ext in pathext)
                if (File.Exists(Path.Combine(dir, exe + ext))) return true;
        }
        return false;
    }
}
