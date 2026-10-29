using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Notifications;
using QaTracker.Infrastructure.Email;
using QaTracker.Infrastructure.Files;
using QaTracker.Infrastructure.Persistence;
using QaTracker.Infrastructure.Persistence.Seeding;
using QaTracker.Infrastructure.Text;
using QaTracker.Infrastructure.Time;

namespace QaTracker.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "QaTracker";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing (appsettings.json).");

        // Notifications and live updates are derived from every save (ADR-028).
        services.AddScoped<ChangeFeed>();
        services.AddScoped<ChangeFeedInterceptor>();
        services.TryAddSingleton<IRealtimePublisher, NoRealtimePublisher>();
        services.AddDbContext<QaTrackerDbContext>((sp, options) => options
            .UseSqlServer(connectionString)
            .AddInterceptors(sp.GetRequiredService<ChangeFeedInterceptor>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<QaTrackerDbContext>());
        services.AddScoped<IIssueNumbering, IssueNumbering>();
        services.AddSingleton<IRichTextSanitizer, RichTextSanitizer>();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IThumbnailGenerator, SkiaThumbnailGenerator>();
        services.AddSingleton<IAttachmentUrlSigner, AttachmentUrlSigner>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<DatabaseSeeder>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<EmailOutboxProcessor>();
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        if (email.Enabled && email.Workers)
        {
            services.AddHostedService<EmailOutboxWorker>();
            services.AddHostedService<DailyDigestWorker>();
        }

        return services;
    }
}
