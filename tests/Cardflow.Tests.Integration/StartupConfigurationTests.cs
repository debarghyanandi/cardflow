using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cardflow.Tests.Integration;

public sealed class StartupConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Startup_RejectsMissingOrBlankInstanceName(string? instanceName)
    {
        using var factory = new InvalidInstanceNameFactory(instanceName);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Equal("InstanceName is required and must not be blank.", exception.Message);
    }

    private sealed class InvalidInstanceNameFactory(string? instanceName)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Production-style configuration has no development-only InstanceName default.
            builder.UseEnvironment("Testing");
            builder.UseSetting("InstanceName", instanceName);
        }
    }
}
