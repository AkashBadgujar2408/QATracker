using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using QaTracker.Application.Features.Auth;

namespace QaTracker.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler (classes named *Handler) and FluentValidation validator.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal));
        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        services.AddScoped<AuthSessions>();
        services.AddScoped<Features.Workflow.WorkflowSupport>();
        services.AddScoped<Features.Attachments.AttachmentWriter>();
        services.AddScoped<Features.Timeline.TimelineReader>();
        services.AddScoped<Features.Import.LegacyImportReader>();
        services.AddScoped<Features.Notifications.NotificationMailer>();
        services.AddScoped<Features.EmailAdmin.EmailSettingsSource>();
        services.AddScoped<Features.Sprints.CarryForwardStep>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
