using FluentValidation;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Services;
using Deployment.Contracts.Catalog;

namespace Deployment.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/deployment/services").WithTags("Services");

        g.MapGet("", async (ListServicesHandler h, CancellationToken ct) => Results.Ok(await h.HandleAsync(new ListServicesQuery(), ct)));
        g.MapGet("{id:guid}", async (Guid id, GetServiceByIdHandler h, CancellationToken ct) =>
            await h.HandleAsync(new GetServiceByIdQuery(id), ct) is { } d ? Results.Ok(d) : Results.NotFound());

        g.MapPost("", async (CreateServiceRequest body, CreateServiceHandler h, IValidator<CreateServiceCommand> v, CancellationToken ct) =>
        {
            var cmd = new CreateServiceCommand(body.Name, body.ContainerName);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () =>
            {
                var dto = await h.HandleAsync(cmd, ct);
                return Results.Created($"/api/deployment/services/{dto.Id}", dto);
            });
        });

        g.MapPut("{id:guid}", async (Guid id, UpdateServiceRequest body, UpdateServiceHandler h, IValidator<UpdateServiceCommand> v, CancellationToken ct) =>
        {
            var cmd = new UpdateServiceCommand(id, body.Name, body.ContainerName);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () => { await h.HandleAsync(cmd, ct); return Results.NoContent(); });
        });

        g.MapPost("{id:guid}/activate", async (Guid id, ChangeServiceActivationHandler h, CancellationToken ct) =>
        { await h.HandleAsync(new ChangeServiceActivationCommand(id, true), ct); return Results.NoContent(); });
        g.MapPost("{id:guid}/deactivate", async (Guid id, ChangeServiceActivationHandler h, CancellationToken ct) =>
        { await h.HandleAsync(new ChangeServiceActivationCommand(id, false), ct); return Results.NoContent(); });

        g.MapDelete("{id:guid}", async (Guid id, DeleteServiceHandler h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new DeleteServiceCommand(id), ct); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Problem(title: "Cannot delete", detail: ex.Message, statusCode: 409); }
        });

        return app;
    }

    public static IEndpointRouteBuilder MapEnvironmentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/deployment/environments").WithTags("Environments");

        g.MapGet("", async (ListEnvironmentsHandler h, CancellationToken ct) => Results.Ok(await h.HandleAsync(new ListEnvironmentsQuery(), ct)));
        g.MapGet("{id:guid}", async (Guid id, GetEnvironmentByIdHandler h, CancellationToken ct) =>
            await h.HandleAsync(new GetEnvironmentByIdQuery(id), ct) is { } d ? Results.Ok(d) : Results.NotFound());

        g.MapPost("", async (CreateEnvironmentRequest body, CreateEnvironmentHandler h, IValidator<CreateEnvironmentCommand> v, CancellationToken ct) =>
        {
            var cmd = new CreateEnvironmentCommand(body.Name, body.GcpProject, body.Region, body.GarRepository);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () =>
            {
                var dto = await h.HandleAsync(cmd, ct);
                return Results.Created($"/api/deployment/environments/{dto.Id}", dto);
            });
        });

        g.MapPut("{id:guid}", async (Guid id, UpdateEnvironmentRequest body, UpdateEnvironmentHandler h, IValidator<UpdateEnvironmentCommand> v, CancellationToken ct) =>
        {
            var cmd = new UpdateEnvironmentCommand(id, body.Name, body.GcpProject, body.Region, body.GarRepository);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () => { await h.HandleAsync(cmd, ct); return Results.NoContent(); });
        });

        g.MapDelete("{id:guid}", async (Guid id, DeleteEnvironmentHandler h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new DeleteEnvironmentCommand(id), ct); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Problem(title: "Cannot delete", detail: ex.Message, statusCode: 409); }
        });

        return app;
    }
}
