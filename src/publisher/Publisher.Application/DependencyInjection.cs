using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Publisher.Application.Features.Channels;
using Publisher.Application.Features.Containers;
using Publisher.Application.Features.Promotions;
using Publisher.Application.Features.Registries;
using Publisher.Application.Features.Rules;

namespace Publisher.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application-layer DI: FluentValidation validators (scanned) + handlers (registered
    /// explicitly, one line each, so the list reads as a catalog of capabilities).
    /// </summary>
    public static IServiceCollection AddPublisherApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<DependencyInjectionMarker>(includeInternalTypes: true);

        // Container inventory
        services.AddScoped<RecordContainerHandler>();
        services.AddScoped<AddContainerManuallyHandler>();
        services.AddScoped<ChangeContainerActivationHandler>();
        services.AddScoped<DeleteContainerHandler>();
        services.AddScoped<ListContainersHandler>();
        services.AddScoped<GetContainerByIdHandler>();

        // Publishable channels
        services.AddScoped<TagContainerPublishableHandler>();
        services.AddScoped<ListChannelsHandler>();
        services.AddScoped<GetChannelByNameHandler>();

        // Remote registries
        services.AddScoped<CreateRegistryHandler>();
        services.AddScoped<UpdateRegistryHandler>();
        services.AddScoped<ChangeRegistryActivationHandler>();
        services.AddScoped<SetDefaultRegistryHandler>();
        services.AddScoped<DeleteRegistryHandler>();
        services.AddScoped<ListRegistriesHandler>();
        services.AddScoped<GetRegistryByIdHandler>();

        // Automation rules
        services.AddScoped<CreateRuleHandler>();
        services.AddScoped<UpdateRuleHandler>();
        services.AddScoped<ChangeRuleActivationHandler>();
        services.AddScoped<DeleteRuleHandler>();
        services.AddScoped<ListRulesHandler>();
        services.AddScoped<GetRuleByIdHandler>();
        services.AddScoped<EvaluateContainerRulesHandler>();

        // Promotions
        services.AddScoped<PromoteContainerHandler>();
        services.AddScoped<RequestManualPromotionHandler>();
        services.AddScoped<ListPromotionsHandler>();
        services.AddScoped<GetPromotionByIdHandler>();

        // The bus consumer (ContainerPublishedConsumer), the promotion executor (PromotionExecutor),
        // and the success translator (PromotionSucceededTranslator) are discovered by Wolverine from
        // this assembly — no explicit registration needed.

        return services;
    }

    /// <summary>
    /// Empty marker — gives FluentValidation an assembly anchor without exposing a real type.
    /// </summary>
    internal sealed class DependencyInjectionMarker { }
}
