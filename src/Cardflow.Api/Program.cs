using Cardflow.Api.Boards;
using Cardflow.Api.Data;
using Cardflow.Api.Ordering;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CardflowDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<BoardService>();
builder.Services.AddHostedService<RankMaintenance>();
var app = builder.Build();

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

var boards = app.MapGroup("/api/boards");
boards.MapPost("/", async (CreateBoardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = SessionCookies.ExistingOrNew(context);
    var created = await service.CreateBoardAsync(request, session, cancellationToken);
    SessionCookies.Set(context, session);
    return Results.Created($"/api/boards/{created.Token}", created);
});
boards.MapPost("/{token}/join", async (string token, JoinBoardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = SessionCookies.ExistingOrNew(context);
    var member = await service.JoinAsync(token, request, session, cancellationToken);
    SessionCookies.Set(context, session);
    return Results.Ok(member);
});
boards.MapGet("/{token}", (string token, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.SnapshotAsync(token, SessionCookies.Existing(context), cancellationToken));
boards.MapPatch("/{token}", (string token, RenameBoardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.RenameBoardAsync(token, SessionCookies.Existing(context), request, cancellationToken));
boards.MapPost("/{token}/columns", async (string token, CreateColumnRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateColumnAsync(token, SessionCookies.Existing(context), request, cancellationToken)));
boards.MapPatch("/{token}/columns/{columnId:guid}", (string token, Guid columnId, RenameColumnRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.RenameColumnAsync(token, SessionCookies.Existing(context), columnId, request, cancellationToken));
boards.MapPost("/{token}/columns/{columnId:guid}/cards", async (string token, Guid columnId, CreateCardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateCardAsync(token, SessionCookies.Existing(context), columnId, request, cancellationToken)));
boards.MapPatch("/{token}/cards/{cardId:guid}", (string token, Guid cardId, EditCardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.EditCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken));
boards.MapPost("/{token}/cards/{cardId:guid}/move", (string token, Guid cardId, MoveCardRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.MoveCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken));
boards.MapPost("/{token}/cards/{cardId:guid}/archive", (string token, Guid cardId, VersionRequest request, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.ArchiveCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken));

await using (var scope = app.Services.CreateAsyncScope())
{
    await DatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<CardflowDbContext>());
}

app.Run();
