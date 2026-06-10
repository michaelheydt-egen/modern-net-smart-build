using Jenkins.Contracts.Repositories;
using Jenkins.Domain.Abstractions;
using Jenkins.Domain.SourceRepositories;
using FluentValidation;

namespace Jenkins.Application.Features.Repositories;

/// <summary>
/// Activate or deactivate a repository. Idempotent — the aggregate no-ops when
/// already in the requested state.
/// </summary>
public sealed record SetRepositoryActiveCommand(Guid Id, bool IsActive);

public sealed class SetRepositoryActiveValidator : AbstractValidator<SetRepositoryActiveCommand>
{
    public SetRepositoryActiveValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class SetRepositoryActiveHandler
{
    private readonly ISourceRepositoryStore _repositories;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SetRepositoryActiveHandler(ISourceRepositoryStore repositories, IUnitOfWork uow, TimeProvider clock)
    {
        _repositories = repositories;
        _uow = uow;
        _clock = clock;
    }

    public async Task<RepositoryDto> HandleAsync(SetRepositoryActiveCommand cmd, CancellationToken cancellationToken = default)
    {
        var repository = await _repositories.GetByIdAsync(cmd.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Repository {cmd.Id} not found.");

        var now = _clock.GetUtcNow();
        if (cmd.IsActive) repository.Reactivate(now);
        else repository.Deactivate(now);

        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return repository.ToDto();
    }
}
