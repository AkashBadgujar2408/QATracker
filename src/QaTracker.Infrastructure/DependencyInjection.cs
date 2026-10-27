using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Application.Abstractions;
using QaTracker.Infrastructure.Persistence;
using QaTracker.Infrastructure.Persistence.Seeding;
using QaTracker.Infrastructure.Time;

namespace QaTracker.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "QaTracker";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing (appsettings.json).");

        services.AddDbContext<QaTrackerDbContext>(options => options.UseSqlServer(connectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
