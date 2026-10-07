using Cardflow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);

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
    ResponseWriter = (context, report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            instance = app.Configuration["InstanceName"]
                ?? Environment.MachineName,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString())
        }, cancellationToken: context.RequestAborted)
});

app.Run();
