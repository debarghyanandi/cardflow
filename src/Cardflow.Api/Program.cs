var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", (HttpContext context) =>
{
    var instance = Environment.GetEnvironmentVariable("CARDFLOW_INSTANCE") ?? "local";
    context.Response.Headers["X-Cardflow-Instance"] = instance;
    return Results.Ok(new { status = "healthy", instance });
});

app.Run();
