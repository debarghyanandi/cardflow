using Cardflow.Api.Data;
using Cardflow.Api.Exceptions;
using Cardflow.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;

namespace Cardflow.Api.Extensions;

/// <summary>
/// Wires up the request pipeline: <see cref="BoardProblem"/> to a problem
/// response, the health check, the SignalR hub, and the controllers. Also
/// runs the database migration once at startup. The equivalent of your
/// reference project's <c>ApiStartup.Configure</c> — kept here as an
/// extension method rather than a Startup class, because Cardflow hosts
/// itself directly (Kestrel in Docker) with no Lambda entry point to split
/// away from.
/// </summary>
public static class WebApplicationExtensions
{
    public static async Task<WebApplication> UseCardflowPipelineAsync(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BoardProblem problem)
            {
                await Results.Problem(statusCode: problem.StatusCode, detail: problem.Message).ExecuteAsync(context);
            }
        });

        app.MapGet("/health", (HttpContext context) =>
        {
            var instance = Environment.GetEnvironmentVariable("CARDFLOW_INSTANCE") ?? "local";
            context.Response.Headers["X-Cardflow-Instance"] = instance;
            return Results.Ok(new { status = "healthy", instance });
        });

        app.MapHub<BoardHub>("/hubs/board", options => options.Transports = HttpTransportType.WebSockets);
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await DatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<CardflowDbContext>());
        }

        return app;
    }
}
