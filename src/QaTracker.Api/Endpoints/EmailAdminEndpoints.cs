using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.EmailAdmin;

namespace QaTracker.Api.Endpoints;

/// <summary>Admin e-mail screen (ADR-029): check the SMTP settings, watch the outbox, send a test mail, run the summary.</summary>
public static class EmailAdminEndpoints
{
    public static IEndpointRouteBuilder MapEmailAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/email").WithTags("Admin – e-mail").RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/", async (EmailStatusHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("Effective SMTP settings (password only as set / not set), outbox counts, recent problems and the daily summary's last run.");

        group.MapPost("/test", async (SendTestEmailCommand command, SendTestEmailHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(result => TypedResults.Ok(result)))
            .WithSummary("Send a test mail straight through SMTP and return the server's answer (422 smtp_failed with the reason).");

        group.MapPost("/digest", async (RunDigestNowHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttp(result => TypedResults.Ok(result)))
            .WithSummary("Queue the daily summary now.");

        group.MapPost("/outbox/{id:int}/retry", async (int id, RetryEmailHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttp(() => TypedResults.NoContent()))
            .WithSummary("Give an unsent mail a fresh set of attempts.");

        return app;
    }
}
