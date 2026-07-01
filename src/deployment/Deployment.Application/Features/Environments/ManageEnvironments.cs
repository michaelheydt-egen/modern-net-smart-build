using FluentValidation;
using Deployment.Contracts.Catalog;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;

namespace Deployment.Application.Features.Environments;

internal static class EnvironmentMapping
{
    public static EnvironmentDto ToDto(this DeploymentEnvironment e) =>
        new(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.KubernetesContext, e.KubernetesNamespace,
            e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc);
}

public sealed record CreateEnvironmentCommand(
    string Name, string? GcpProject, string? Region, string? GarRepository, string? KubernetesContext, string? KubernetesNamespace);

/// <summary>An environment must describe at least one target: Cloud Run (GCP project + region) or Kubernetes (context + namespace).</summary>
internal static class EnvironmentRules
{
    public static bool HasCloudRun(string? project, string? region) => !string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(region);
    public static bool HasKubernetes(string? ctx, string? ns) => !string.IsNullOrWhiteSpace(ctx) && !string.IsNullOrWhiteSpace(ns);
    public const string TargetMessage = "Specify a Cloud Run target (GcpProject + Region) and/or a Kubernetes target (KubernetesContext + KubernetesNamespace).";
}

public sealed class CreateEnvironmentValidator : AbstractValidator<CreateEnvironmentCommand>
{
    public CreateEnvironmentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GcpProject).MaximumLength(200);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.GarRepository).MaximumLength(200);
        RuleFor(x => x.KubernetesContext).MaximumLength(200);
        RuleFor(x => x.KubernetesNamespace).MaximumLength(200);
        RuleFor(x => x).Must(c => EnvironmentRules.HasCloudRun(c.GcpProject, c.Region) || EnvironmentRules.HasKubernetes(c.KubernetesContext, c.KubernetesNamespace))
            .WithMessage(EnvironmentRules.TargetMessage);
    }
}

public sealed class CreateEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public CreateEnvironmentHandler(IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _envs = envs; _uow = uow; _clock = clock; }

    public async Task<EnvironmentDto> HandleAsync(CreateEnvironmentCommand cmd, CancellationToken ct = default)
    {
        if (await _envs.FindByNameAsync(cmd.Name, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"An environment named '{cmd.Name}' already exists.");
        var env = new DeploymentEnvironment(Guid.NewGuid(), cmd.Name, cmd.GcpProject, cmd.Region, cmd.GarRepository,
            cmd.KubernetesContext, cmd.KubernetesNamespace, _clock.GetUtcNow());
        await _envs.AddAsync(env, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return env.ToDto();
    }
}

public sealed record UpdateEnvironmentCommand(
    Guid EnvironmentId, string Name, string? GcpProject, string? Region, string? GarRepository, string? KubernetesContext, string? KubernetesNamespace);

public sealed class UpdateEnvironmentValidator : AbstractValidator<UpdateEnvironmentCommand>
{
    public UpdateEnvironmentValidator()
    {
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GcpProject).MaximumLength(200);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.KubernetesContext).MaximumLength(200);
        RuleFor(x => x.KubernetesNamespace).MaximumLength(200);
        RuleFor(x => x).Must(c => EnvironmentRules.HasCloudRun(c.GcpProject, c.Region) || EnvironmentRules.HasKubernetes(c.KubernetesContext, c.KubernetesNamespace))
            .WithMessage(EnvironmentRules.TargetMessage);
    }
}

public sealed class UpdateEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public UpdateEnvironmentHandler(IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _envs = envs; _uow = uow; _clock = clock; }

    public async Task HandleAsync(UpdateEnvironmentCommand cmd, CancellationToken ct = default)
    {
        var env = await _envs.GetByIdAsync(cmd.EnvironmentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Environment {cmd.EnvironmentId} not found.");
        env.Update(cmd.Name, cmd.GcpProject, cmd.Region, cmd.GarRepository, cmd.KubernetesContext, cmd.KubernetesNamespace, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record DeleteEnvironmentCommand(Guid EnvironmentId);

public sealed class DeleteEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IAspireApplicationRepository _aspireApps;
    private readonly IUnitOfWork _uow;
    public DeleteEnvironmentHandler(IEnvironmentRepository envs, IDeploymentMappingRepository mappings, IAspireApplicationRepository aspireApps, IUnitOfWork uow)
    { _envs = envs; _mappings = mappings; _aspireApps = aspireApps; _uow = uow; }

    public async Task HandleAsync(DeleteEnvironmentCommand cmd, CancellationToken ct = default)
    {
        var env = await _envs.GetByIdAsync(cmd.EnvironmentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Environment {cmd.EnvironmentId} not found.");
        var maps = await _mappings.ListByEnvironmentAsync(env.Id, ct).ConfigureAwait(false);
        if (maps.Count > 0)
            throw new InvalidOperationException($"Environment '{env.Name}' is referenced by {maps.Count} mapping(s); remove them first.");
        // Guard the Aspire-app reference too — otherwise deleting leaves the app with a dangling
        // EnvironmentId and its deploy fails with 'environment-not-found'.
        var apps = await _aspireApps.ListByEnvironmentAsync(env.Id, ct).ConfigureAwait(false);
        if (apps.Count > 0)
            throw new InvalidOperationException(
                $"Environment '{env.Name}' is referenced by {apps.Count} Aspire application(s) " +
                $"({string.Join(", ", apps.Take(5).Select(a => a.Name))}); reassign or remove them first.");
        _envs.Remove(env);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
