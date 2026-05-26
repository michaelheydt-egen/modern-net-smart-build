namespace Jenkins.Client;

public sealed record Build(
    int Number,
    string Url,
    bool Building,
    BuildResult? Result,
    long Duration,
    long Timestamp);
