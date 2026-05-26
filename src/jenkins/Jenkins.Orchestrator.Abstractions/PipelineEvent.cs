using Jenkins.Client;

namespace Jenkins.Orchestrator;

public abstract record PipelineEvent(string JobName, DateTimeOffset Timestamp);

public sealed record PipelineStepStarted(string JobName, DateTimeOffset Timestamp, int? UpstreamBuildNumber)
    : PipelineEvent(JobName, Timestamp);

public sealed record PipelineStepQueued(string JobName, DateTimeOffset Timestamp, long QueueId)
    : PipelineEvent(JobName, Timestamp);

public sealed record PipelineStepRunning(string JobName, DateTimeOffset Timestamp, int BuildNumber)
    : PipelineEvent(JobName, Timestamp);

public sealed record PipelineStepCompleted(string JobName, DateTimeOffset Timestamp, int BuildNumber, BuildResult Result, TimeSpan Duration)
    : PipelineEvent(JobName, Timestamp);

public sealed record PipelineStepFailed(string JobName, DateTimeOffset Timestamp, string Reason)
    : PipelineEvent(JobName, Timestamp);
