using Util.Commands;
using Util.Commands.Abstractions;

if (args.Length == 0 || !CommandRegistry.Commands.TryGetValue(args[0], out ICommand? command))
{
    Console.Error.WriteLine("Usage: Util.Cli <command> [args...]");
    if (CommandRegistry.Commands.Count > 0)
    {
        Console.Error.WriteLine("Available commands:");
        foreach (var name in CommandRegistry.Commands.Keys.OrderBy(n => n))
        {
            Console.Error.WriteLine($"  {name}");
        }
    }
    else
    {
        Console.Error.WriteLine("(no commands registered yet)");
    }
    return 1;
}

return await command.ExecuteAsync(args.Skip(1).ToArray());
