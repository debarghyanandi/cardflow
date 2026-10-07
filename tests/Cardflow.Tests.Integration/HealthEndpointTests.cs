using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Containers;

namespace Cardflow.Tests.Integration;

// xUnit runs tests in this class sequentially because they share the containers.
public sealed class HealthEndpointTests(HealthApiFixture fixture)
    : IClassFixture<HealthApiFixture>
{
    [Fact]
    public async Task FullHealth_ReturnsHealthyDependenciesAndConfiguredInstance()
    {
        var (statusCode, report) = await ReadReportAsync("/health");

        Assert.Equal(HttpStatusCode.OK, statusCode);
        AssertHealthyDependencies(report);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("redis")]
    public async Task FullHealth_ReportsUnavailableDependencyAndRecovers(string dependency)
    {
        await WaitForHealthyAsync();

        IContainer container = dependency == "postgres" ? fixture.Postgres : fixture.Redis;
        string otherDependency = dependency == "postgres" ? "redis" : "postgres";

        await container.PauseAsync();
        try
        {
            var (statusCode, report) = await ReadReportAsync("/health");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, statusCode);
            Assert.Equal("Unhealthy", report.Status);
            Assert.Equal(HealthApiFixture.InstanceName, report.Instance);
            Assert.Equal(2, report.Checks.Count);
            Assert.Equal("Unhealthy", report.Checks[dependency]);
            Assert.Equal("Healthy", report.Checks[otherDependency]);
        }
        finally
        {
            await container.UnpauseAsync();
        }

        AssertHealthyDependencies(await WaitForHealthyAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Liveness_ReturnsHealthyWithoutDependencyChecks(bool dependenciesUnavailable)
    {
        await WaitForHealthyAsync();
        var pausedContainers = new List<IContainer>();

        try
        {
            if (dependenciesUnavailable)
            {
                foreach (IContainer container in new IContainer[] { fixture.Postgres, fixture.Redis })
                {
                    await container.PauseAsync();
                    pausedContainers.Add(container);
                }
            }

            // A dependency probe would time out after five seconds. Liveness must skip it.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var (statusCode, report) = await ReadReportAsync("/health/live", deadline.Token);

            Assert.Equal(HttpStatusCode.OK, statusCode);
            Assert.Equal("Healthy", report.Status);
            Assert.Equal(HealthApiFixture.InstanceName, report.Instance);
            Assert.Empty(report.Checks);
        }
        finally
        {
            foreach (IContainer container in pausedContainers)
            {
                await container.UnpauseAsync();
            }
        }

        if (dependenciesUnavailable)
        {
            AssertHealthyDependencies(await WaitForHealthyAsync());
        }
    }

    private async Task<(HttpStatusCode StatusCode, HealthResponse Report)> ReadReportAsync(
        string path, CancellationToken cancellationToken = default)
    {
        using var response = await fixture.Client.GetAsync(path, cancellationToken);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var report = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);
        Assert.NotNull(report);
        Assert.NotNull(report.Checks);

        return (response.StatusCode, report);
    }

    private async Task<HealthResponse> WaitForHealthyAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            var (statusCode, report) = await ReadReportAsync("/health", deadline.Token);
            if (statusCode == HttpStatusCode.OK && report.Status == "Healthy")
            {
                return report;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
        }

        Assert.Fail("The dependencies did not recover within 30 seconds.");
        throw new InvalidOperationException("Unreachable after assertion failure.");
    }

    private static void AssertHealthyDependencies(HealthResponse report)
    {
        Assert.Equal("Healthy", report.Status);
        Assert.Equal(HealthApiFixture.InstanceName, report.Instance);
        Assert.Equal(2, report.Checks.Count);
        Assert.Equal("Healthy", report.Checks["postgres"]);
        Assert.Equal("Healthy", report.Checks["redis"]);
    }

    private sealed record HealthResponse(
        string Status, string Instance, Dictionary<string, string> Checks);
}
