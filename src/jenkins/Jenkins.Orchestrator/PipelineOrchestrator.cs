using System.Diagnostics;
using Jenkins.Client;

namespace Jenkins.Orchestrator;

public sealed class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IJenkinsClient _client;

    public PipelineOrchestrator(IJenkinsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<PipelineRun> RunAsync(
        IEnumerable<PipelineStep> steps,
        IProgress<PipelineEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stepList = steps.ToList();
        var results = new List<PipelineRunStep>(stepList.Count);
        var resultsByJob = new Dictionary<string, PipelineRunStep>(StringComparer.OrdinalIgnoreCase);

        string? failureReason = null;
        string? currentJobName = null;
        int? currentBuildNumber = null;

        foreach (var step in stepList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failureReason = "cancelled";
                break;
            }

            currentJobName = step.JobName;
            currentBuildNumber = null;

            // Resolve upstream build number, if declared.
            int? upstreamBuildNumber = null;
            if (step.UpstreamJob is { Length: > 0 } upstream)
            {
                if (!resultsByJob.TryGetValue(upstream, out var upstreamResult) || upstreamResult.BuildNumber is null)
                {
                    var msg = $"Upstream step '{upstream}' has no build number; cannot resolve SOURCE_BUILD_NUMBER for '{step.JobName}'.";
                    progress?.Report(new PipelineStepFailed(step.JobName, DateTimeOffset.UtcNow, msg));
                    var failed = new PipelineRunStep(step.JobName, null, null, TimeSpan.Zero, msg);
                    results.Add(failed);
                    resultsByJob[step.JobName] = failed;
                    failureReason = msg;
                    break;
                }
                upstreamBuildNumber = upstreamResult.BuildNumber;
            }

            // Build the parameter set: SOURCE_BUILD_NUMBER (if any) + caller-supplied extras.
            var paramsForCall = new Dictionary<string, string>(StringComparer.Ordinal);
            if (upstreamBuildNumber is int n)
            {
                paramsForCall["SOURCE_BUILD_NUMBER"] = n.ToString();
            }
            if (step.AdditionalParameters is { Count: > 0 } extras)
            {
                foreach (var kv in extras) paramsForCall[kv.Key] = kv.Value;
            }

            progress?.Report(new PipelineStepStarted(step.JobName, DateTimeOffset.UtcNow, upstreamBuildNumber));
            var sw = Stopwatch.StartNew();
            PipelineRunStep stepResult;

            try
            {
                var queueId = await _client.StartBuildAsync(step.JobName, paramsForCall, cancellationToken);
                progress?.Report(new PipelineStepQueued(step.JobName, DateTimeOffset.UtcNow, queueId));

                var buildNumber = await _client.WaitForBuildToStartAsync(queueId, cancellationToken: cancellationToken);
                currentBuildNumber = buildNumber;
                progress?.Report(new PipelineStepRunning(step.JobName, DateTimeOffset.UtcNow, buildNumber));

                var build = await _client.WaitForBuildToFinishAsync(step.JobName, buildNumber, cancellationToken: cancellationToken);
                sw.Stop();

                stepResult = new PipelineRunStep(step.JobName, buildNumber, build.Result, sw.Elapsed, null);

                if (build.Result == BuildResult.Success)
                {
                    progress?.Report(new PipelineStepCompleted(step.JobName, DateTimeOffset.UtcNow, buildNumber, build.Result.Value, sw.Elapsed));
                }
                else
                {
                    var reason = $"Build {step.JobName}#{buildNumber} ended with result {build.Result?.ToString() ?? "(unknown)"}";
                    progress?.Report(new PipelineStepFailed(step.JobName, DateTimeOffset.UtcNow, reason));
                    failureReason = reason;
                    results.Add(stepResult);
                    resultsByJob[step.JobName] = stepResult;
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                await TryStopBuildAsync(step.JobName, currentBuildNumber);
                stepResult = new PipelineRunStep(step.JobName, currentBuildNumber, BuildResult.Aborted, sw.Elapsed, "cancelled");
                progress?.Report(new PipelineStepFailed(step.JobName, DateTimeOffset.UtcNow, "cancelled"));
                failureReason = "cancelled";
                results.Add(stepResult);
                resultsByJob[step.JobName] = stepResult;
                break;
            }
            catch (Exception ex)
            {
                sw.Stop();
                stepResult = new PipelineRunStep(step.JobName, currentBuildNumber, null, sw.Elapsed, ex.Message);
                progress?.Report(new PipelineStepFailed(step.JobName, DateTimeOffset.UtcNow, ex.Message));
                failureReason = ex.Message;
                results.Add(stepResult);
                resultsByJob[step.JobName] = stepResult;
                break;
            }

            results.Add(stepResult);
            resultsByJob[step.JobName] = stepResult;
        }

        return new PipelineRun(results, Success: failureReason is null, FailureReason: failureReason);
    }

    private async Task TryStopBuildAsync(string? jobName, int? buildNumber)
    {
        if (jobName is null || buildNumber is null) return;
        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _client.StopBuildAsync(jobName, buildNumber.Value, stopCts.Token);
        }
        catch
        {
            // Best effort - cancellation is already underway; don't mask it.
        }
    }
}
