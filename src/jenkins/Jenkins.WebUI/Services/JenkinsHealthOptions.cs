namespace Jenkins.WebUI.Services;

public sealed record JenkinsHealthOptions
{
    public int PollIntervalSeconds { get; init; } = 30;
    public int ProbeTimeoutSeconds { get; init; } = 5;
}
