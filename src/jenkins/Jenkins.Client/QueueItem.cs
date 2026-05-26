namespace Jenkins.Client;

public sealed record QueueItem(
    long Id,
    bool Stuck,
    bool Cancelled,
    BuildExecutable? Executable,
    string? Why);

public sealed record BuildExecutable(int Number, string Url);
