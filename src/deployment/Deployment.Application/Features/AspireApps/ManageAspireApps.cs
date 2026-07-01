using FluentValidation;
using Deployment.Contracts.AspireApps;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.Environments;

namespace Deployment.Application.Features.AspireApps;

internal static class AspireAppMapping
{
    public static AspireApplicationDto ToDto(this AspireApplication a, string environmentName) =>
        new(a.Id, a.Name, a.Description, a.EnvironmentId, environmentName, a.ManifestSource, a.Version, a.SourceKey, a.IsActive, a.AutoDeploy, a.CreatedAtUtc, a.UpdatedAtUtc);
}

public sealed record CreateAspireApplicationCommand(string Name, string? Description, Guid EnvironmentId, string ManifestSource, string? Version, string? SourceKey = null);

public sealed class CreateAspireApplicationValidator : AbstractValidator<CreateAspireApplicationCommand>
{
    public CreateAspireApplicationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.ManifestSource).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Version).MaximumLength(200);
        RuleFor(x => x.SourceKey).MaximumLength(200);
    }
}

public sealed class CreateAspireApplicationHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public CreateAspireApplicationHandler(IAspireApplicationRepository apps, IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _apps = apps; _envs = envs; _uow = uow; _clock = clock; }

    public async Task<AspireApplicationDto> HandleAsync(CreateAspireApplicationCommand cmd, CancellationToken ct = default)
    {
        if (await _apps.FindByNameAsync(cmd.Name, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"An Aspire application named '{cmd.Name}' already exists.");
        var env = await RequireKubernetesEnvironmentAsync(_envs, cmd.EnvironmentId, ct).ConfigureAwait(false);
        var app = new AspireApplication(Guid.NewGuid(), cmd.Name, cmd.Description, cmd.EnvironmentId, cmd.ManifestSource, cmd.Version, cmd.SourceKey, _clock.GetUtcNow());
        await _apps.AddAsync(app, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return app.ToDto(env.Name);
    }

    internal static async Task<DeploymentEnvironment> RequireKubernetesEnvironmentAsync(IEnvironmentRepository envs, Guid environmentId, CancellationToken ct)
    {
        var env = await envs.GetByIdAsync(environmentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Environment {environmentId} not found.");
        if (string.IsNullOrWhiteSpace(env.KubernetesContext) || string.IsNullOrWhiteSpace(env.KubernetesNamespace))
            throw new InvalidOperationException($"Environment '{env.Name}' has no Kubernetes target (set its KubernetesContext + KubernetesNamespace).");
        return env;
    }
}

public sealed record UpdateAspireApplicationCommand(Guid ApplicationId, string Name, string? Description, Guid EnvironmentId, string ManifestSource, string? Version, string? SourceKey = null);

public sealed class UpdateAspireApplicationValidator : AbstractValidator<UpdateAspireApplicationCommand>
{
    public UpdateAspireApplicationValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.ManifestSource).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Version).MaximumLength(200);
        RuleFor(x => x.SourceKey).MaximumLength(200);
    }
}

public sealed class UpdateAspireApplicationHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public UpdateAspireApplicationHandler(IAspireApplicationRepository apps, IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _apps = apps; _envs = envs; _uow = uow; _clock = clock; }

    public async Task HandleAsync(UpdateAspireApplicationCommand cmd, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(cmd.ApplicationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Aspire application {cmd.ApplicationId} not found.");
        await CreateAspireApplicationHandler.RequireKubernetesEnvironmentAsync(_envs, cmd.EnvironmentId, ct).ConfigureAwait(false);
        app.Update(cmd.Name, cmd.Description, cmd.EnvironmentId, cmd.ManifestSource, cmd.Version, cmd.SourceKey, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record SetAspireAutoDeployCommand(Guid ApplicationId, bool AutoDeploy);

public sealed class SetAspireAutoDeployHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public SetAspireAutoDeployHandler(IAspireApplicationRepository apps, IUnitOfWork uow, TimeProvider clock)
    { _apps = apps; _uow = uow; _clock = clock; }

    public async Task HandleAsync(SetAspireAutoDeployCommand cmd, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(cmd.ApplicationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Aspire application {cmd.ApplicationId} not found.");
        app.SetAutoDeploy(cmd.AutoDeploy, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record DeleteAspireApplicationCommand(Guid ApplicationId);

public sealed class DeleteAspireApplicationHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IUnitOfWork _uow;
    public DeleteAspireApplicationHandler(IAspireApplicationRepository apps, IUnitOfWork uow)
    { _apps = apps; _uow = uow; }

    public async Task HandleAsync(DeleteAspireApplicationCommand cmd, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(cmd.ApplicationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Aspire application {cmd.ApplicationId} not found.");
        _apps.Remove(app);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
