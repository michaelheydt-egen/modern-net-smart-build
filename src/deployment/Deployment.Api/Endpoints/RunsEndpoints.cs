using FluentValidation;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Runs;
using Deployment.Contracts.Runs;

namespace Deployment.Api.Endpoints;

public static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/deployment/runs").WithTags("Runs");
        g.MapGet("", async (Guid? serviceId, Guid? mappingId, ListRunsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new ListRunsQuery(serviceId, mappingId), ct)));
        g.MapGet("{id:guid}", async (Guid id, GetRunByIdHandler h, CancellationToken ct) =>
            await h.HandleAsync(new GetRunByIdQuery(id), ct) is { } d ? Results.Ok(d) : Results.NotFound());

        // The light container inventory (latest push per name) — what manual deploys draw from.
        app.MapGet("/api/deployment/containers", async (ListKnownContainersHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new ListKnownContainersQuery(), ct))).WithTags("Containers");

        // Manually seed the inventory (testing the deploy flow without a live CI push).
        app.MapPost("/api/deployment/containers", async (AddKnownContainerRequest body, AddKnownContainerHandler h, IValidator<AddKnownContainerCommand> v, CancellationToken ct) =>
        {
            var cmd = new AddKnownContainerCommand(body.ContainerName, body.Version, body.NexusRef);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () =>
            {
                var dto = await h.HandleAsync(cmd, ct);
                return Results.Created($"/api/deployment/containers", dto);
            });
        }).WithTags("Containers");

        return app;
    }
}
