using FluentValidation;
using Publisher.Application.Features.Containers;
using Publisher.Contracts.Containers;

namespace Publisher.Api.Endpoints;

/// <summary>
/// Read surface over the publisher's container inventory (sourced from local Nexus via the
/// CI <c>ContainerPublished</c> bus event). Write-side ingestion happens on the bus, not HTTP.
/// </summary>
public static class ContainersEndpoints
{
    public static IEndpointRouteBuilder MapContainersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/publisher/containers").WithTags("Containers");

        group.MapGet("", async (
            Guid? repositoryId,
            string? containerName,
            ListContainersHandler handler,
            CancellationToken ct) =>
        {
            var list = await handler.HandleAsync(new ListContainersQuery(repositoryId, containerName), ct);
            return Results.Ok(list);
        });

        group.MapGet("{id:guid}", async (
            Guid id,
            GetContainerByIdHandler handler,
            CancellationToken ct) =>
        {
            var hit = await handler.HandleAsync(new GetContainerByIdQuery(id), ct);
            return hit is null ? Results.NotFound() : Results.Ok(hit);
        });

        // Manually add a container (picked from the local Nexus docker registry in the UI). Active.
        group.MapPost("", async (
            AddContainerRequest body,
            AddContainerManuallyHandler handler,
            GetContainerByIdHandler get,
            IValidator<AddContainerManuallyCommand> validator,
            CancellationToken ct) =>
        {
            var cmd = new AddContainerManuallyCommand(body.ContainerName, body.Version, body.CommitSha, body.ArtifactUri);
            return await EndpointHelpers.ValidateAndRun(validator, cmd, ct, async () =>
            {
                var id = await handler.HandleAsync(cmd, ct);
                var dto = await get.HandleAsync(new GetContainerByIdQuery(id), ct);
                return Results.Created($"/api/publisher/containers/{id}", dto);
            });
        });

        group.MapPost("{id:guid}/activate", async (Guid id, ChangeContainerActivationHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeContainerActivationCommand(id, Active: true), ct);
            return Results.NoContent();
        });

        group.MapPost("{id:guid}/deactivate", async (Guid id, ChangeContainerActivationHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeContainerActivationCommand(id, Active: false), ct);
            return Results.NoContent();
        });

        group.MapDelete("{id:guid}", async (Guid id, DeleteContainerHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.HandleAsync(new DeleteContainerCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
