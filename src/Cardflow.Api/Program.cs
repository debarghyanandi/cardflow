using Cardflow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);

var instanceName = builder.Configuration["InstanceName"];
if (string.IsNullOrWhiteSpace(instanceName))
{
    throw new InvalidOperationException("InstanceName is required and must not be blank.");
}

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddInfrastructureHealthChecks(
    builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string is missing ."),
    builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string is missing."));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness checks the API process without probing its dependencies.
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});

app.Run();

Task WriteHealthResponse(HttpContext context, HealthReport report) =>
    context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        instance = instanceName,
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString())
    }, cancellationToken: context.RequestAborted);

public partial class Program { }
