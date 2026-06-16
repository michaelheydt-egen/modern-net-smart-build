using FluentValidation;

namespace Deployment.Api.Endpoints;

internal static class EndpointHelpers
{
    internal static async Task<IResult> ValidateAndRun<TCommand>(
        IValidator<TCommand> validator, TCommand cmd, CancellationToken ct, Func<Task<IResult>> run)
    {
        var result = await validator.ValidateAsync(cmd, ct);
        if (!result.IsValid)
        {
            var errors = result.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray());
            return Results.ValidationProblem(errors);
        }
        try { return await run(); }
        catch (InvalidOperationException ex) { return Results.Problem(title: "Invalid operation", detail: ex.Message, statusCode: 409); }
    }
}
