using Util.Commands.Abstractions;

namespace Util.Commands.Nexus;

public sealed class PurgeRepoCommand : ICommand
{
    public string Name => "nexus-purge-repo";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var repo = "nuget-hosted";
        var execute = false;
        string? urlOverride = null;
        string? userOverride = null;
        string? passOverride = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length:
                    repo = args[++i];
                    break;
                case "--yes":
                    execute = true;
                    break;
                case "--url" when i + 1 < args.Length:
                    urlOverride = args[++i];
                    break;
                case "--user" when i + 1 < args.Length:
                    userOverride = args[++i];
                    break;
                case "--password" when i + 1 < args.Length:
                    passOverride = args[++i];
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

        var url  = urlOverride  ?? Environment.GetEnvironmentVariable("NEXUS_URL")  ?? "http://nexus:8081";
        var user = userOverride ?? Environment.GetEnvironmentVariable("NEXUS_USER") ?? "admin";
        var pass = passOverride ?? Environment.GetEnvironmentVariable("NEXUS_PASS");

        if (string.IsNullOrEmpty(pass))
        {
            Console.Error.WriteLine("ERROR: NEXUS_PASS env var (or --password flag) is required.");
            return 2;
        }

        using var client = new NexusClient(new NexusOptions(url, user, pass));

        Console.WriteLine(execute
            ? $"Deleting all components from {url} -> repository '{repo}' ..."
            : $"DRY RUN: enumerating components in {url} -> repository '{repo}' (re-run with --yes to actually delete)");

        var components = new List<NexusComponent>();
        try
        {
            await foreach (var c in client.GetComponentsAsync(repo, cancellationToken))
            {
                components.Add(c);
            }
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"ERROR: Failed to list components in '{repo}': {ex.Message}");
            return 1;
        }

        if (components.Count == 0)
        {
            Console.WriteLine($"Repository '{repo}' is empty. Nothing to delete.");
            return 0;
        }

        if (!execute)
        {
            Console.WriteLine($"Would delete {components.Count} components:");
            foreach (var c in components)
            {
                Console.WriteLine($"  {c.Name} {c.Version}");
            }
            Console.WriteLine();
            Console.WriteLine("Re-run with --yes to actually delete.");
            return 0;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var c in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await client.DeleteComponentAsync(c.Id, cancellationToken);
                deleted++;
                Console.WriteLine($"  [{deleted + failed}/{components.Count}] deleted {c.Name} {c.Version}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  [{deleted + failed}/{components.Count}] FAILED  {c.Name} {c.Version}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Deleted {deleted}/{components.Count} components ({failed} failed).");
        Console.WriteLine("Note: blob disk space is reclaimed by Nexus's 'Compact blob store' scheduled task, not immediately.");
        return failed == 0 ? 0 : 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage: Util.Cli nexus-purge-repo [options]

            Deletes every component from a Nexus hosted repository. Dry-run by default;
            pass --yes to actually delete.

            Options:
              --repo <name>      Repository name (default: nuget-hosted)
              --yes              Actually delete (without this, dry-run only)
              --url <url>        Nexus base URL (default: $NEXUS_URL or http://nexus:8081)
              --user <user>      Nexus username (default: $NEXUS_USER or admin)
              --password <pass>  Nexus password (default: $NEXUS_PASS - required)
              --help, -h         Show this help

            Environment variables: NEXUS_URL, NEXUS_USER, NEXUS_PASS
            """);
    }
}
