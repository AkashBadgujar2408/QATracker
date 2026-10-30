using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Settings;

// ---- SLA (plan 8.4: due date = testing completed + SLA hours of the severity, in working hours) ----

public sealed record SlaPolicyDto(Severity Severity, int WorkingHours, string RowVersion);

public sealed record SlaPolicyUpdate(Severity Severity, int WorkingHours, string RowVersion);

public sealed record UpdateSlaCommand(IReadOnlyList<SlaPolicyUpdate> Policies);

public sealed class UpdateSlaValidator : AbstractValidator<UpdateSlaCommand>
{
    /// <summary>A year of working days – anything longer is a typo.</summary>
    public const int MaxWorkingHours = 300 * WorkingCalendar.HoursPerDay;

    public UpdateSlaValidator()
    {
        RuleFor(x => x.Policies).NotEmpty()
            .Must(p => p.Select(x => x.Severity).Distinct().Count() == p.Count).WithMessage("Each severity may appear only once.");
        RuleForEach(x => x.Policies).ChildRules(policy =>
        {
            policy.RuleFor(p => p.Severity).IsInEnum();
            policy.RuleFor(p => p.WorkingHours).InclusiveBetween(1, MaxWorkingHours)
                .WithMessage($"Working hours must be between 1 and {MaxWorkingHours}.");
            policy.RuleFor(p => p.RowVersion).ValidRowVersion();
        });
    }
}

public sealed class GetSlaHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<SlaPolicyDto>> HandleAsync(CancellationToken cancellationToken) =>
        (await db.SlaPolicies.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(p => p.Severity)
            .Select(p => new SlaPolicyDto(p.Severity, p.WorkingHours, Convert.ToBase64String(p.RowVersion)))
            .ToList();
}

/// <summary>Change the SLA hours. Only due dates set from now on use them – existing due dates stay as they are.</summary>
public sealed class UpdateSlaHandler(IValidator<UpdateSlaCommand> validator, IApplicationDbContext db, GetSlaHandler get)
{
    public async Task<Result<IReadOnlyList<SlaPolicyDto>>> HandleAsync(UpdateSlaCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var policies = await db.SlaPolicies.ToDictionaryAsync(p => p.Severity, cancellationToken);
        foreach (var update in command.Policies)
        {
            if (!policies.TryGetValue(update.Severity, out var policy))
            {
                return Error.NotFound($"The SLA for {update.Severity}");
            }

            db.ExpectRowVersion(policy, update.RowVersion);
            policy.WorkingHours = update.WorkingHours;
        }

        if (await db.TrySaveAsync("The SLA", cancellationToken) is { } failed)
        {
            return failed;
        }

        return (await get.HandleAsync(cancellationToken)).ToList();
    }
}

// ---- Holidays (working days skip Sundays and these dates) ----

public sealed record HolidayDto(DateOnly Date, string Name);

public sealed record CreateHolidayCommand(DateOnly Date, string Name);

public sealed class CreateHolidayValidator : AbstractValidator<CreateHolidayCommand>
{
    public CreateHolidayValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Choose the date.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class ListHolidaysHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<HolidayDto>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Holidays.AsNoTracking().OrderBy(h => h.Date).Select(h => new HolidayDto(h.Date, h.Name)).ToListAsync(cancellationToken);
}

public sealed class CreateHolidayHandler(IValidator<CreateHolidayCommand> validator, IApplicationDbContext db)
{
    public async Task<Result<HolidayDto>> HandleAsync(CreateHolidayCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (await db.Holidays.AnyAsync(h => h.Date == command.Date, cancellationToken))
        {
            return Error.Conflict("holiday_exists", "That date is already a holiday.");
        }

        var holiday = new Holiday { Date = command.Date, Name = command.Name.Trim() };
        db.Holidays.Add(holiday);
        if (await db.TrySaveAsync("That holiday", cancellationToken) is { } failed)
        {
            return failed;
        }

        return new HolidayDto(holiday.Date, holiday.Name);
    }
}

public sealed class DeleteHolidayHandler(IApplicationDbContext db)
{
    public async Task<Result> HandleAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var deleted = await db.Holidays.Where(h => h.Date == date).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? Error.NotFound("Holiday") : Result.Success();
    }
}

// ---- Sprint phases (the picker in "Start a testing sprint" lists the active ones in this order) ----

public sealed record PhaseDto(int Id, string Name, int SortOrder, bool IsActive, int SprintCount, string RowVersion);

public sealed record CreatePhaseCommand(string Name);

public sealed record UpdatePhaseCommand(int Id, string Name, bool IsActive, string RowVersion);

public sealed record PhaseOrderItem(int Id, string RowVersion);

public sealed record ReorderPhasesCommand(IReadOnlyList<PhaseOrderItem> Phases);

public sealed class CreatePhaseValidator : AbstractValidator<CreatePhaseCommand>
{
    public CreatePhaseValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
}

public sealed class UpdatePhaseValidator : AbstractValidator<UpdatePhaseCommand>
{
    public UpdatePhaseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RowVersion).ValidRowVersion();
    }
}

public sealed class ReorderPhasesValidator : AbstractValidator<ReorderPhasesCommand>
{
    public ReorderPhasesValidator()
    {
        RuleFor(x => x.Phases).NotEmpty()
            .Must(p => p.Select(x => x.Id).Distinct().Count() == p.Count).WithMessage("Each phase may appear only once.");
        RuleForEach(x => x.Phases).ChildRules(item => item.RuleFor(p => p.RowVersion).ValidRowVersion());
    }
}

public sealed class ListPhasesHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<PhaseDto>> HandleAsync(CancellationToken cancellationToken) =>
        await db.SprintPhases.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new PhaseDto(p.Id, p.Name, p.SortOrder, p.IsActive, p.TestingSprints.Count, Convert.ToBase64String(p.RowVersion)))
            .ToListAsync(cancellationToken);
}

public sealed class CreatePhaseHandler(IValidator<CreatePhaseCommand> validator, IApplicationDbContext db)
{
    public async Task<Result<PhaseDto>> HandleAsync(CreatePhaseCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var name = command.Name.Trim();
        if (await db.SprintPhases.AnyAsync(p => p.Name == name, cancellationToken))
        {
            return Error.Conflict("phase_name_taken", $"A phase named '{name}' already exists.");
        }

        var last = await db.SprintPhases.MaxAsync(p => (int?)p.SortOrder, cancellationToken) ?? 0;
        var phase = new SprintPhase { Name = name, SortOrder = last + 1, IsActive = true };
        db.SprintPhases.Add(phase);
        if (await db.TrySaveAsync($"A phase named '{name}'", cancellationToken) is { } failed)
        {
            return failed;
        }

        return new PhaseDto(phase.Id, phase.Name, phase.SortOrder, phase.IsActive, 0, Convert.ToBase64String(phase.RowVersion));
    }
}

/// <summary>Rename or (de)activate a phase. Sprints keep their phase; an inactive one is only hidden from new sprints.</summary>
public sealed class UpdatePhaseHandler(IValidator<UpdatePhaseCommand> validator, IApplicationDbContext db)
{
    public async Task<Result<PhaseDto>> HandleAsync(UpdatePhaseCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var phase = await db.SprintPhases.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (phase is null)
        {
            return Error.NotFound("Phase");
        }

        var name = command.Name.Trim();
        if (await db.SprintPhases.AnyAsync(p => p.Id != phase.Id && p.Name == name, cancellationToken))
        {
            return Error.Conflict("phase_name_taken", $"A phase named '{name}' already exists.");
        }

        if (phase.IsActive && !command.IsActive && !await db.SprintPhases.AnyAsync(p => p.Id != phase.Id && p.IsActive, cancellationToken))
        {
            return Error.Rule("last_active_phase", "At least one phase must stay active – sprints are started in a phase.");
        }

        db.ExpectRowVersion(phase, command.RowVersion);
        phase.Name = name;
        phase.IsActive = command.IsActive;
        if (await db.TrySaveAsync("This phase", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.SprintPhases.AsNoTracking().Where(p => p.Id == phase.Id)
            .Select(p => new PhaseDto(p.Id, p.Name, p.SortOrder, p.IsActive, p.TestingSprints.Count, Convert.ToBase64String(p.RowVersion)))
            .SingleAsync(cancellationToken);
    }
}

/// <summary>New order of all phases (every phase exactly once, each with the version the page showed).</summary>
public sealed class ReorderPhasesHandler(IValidator<ReorderPhasesCommand> validator, IApplicationDbContext db, ListPhasesHandler list)
{
    public async Task<Result<IReadOnlyList<PhaseDto>>> HandleAsync(ReorderPhasesCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var phases = await db.SprintPhases.ToDictionaryAsync(p => p.Id, cancellationToken);
        if (phases.Count != command.Phases.Count || command.Phases.Any(p => !phases.ContainsKey(p.Id)))
        {
            return Error.Validation(nameof(command.Phases), "Send every phase exactly once – reload and try again.");
        }

        for (var i = 0; i < command.Phases.Count; i++)
        {
            var phase = phases[command.Phases[i].Id];
            db.ExpectRowVersion(phase, command.Phases[i].RowVersion);
            phase.SortOrder = i + 1;
        }

        if (await db.TrySaveAsync("The phases", cancellationToken) is { } failed)
        {
            return failed;
        }

        return (await list.HandleAsync(cancellationToken)).ToList();
    }
}
