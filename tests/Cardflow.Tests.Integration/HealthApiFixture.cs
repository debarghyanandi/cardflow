using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Cardflow.Tests.Integration;

public sealed class HealthApiFixture : IAsyncLifetime
{
    public const string InstanceName = "health-integration-test";

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cardflow_tests")
        .WithUsername("cardflow_tests")
        .WithPassword("cardflow_tests")
        .Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public HttpClient Client { get; private set; } = null!;

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync());

            _factory = new HealthWebApplicationFactory(
                Postgres.GetConnectionString(), Redis.GetConnectionString());

            Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();

        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
        finally
        {
            await Task.WhenAll(
                Postgres.DisposeAsync().AsTask(), Redis.DisposeAsync().AsTask());
        }
    }

    private sealed class HealthWebApplicationFactory(
        string postgresConnection, string redisConnection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", postgresConnection);
            builder.UseSetting("ConnectionStrings:Redis", redisConnection);
            builder.UseSetting("InstanceName", InstanceName);
        }
    }
}
