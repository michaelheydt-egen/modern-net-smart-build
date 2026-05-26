using Jenkins.Client;
using Jenkins.Orchestrator;
using Util.Commands.Abstractions;

namespace Util.Commands.Pipeline;

public sealed class RunPipelineCommand : ICommand
{
    public string Name => "pipeline-run";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        string? urlOverride = null;
        string? userOverride = null;
        string? tokenOverride = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length:
                    urlOverride = args[++i];
                    break;
                case "--user" when i + 1 < args.Length:
                    userOverride = args[++i];
                    break;
                case "--token" when i + 1 < args.Length:
                    tokenOverride = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    PrintUsage();
                    return 2;
            }
        }

        var url   = urlOverride   ?? Environment.GetEnvironmentVariable("JENKINS_URL")   ?? "http://jenkins:8080";
        var user  = userOverride  ?? Environment.GetEnvironmentVariable("JENKINS_USER")  ?? "admin";
        var token = tokenOverride ?? Environment.GetEnvironmentVariable("JENKINS_TOKEN");

        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("ERROR: JENKINS_TOKEN env var (or --token flag) is required.");
            return 2;
        }

        using var jenkins = new JenkinsClient(new JenkinsOptions(url, user, token));
        IPipelineOrchestrator orchestrator = new PipelineOrchestrator(jenkins);

        var steps = DefaultPipelines.CicdMain();

        Console.WriteLine($"Running pipeline against {url} as {user}");
        Console.WriteLine("Steps:");
        foreach (var s in steps)
        {
            var upstream = s.UpstreamJob is { Length: > 0 } ? $"  (upstream: {s.UpstreamJob})" : string.Empty;
            Console.WriteLine($"  - {s.JobName}{upstream}");
        }
        Console.WriteLine();

        var progress = new Progress<PipelineEvent>(evt =>
        {
            var ts = evt.Timestamp.ToString("HH:mm:ss");
            switch (evt)
            {
                case PipelineStepStarted s:
                    var upstreamInfo = s.UpstreamBuildNumber is int n ? $" (consuming upstream build #{n})" : string.Empty;
                    Console.WriteLine($"[{ts}] {s.JobName}: started{upstreamInfo}");
                    break;
                case PipelineStepQueued q:
                    Console.WriteLine($"[{ts}] {q.JobName}: queued (queue item {q.QueueId})");
                    break;
                case PipelineStepRunning r:
                    Console.WriteLine($"[{ts}] {r.JobName}: running as build #{r.BuildNumber}");
                    break;
                case PipelineStepCompleted c:
                    Console.WriteLine($"[{ts}] {c.JobName}: completed #{c.BuildNumber} {c.Result} in {c.Duration.TotalSeconds:F1}s");
                    break;
                case PipelineStepFailed f:
                    Console.Error.WriteLine($"[{ts}] {f.JobName}: FAILED - {f.Reason}");
                    break;
            }
        });

        var run = await orchestrator.RunAsync(steps, progress, cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Pipeline {(run.Success ? "SUCCEEDED" : "FAILED")}");
        if (!run.Success) Console.WriteLine($"Reason: {run.FailureReason}");
        Console.WriteLine();
        Console.WriteLine("Summary:");
        foreach (var s in run.Steps)
        {
            var resultStr = s.Result?.ToString() ?? "(none)";
            var buildStr  = s.BuildNumber.HasValue ? $"#{s.BuildNumber}" : "(no build)";
            var errorStr  = s.Error is null ? string.Empty : $"  ERROR: {s.Error}";
            Console.WriteLine($"  {s.JobName} {buildStr} {resultStr} ({s.Duration.TotalSeconds:F1}s){errorStr}");
        }

        return run.Success ? 0 : 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage: Util.Cli pipeline-run [options]

            Runs the canonical cicd pipeline against Jenkins:
              cicd-build
                -> cicd-publish-nuget
                -> cicd-publish-nexus-docker
                  -> cicd-publish-gar
                    -> cicd-publish-gcr

            Each step's build number is passed to its downstream as SOURCE_BUILD_NUMBER.
            Stops on the first non-success result. Cancellation (Ctrl-C) attempts to stop
            the in-flight Jenkins build.

            Options:
              --url <url>      Jenkins base URL (default: $JENKINS_URL or http://jenkins:8080)
              --user <user>    Jenkins user (default: $JENKINS_USER or admin)
              --token <token>  Jenkins API token (default: $JENKINS_TOKEN - required)
              --help, -h       Show this help

            Environment variables: JENKINS_URL, JENKINS_USER, JENKINS_TOKEN
            """);
    }
}
