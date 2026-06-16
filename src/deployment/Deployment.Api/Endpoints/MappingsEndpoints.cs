using FluentValidation;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Runs;
using Deployment.Contracts.Mappings;
using Deployment.Contracts.Runs;
using Deployment.Domain.Runs;

namespace Deployment.Api.Endpoints;

public static class MappingsEndpoints
{
    public static IEndpointRouteBuilder MapMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/deployment/mappings").WithTags("Mappings");

        g.MapGet("", async (Guid? serviceId, ListMappingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new ListMappingsQuery(serviceId), ct)));
        g.MapGet("{id:guid}", async (Guid id, GetMappingByIdHandler h, CancellationToken ct) =>
            await h.HandleAsync(new GetMappingByIdQuery(id), ct) is { } d ? Results.Ok(d) : Results.NotFound());

        g.MapPost("", async (CreateMappingRequest body, CreateMappingHandler h, IValidator<CreateMappingCommand> v, CancellationToken ct) =>
        {
            var cmd = new CreateMappingCommand(body.ServiceId, body.EnvironmentId, body.CloudRunServiceName, body.AutoDeploy, body.Steps);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () =>
            {
                var id = await h.HandleAsync(cmd, ct);
                return Results.Created($"/api/deployment/mappings/{id}", new { id });
            });
        });

        g.MapPut("{id:guid}", async (Guid id, UpdateMappingRequest body, UpdateMappingHandler h, IValidator<UpdateMappingCommand> v, CancellationToken ct) =>
        {
            var cmd = new UpdateMappingCommand(id, body.CloudRunServiceName, body.Steps);
            return await EndpointHelpers.ValidateAndRun(v, cmd, ct, async () => { await h.HandleAsync(cmd, ct); return Results.NoContent(); });
        });

        g.MapPost("{id:guid}/auto", async (Guid id, SetAutoDeployRequest body, SetAutoDeployHandler h, CancellationToken ct) =>
        { await h.HandleAsync(new SetAutoDeployCommand(id, body.AutoDeploy), ct); return Results.NoContent(); });

        g.MapDelete("{id:guid}", async (Guid id, DeleteMappingHandler h, CancellationToken ct) =>
        { await h.HandleAsync(new DeleteMappingCommand(id), ct); return Results.NoContent(); });

        // Manual trigger — deploy now (latest known container, or a specific version).
        g.MapPost("{id:guid}/deploy", async (Guid id, TriggerDeploymentRequest body, RequestDeploymentHandler h, CancellationToken ct) =>
        {
            try
            {
                var result = await h.HandleAsync(new RequestDeploymentCommand(id, body.Version, DeploymentTrigger.Manual, body.TriggeredBy ?? "manual"), ct);
                return result.RunId is null
                    ? Results.Ok(new { outcome = result.Outcome })
                    : Results.Accepted($"/api/deployment/runs/{result.RunId}", new { runId = result.RunId, outcome = result.Outcome });
            }
            catch (InvalidOperationException ex) { return Results.Problem(title: "Cannot deploy", detail: ex.Message, statusCode: 409); }
        });

        return app;
    }
}
