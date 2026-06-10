using FluentValidation;
using Jenkins.Application.Features.Builds;
using Jenkins.Application.Features.Handoffs;
using Jenkins.Application.Features.Pipelines;
using Jenkins.Application.Features.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Jenkins.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application layer: FluentValidation registrations + handlers (registered
    /// explicitly so the list reads as a catalog of capabilities). Wolverine
    /// handler discovery is wired in the host's Program.cs. Mirrors
    /// Deployment.Application.AddDeploymentApplication.
    /// </summary>
    public static IServiceCollection AddJenkinsApplication(this IServiceCollection services)
    {
        // Validators in Features/* are picked up here as they're added.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Repositories (SourceRepository) handlers.
        services.AddScoped<RegisterRepositoryHandler>();
        services.AddScoped<UpdateRepositoryHandler>();
        services.AddScoped<SetRepositoryActiveHandler>();
        services.AddScoped<MapComponentHandler>();
        services.AddScoped<ListRepositoriesHandler>();
        services.AddScoped<GetRepositoryByIdHandler>();

        // Build handlers.
        services.AddScoped<RecordBuildHandler>();
        services.AddScoped<CompleteBuildHandler>();
        services.AddScoped<RecordArtifactHandler>();
        services.AddScoped<ReconcileBuildArtifactsHandler>();
        services.AddScoped<ListBuildsHandler>();
        services.AddScoped<GetBuildByIdHandler>();

        // Handoff handlers.
        services.AddScoped<PromoteToReleaseHandler>();
        services.AddScoped<ListHandoffsByBuildHandler>();
        services.AddScoped<GetHandoffByIdHandler>();

        // Pipeline handlers.
        services.AddScoped<CreatePipelineHandler>();
        services.AddScoped<UpdatePipelineHandler>();
        services.AddScoped<SetPipelineActiveHandler>();
        services.AddScoped<DeletePipelineHandler>();
        services.AddScoped<AddStageHandler>();
        services.AddScoped<UpdateStageHandler>();
        services.AddScoped<RemoveStageHandler>();
        services.AddScoped<ReorderStagesHandler>();
        services.AddScoped<ListPipelinesHandler>();
        services.AddScoped<GetPipelineByIdHandler>();
        services.AddScoped<SeedDefaultPipelineHandler>();

        return services;
    }
}
