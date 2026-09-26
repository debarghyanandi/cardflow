using Cardflow.Api.Boards;
using Cardflow.Api.Data;
using Cardflow.Api.Ordering;
using Cardflow.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CardflowDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<BoardEventStore>();
builder.Services.AddScoped<BoardCommandService>();
builder.Services.AddScoped<BoardSyncService>();
builder.Services.AddSingleton<BoardEventPublisher>();
builder.Services.AddHostedService<RankMaintenance>();
builder.Services.AddSignalR().AddStackExchangeRedis(
    builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("A Redis connection string is required."));
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
app.MapHub<BoardHub>("/hubs/board", options => options.Transports = HttpTransportType.WebSockets);

var boards = app.MapGroup("/api/boards");
boards.MapPost("/", async (CreateBoardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = SessionCookies.ExistingOrNew(context);
    var mutation = await service.CreateBoardAsync(request, session, cancellationToken);
    await publisher.PublishAsync(mutation.Event);
    SessionCookies.Set(context, session);
    return Results.Created($"/api/boards/{mutation.Value.Token}", mutation.Value);
});
boards.MapPost("/{token}/join", async (string token, JoinBoardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = SessionCookies.ExistingOrNew(context);
    var mutation = await service.JoinAsync(token, request, session, cancellationToken);
    await publisher.PublishAsync(mutation.Event);
    SessionCookies.Set(context, session);
    return Results.Ok(mutation.Value);
});
boards.MapGet("/{token}", (string token, BoardService service, HttpContext context, CancellationToken cancellationToken) =>
    service.SnapshotAsync(token, SessionCookies.Existing(context), cancellationToken));
boards.MapPatch("/{token}", (string token, RenameBoardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.RenameBoardAsync(token, SessionCookies.Existing(context), request, cancellationToken), publisher));
boards.MapPost("/{token}/columns", (string token, CreateColumnRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.CreateColumnAsync(token, SessionCookies.Existing(context), request, cancellationToken), publisher));
boards.MapPatch("/{token}/columns/{columnId:guid}", (string token, Guid columnId, RenameColumnRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.RenameColumnAsync(token, SessionCookies.Existing(context), columnId, request, cancellationToken), publisher));
boards.MapPost("/{token}/columns/{columnId:guid}/cards", (string token, Guid columnId, CreateCardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.CreateCardAsync(token, SessionCookies.Existing(context), columnId, request, cancellationToken), publisher));
boards.MapPatch("/{token}/cards/{cardId:guid}", (string token, Guid cardId, EditCardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.EditCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken), publisher));
boards.MapPost("/{token}/cards/{cardId:guid}/move", (string token, Guid cardId, MoveCardRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.MoveCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken), publisher));
boards.MapPost("/{token}/cards/{cardId:guid}/archive", (string token, Guid cardId, VersionRequest request, BoardCommandService service, BoardEventPublisher publisher, HttpContext context, CancellationToken cancellationToken) =>
    PublishAsync(service.ArchiveCardAsync(token, SessionCookies.Existing(context), cardId, request, cancellationToken), publisher));

await using (var scope = app.Services.CreateAsyncScope())
{
    await DatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<CardflowDbContext>());
}

app.Run();

static async Task<IResult> PublishAsync<T>(Task<BoardMutation<T>> operation, BoardEventPublisher publisher)
{
    var mutation = await operation;
    await publisher.PublishAsync(mutation.Event);
    return Results.Ok(mutation.Value);
}
