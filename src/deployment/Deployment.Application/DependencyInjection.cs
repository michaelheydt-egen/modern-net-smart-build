using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Deployment.Application.Features.AspireApps;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Runs;
using Deployment.Application.Features.Services;

namespace Deployment.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application-layer DI: FluentValidation validators (scanned) + handlers (explicit, one line
    /// each). The bus consumer, executor, and translator are discovered by Wolverine.
    /// </summary>
    public static IServiceCollection AddDeploymentApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<DependencyInjectionMarker>(includeInternalTypes: true);

        // Services
        services.AddScoped<CreateServiceHandler>();
        services.AddScoped<UpdateServiceHandler>();
        services.AddScoped<ChangeServiceActivationHandler>();
        services.AddScoped<DeleteServiceHandler>();
        services.AddScoped<ListServicesHandler>();
        services.AddScoped<GetServiceByIdHandler>();

        // Environments
        services.AddScoped<CreateEnvironmentHandler>();
        services.AddScoped<UpdateEnvironmentHandler>();
        services.AddScoped<DeleteEnvironmentHandler>();
        services.AddScoped<ListEnvironmentsHandler>();
        services.AddScoped<GetEnvironmentByIdHandler>();

        // Mappings
        services.AddScoped<CreateMappingHandler>();
        services.AddScoped<UpdateMappingHandler>();
        services.AddScoped<SetAutoDeployHandler>();
        services.AddScoped<DeleteMappingHandler>();
        services.AddScoped<ListMappingsHandler>();
        services.AddScoped<GetMappingByIdHandler>();

        // Runs + inventory
        services.AddScoped<RequestDeploymentHandler>();
        services.AddScoped<ListRunsHandler>();
        services.AddScoped<GetRunByIdHandler>();
        services.AddScoped<ListKnownContainersHandler>();
        services.AddScoped<AddKnownContainerHandler>();

        // Aspire applications (Aspir8 → Kubernetes)
        services.AddScoped<CreateAspireApplicationHandler>();
        services.AddScoped<UpdateAspireApplicationHandler>();
        services.AddScoped<DeleteAspireApplicationHandler>();
        services.AddScoped<SetAspireAutoDeployHandler>();
        services.AddScoped<ListAspireApplicationsHandler>();
        services.AddScoped<GetAspireApplicationByIdHandler>();
        services.AddScoped<RequestAspireDeploymentHandler>();
        services.AddScoped<RollbackAspireDeploymentHandler>();
        services.AddScoped<PromoteAspireDeploymentHandler>();
        services.AddScoped<ListAspireRunsHandler>();
        services.AddScoped<GetAspireRunByIdHandler>();

        return services;
    }

    internal sealed class DependencyInjectionMarker { }
}
