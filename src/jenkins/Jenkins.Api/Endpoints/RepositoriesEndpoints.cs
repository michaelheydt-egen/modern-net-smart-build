using FluentValidation;
using Jenkins.Application.Features.Repositories;
using Jenkins.Contracts.Repositories;

namespace Jenkins.Api.Endpoints;

/// <summary>
/// HTTP surface for the <c>SourceRepository</c> aggregate: register, list, detail,
/// and the container→deployment component mapping (decision #2). FluentValidation
/// runs inline so the failure shape is consistent across endpoints.
/// </summary>
public static class RepositoriesEndpoints
{
    public static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jenkins/repositories").WithTags("Repositories");

        // --- Reads ---

        group.MapGet("", async (
            bool? onlyActive,
            ListRepositoriesHandler handler,
            CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(new ListRepositoriesQuery(onlyActive), ct));
        });

        group.MapGet("{id:guid}", async (
            Guid id,
            GetRepositoryByIdHandler handler,
            CancellationToken ct) =>
        {
            var hit = await handler.HandleAsync(new GetRepositoryByIdQuery(id), ct);
            return hit is null ? Results.NotFound() : Results.Ok(hit);
        });

        // --- Lifecycle ---

        group.MapPost("", async (
            RegisterRepositoryRequest body,
            RegisterRepositoryHandler handler,
            IValidator<RegisterRepositoryCommand> validator,
            CancellationToken ct) =>
        {
            var cmd = new RegisterRepositoryCommand(
                Guid.NewGuid(), body.Name, body.GitUrl, body.Provider,
                body.DefaultBranch, body.CiJobName, body.BaseVersion);
            return await ValidateAndRun(validator, cmd, ct, async () =>
            {
                var dto = await handler.HandleAsync(cmd, ct);
                return Results.Created($"/api/jenkins/repositories/{dto.Id}", dto);
            });
        });

        group.MapPut("{id:guid}", async (
            Guid id,
            UpdateRepositoryRequest body,
            UpdateRepositoryHandler handler,
            IValidator<UpdateRepositoryCommand> validator,
            CancellationToken ct) =>
        {
            var cmd = new UpdateRepositoryCommand(
                id, body.Name, body.GitUrl, body.Provider,
                body.DefaultBranch, body.CiJobName, body.BaseVersion);
            return await ValidateAndRun(validator, cmd, ct, async () =>
                Results.Ok(await handler.HandleAsync(cmd, ct)));
        });

        group.MapPost("{id:guid}/active", async (
            Guid id,
            SetRepositoryActiveRequest body,
            SetRepositoryActiveHandler handler,
            IValidator<SetRepositoryActiveCommand> validator,
            CancellationToken ct) =>
        {
            var cmd = new SetRepositoryActiveCommand(id, body.IsActive);
            return await ValidateAndRun(validator, cmd, ct, async () =>
                Results.Ok(await handler.HandleAsync(cmd, ct)));
        });

        // --- Component mapping (upsert by container name) ---

        group.MapPost("{id:guid}/components", async (
            Guid id,
            MapComponentRequest body,
            MapComponentHandler handler,
            IValidator<MapComponentCommand> validator,
            CancellationToken ct) =>
        {
            var cmd = new MapComponentCommand(
                id, Guid.NewGuid(), body.ContainerName,
                body.DeployableUnitId, body.DeployableUnitName, body.AutoPublish);
            return await ValidateAndRun(validator, cmd, ct, async () =>
                Results.Ok(await handler.HandleAsync(cmd, ct)));
        });

        return app;
    }

    internal static async Task<IResult> ValidateAndRun<TCommand>(
        IValidator<TCommand> validator,
        TCommand cmd,
        CancellationToken ct,
        Func<Task<IResult>> run)
    {
        var result = await validator.ValidateAsync(cmd, ct);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray());
            return Results.ValidationProblem(errors);
        }

        try
        {
            return await run();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "Invalid operation", detail: ex.Message, statusCode: 409);
        }
    }
}
