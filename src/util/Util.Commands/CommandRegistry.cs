using Util.Commands.Abstractions;
using Util.Commands.Nexus;

namespace Util.Commands;

public static class CommandRegistry
{
    public static IReadOnlyDictionary<string, ICommand> Commands { get; } = BuildRegistry();

    private static IReadOnlyDictionary<string, ICommand> BuildRegistry()
    {
        var commands = new ICommand[]
        {
            new PurgeRepoCommand(),
        };
        return commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }
}
