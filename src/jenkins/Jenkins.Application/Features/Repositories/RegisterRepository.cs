using Jenkins.Contracts.Repositories;
using Jenkins.Domain.Abstractions;
using Jenkins.Domain.SourceRepositories;
using FluentValidation;

namespace Jenkins.Application.Features.Repositories;

public sealed record RegisterRepositoryCommand(
    Guid Id,
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    BuildKindDto BuildKind = BuildKindDto.Standard,
    string? AppHostPath = null);

public sealed class RegisterRepositoryValidator : AbstractValidator<RegisterRepositoryCommand>
{
    public RegisterRepositoryValidator()
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

public sealed class RegisterRepositoryHandler
{
    private readonly ISourceRepositoryStore _repositories;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RegisterRepositoryHandler(ISourceRepositoryStore repositories, IUnitOfWork uow, TimeProvider clock)
    {
        _repositories = repositories;
        _uow = uow;
        _clock = clock;
    }

    public async Task<RepositoryDto> HandleAsync(RegisterRepositoryCommand cmd, CancellationToken cancellationToken = default)
    {
        var existing = await _repositories.FindByNameAsync(cmd.Name, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException($"A repository named '{cmd.Name}' already exists.");

        var repository = new SourceRepository(
            id: cmd.Id,
            name: cmd.Name,
            gitUrl: cmd.GitUrl,
            provider: cmd.Provider.ToDomain(),
            defaultBranch: cmd.DefaultBranch,
            ciJobName: cmd.CiJobName,
            baseVersion: cmd.BaseVersion,
            createdAtUtc: _clock.GetUtcNow(),
            buildKind: cmd.BuildKind.ToDomain(),
            appHostPath: cmd.AppHostPath);

        await _repositories.AddAsync(repository, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return repository.ToDto();
    }
}
