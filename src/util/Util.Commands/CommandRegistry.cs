using Util.Commands.Abstractions;
using Util.Commands.Nexus;
using Util.Commands.Pipeline;

namespace Util.Commands;

public static class CommandRegistry
{
    public static IReadOnlyDictionary<string, ICommand> Commands { get; } = BuildRegistry();

    private static IReadOnlyDictionary<string, ICommand> BuildRegistry()
    {
        var commands = new ICommand[]
        {
            new PurgeRepoCommand(),
            new RunPipelineCommand(),
        };
        return commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }
}
