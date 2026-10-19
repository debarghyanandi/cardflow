using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cardflow.Tests.Integration;

public sealed class HttpsRedirectionTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.TemporaryRedirect)]
    [InlineData("false", HttpStatusCode.OK)]
    public async Task HttpLiveness_RedirectsByDefaultButAllowsLocalComposeHttp(
        string? enabled, HttpStatusCode expectedStatus)
    {
        using var factory = new HttpApiFactory(enabled);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.TemporaryRedirect)
        {
            Assert.Equal("https://localhost:7443/health/live", response.Headers.Location?.ToString());
        }
        else
        {
            Assert.Null(response.Headers.Location);
        }
    }

    private sealed class HttpApiFactory(string? enabled) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InstanceName", "http-test");
            builder.UseSetting("ConnectionStrings:Postgres",
                "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
            builder.ConfigureServices(services =>
                services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 7443));
            if (enabled is not null)
            {
                builder.UseSetting("HttpsRedirection:Enabled", enabled);
            }
        }
    }
}
