using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;
using Deployment.Application.Features.AspireApps;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Runs;
using Deployment.Application.Features.Services;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.AspireApps.Runs;
using Deployment.Domain.Containers;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;
using Deployment.Infrastructure.Aspirate;
using Deployment.Infrastructure.Gcp;
using Deployment.Infrastructure.Kubernetes;
using Deployment.Infrastructure.Nexus;
using Deployment.Infrastructure.Messaging;
using Deployment.Infrastructure.Persistence;
using Deployment.Infrastructure.Persistence.Readers;
using Deployment.Infrastructure.Persistence.Repositories;
using Deployment.Infrastructure.Steps;

namespace Deployment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDeploymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Deployment")
            ?? throw new InvalidOperationException("ConnectionStrings:Deployment is required by Deployment.Infrastructure.");

        services.AddDbContext<DeploymentDbContext>(opts => opts.UseSqlServer(connection));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, WolverineDomainEventDispatcher>();
        services.AddSingleton(TimeProvider.System);

        // Repositories
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IDeploymentMappingRepository, DeploymentMappingRepository>();
        services.AddScoped<IKnownContainerRepository, KnownContainerRepository>();
        services.AddScoped<IDeploymentRunRepository, DeploymentRunRepository>();
        services.AddScoped<IAspireApplicationRepository, AspireApplicationRepository>();
        services.AddScoped<IAspireApplicationRunRepository, AspireApplicationRunRepository>();

        // Readers
        services.AddScoped<IServiceReader, EfServiceReader>();
        services.AddScoped<IEnvironmentReader, EfEnvironmentReader>();
        services.AddScoped<IMappingReader, EfMappingReader>();
        services.AddScoped<IRunReader, EfRunReader>();
        services.AddScoped<IKnownContainerReader, EfKnownContainerReader>();
        services.AddScoped<IAspireApplicationReader, EfAspireApplicationReader>();
        services.AddScoped<IAspireApplicationRunReader, EfAspireApplicationRunReader>();

        // GCP adapters (ADC). Crane for Nexus→GAR, Cloud Run admin client for deploy.
        // ValidateOnStart fails the host immediately on a missing crane / bad timeouts, instead of
        // surfacing the misconfiguration as a late GarPush failure on the first deploy.
        services.AddSingleton<IValidateOptions<GoogleCloudRunOptions>, GoogleCloudRunOptionsValidator>();
        services.AddOptions<GoogleCloudRunOptions>()
            .Bind(configuration.GetSection(GoogleCloudRunOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IArtifactPromoter, CraneArtifactPromoter>();
        services.AddSingleton<ICloudRunDeployer, GoogleCloudRunDeployer>();

        // Aspir8 (aspirate) CLI shell-out for whole-Aspire-app deploys to Kubernetes.
        services.AddSingleton<IValidateOptions<AspireOptions>, AspireOptionsValidator>();
        services.AddOptions<AspireOptions>()
            .Bind(configuration.GetSection(AspireOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IAspirateRunner, AspirateRunner>();

        // Nexus digest resolver — pins Aspire deploy images to their sha256 (provenance). Optional:
        // disabled when Deployment:Nexus:RegistryV2Url is empty (the runner then keeps the floating tag).
        services.AddOptions<NexusRegistryOptions>().Bind(configuration.GetSection(NexusRegistryOptions.SectionName));
        services.AddSingleton<INexusImageDigestResolver, NexusImageDigestResolver>();

        // Pluggable step executors — one per DeploymentStepKind — fronted by a registry the run
        // handler resolves by kind (see IStepExecutorRegistry for why the handler can't inject the
        // executor collection directly).
        services.AddScoped<IDeploymentStepExecutor, GarPushStepExecutor>();
        services.AddScoped<IDeploymentStepExecutor, CloudRunDeployStepExecutor>();
        services.AddScoped<IDeploymentStepExecutor, KubernetesApplyStepExecutor>();
        services.AddScoped<IStepExecutorRegistry, StepExecutorRegistry>();

        // Kubernetes target: in-process KubernetesClient; cluster access via a kubeconfig + context.
        services.AddOptions<KubernetesOptions>().Bind(configuration.GetSection(KubernetesOptions.SectionName));
        services.AddSingleton<IKubeClientFactory, KubeClientFactory>();
        services.AddSingleton<IKubernetesDeployer, KubernetesDeployer>();
        services.AddScoped<Application.Features.AspireApps.IAspireClusterStatusReader, AspireClusterStatusReader>();

        return services;
    }
}
