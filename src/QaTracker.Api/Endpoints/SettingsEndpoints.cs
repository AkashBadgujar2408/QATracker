using System.Globalization;
using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Settings;

namespace QaTracker.Api.Endpoints;

public sealed record UpdatePhaseRequest(string Name, bool IsActive, string RowVersion);

/// <summary>Admin settings (plan 13: SLA hours, holidays, sprint phases – ADR-038). SMTP settings are under /api/admin/email.</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Admin – settings").RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/sla", async (GetSlaHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("Working hours per severity used for due dates.");

        group.MapPut("/sla", async (UpdateSlaCommand command, UpdateSlaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(policies => TypedResults.Ok(policies)))
            .WithSummary("Change the working hours (each policy with its row version). Only due dates set from now on change.");

        group.MapGet("/holidays", async (ListHolidaysHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("Holidays (not working days for due dates and working-time metrics), by date.");

        group.MapPost("/holidays", async (CreateHolidayCommand command, CreateHolidayHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(holiday =>
                    TypedResults.Created($"/api/settings/holidays/{holiday.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", holiday)))
            .WithSummary("Add a holiday (409 holiday_exists when the date is one already).");

        group.MapDelete("/holidays/{date}", async (DateOnly date, DeleteHolidayHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(date, ct)).ToHttp(() => TypedResults.NoContent()))
            .WithSummary("Remove a holiday (date as yyyy-MM-dd).");

        group.MapGet("/phases", async (ListPhasesHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("All sprint phases in order, with the number of sprints in each.");

        group.MapPost("/phases", async (CreatePhaseCommand command, CreatePhaseHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(phase => TypedResults.Created($"/api/settings/phases/{phase.Id}", phase)))
            .WithSummary("Add a phase at the end of the list.");

        group.MapPut("/phases/{id:int}", async (int id, UpdatePhaseRequest request, UpdatePhaseHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdatePhaseCommand(id, request.Name, request.IsActive, request.RowVersion), ct))
                    .ToHttp(phase => TypedResults.Ok(phase)))
            .WithSummary("Rename or (de)activate a phase (422 last_active_phase).");

        group.MapPut("/phases/order", async (ReorderPhasesCommand command, ReorderPhasesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(phases => TypedResults.Ok(phases)))
            .WithSummary("New order of all phases.");

        return app;
    }
}
