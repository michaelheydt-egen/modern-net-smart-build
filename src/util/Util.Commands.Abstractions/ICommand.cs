namespace Util.Commands.Abstractions;

public interface ICommand
{
    string Name { get; }

    Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default);
}
