using Jenkins.Contracts.Repositories;
using Jenkins.Domain.Abstractions;
using Jenkins.Domain.SourceRepositories;
using FluentValidation;

namespace Jenkins.Application.Features.Repositories;

/// <summary>
/// Update a repository's editable identity/CI fields. Re-checks the unique-name
/// invariant against <em>other</em> repos (a rename can't collide).
/// </summary>
public sealed record UpdateRepositoryCommand(
    Guid Id,
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    BuildKindDto BuildKind = BuildKindDto.Standard,
    string? AppHostPath = null);

public sealed class UpdateRepositoryValidator : AbstractValidator<UpdateRepositoryCommand>
{
    public UpdateRepositoryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GitUrl).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DefaultBranch).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CiJobName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseVersion).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AppHostPath).MaximumLength(500);
    }
}

public sealed class UpdateRepositoryHandler
{
    private readonly ISourceRepositoryStore _repositories;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateRepositoryHandler(ISourceRepositoryStore repositories, IUnitOfWork uow, TimeProvider clock)
    {
        _repositories = repositories;
        _uow = uow;
        _clock = clock;
    }

    public async Task<RepositoryDto> HandleAsync(UpdateRepositoryCommand cmd, CancellationToken cancellationToken = default)
    {
        var repository = await _repositories.GetByIdAsync(cmd.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Repository {cmd.Id} not found.");

        var clash = await _repositories.FindByNameAsync(cmd.Name, cancellationToken).ConfigureAwait(false);
        if (clash is not null && clash.Id != cmd.Id)
            throw new InvalidOperationException($"A repository named '{cmd.Name}' already exists.");

        repository.UpdateDetails(
            name: cmd.Name,
            gitUrl: cmd.GitUrl,
            provider: cmd.Provider.ToDomain(),
            defaultBranch: cmd.DefaultBranch,
            ciJobName: cmd.CiJobName,
            baseVersion: cmd.BaseVersion,
            occurredAtUtc: _clock.GetUtcNow(),
            buildKind: cmd.BuildKind.ToDomain(),
            appHostPath: cmd.AppHostPath);

        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return repository.ToDto();
    }
}
