using Microsoft.Extensions.DependencyInjection;
namespace Cardflow.Infrastructure
{
    public static class ServiceCollection
    {
        public static IServiceCollection AddInfrastructureHealthChecks(
            this IServiceCollection services,
            string postgresConnection,
            string redisConnection)
        {
            services.AddHealthChecks()
                .AddNpgSql(
                    postgresConnection,
                    name: "postgres",
                    timeout: TimeSpan.FromSeconds(5))
                .AddRedis(
                    redisConnection,
                    name: "redis",
                    timeout: TimeSpan.FromSeconds(5));

            return services;
        }
    }
}
