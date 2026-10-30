using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.EmailAdmin;

/// <summary>The SMTP server and sender mails go out with (ADR-038).</summary>
public sealed record SmtpConfiguration(string Host, int Port, bool EnableSsl, string? UserName, string? Password, string From, string FromName)
{
    public string Server => $"{Host}:{Port}{(EnableSsl ? " (TLS)" : string.Empty)}";
}

/// <summary>
/// Effective mail settings: the row saved on Admin › E-mail, otherwise the <c>Email</c> section of the configuration.
/// Read once per scope (an outbox run or a request), so a change applies to the next mail.
/// </summary>
public sealed class EmailSettingsSource(IApplicationDbContext db, IOptions<EmailOptions> options)
{
    private SmtpConfiguration? _current;

    public async Task<SmtpConfiguration> GetAsync(CancellationToken cancellationToken) =>
        _current ??= await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is { } saved
            ? FromRow(saved)
            : FromOptions(options.Value);

    public static SmtpConfiguration FromRow(EmailSetting row) =>
        new(row.SmtpHost, row.SmtpPort, row.SmtpEnableSsl, row.SmtpUserName, row.SmtpPassword, row.FromAddress, row.FromName);

    public static SmtpConfiguration FromOptions(EmailOptions o) =>
        new(o.Smtp.Host, o.Smtp.Port, o.Smtp.EnableSsl, string.IsNullOrWhiteSpace(o.Smtp.UserName) ? null : o.Smtp.UserName, o.Smtp.Password, o.From, o.FromName);
}

/// <summary>What the settings form shows. The password is never returned – only whether one is set.</summary>
public sealed record MailServerSettingsDto(
    string SmtpHost,
    int SmtpPort,
    bool SmtpEnableSsl,
    string? SmtpUserName,
    bool PasswordSet,
    string FromAddress,
    string FromName,
    bool SavedInApp,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);

/// <summary>
/// Save the mail settings. <c>SmtpPassword</c>: null keeps the current one (also the configured one on the first save),
/// "" removes it, anything else replaces it. No user name means no sign-in, so no password is kept.
/// <c>RowVersion</c> is null for the first save.
/// </summary>
public sealed record UpdateMailServerSettingsCommand(
    string SmtpHost,
    int SmtpPort,
    bool SmtpEnableSsl,
    string? SmtpUserName,
    string? SmtpPassword,
    string FromAddress,
    string FromName,
    string? RowVersion);

public sealed class UpdateMailServerSettingsValidator : AbstractValidator<UpdateMailServerSettingsCommand>
{
    public UpdateMailServerSettingsValidator()
    {
        RuleFor(x => x.SmtpHost).NotEmpty().WithMessage("Enter the SMTP server.").MaximumLength(255)
            .Must(h => h is null || !h.Trim().Any(char.IsWhiteSpace)).WithMessage("The SMTP server name cannot contain spaces.");
        RuleFor(x => x.SmtpPort).InclusiveBetween(1, 65535).WithMessage("The port must be between 1 and 65535.");
        RuleFor(x => x.SmtpUserName).MaximumLength(256);
        RuleFor(x => x.SmtpPassword).MaximumLength(256);
        RuleFor(x => x.FromAddress).NotEmpty().WithMessage("Enter the sender address.").EmailAddress().WithMessage("Enter a valid sender address.").MaximumLength(256);
        RuleFor(x => x.FromName).NotEmpty().WithMessage("Enter the sender name.").MaximumLength(100);
        RuleFor(x => x.RowVersion!).ValidRowVersion().When(x => x.RowVersion is not null);
    }
}

public sealed class GetMailServerSettingsHandler(IApplicationDbContext db, IOptions<EmailOptions> options)
{
    public async Task<MailServerSettingsDto> HandleAsync(CancellationToken cancellationToken)
    {
        var saved = await db.EmailSettings.AsNoTracking()
            .Select(s => new { Row = s, UpdatedBy = s.UpdatedBy.FullName })
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
        {
            var o = EmailSettingsSource.FromOptions(options.Value);
            return new MailServerSettingsDto(o.Host, o.Port, o.EnableSsl, o.UserName, !string.IsNullOrEmpty(o.Password), o.From, o.FromName, false, null, null, null);
        }

        var row = saved.Row;
        return new MailServerSettingsDto(row.SmtpHost, row.SmtpPort, row.SmtpEnableSsl, row.SmtpUserName, !string.IsNullOrEmpty(row.SmtpPassword),
            row.FromAddress, row.FromName, true, row.UpdatedAt, saved.UpdatedBy, Convert.ToBase64String(row.RowVersion));
    }
}

public sealed class UpdateMailServerSettingsHandler(
    IValidator<UpdateMailServerSettingsCommand> validator,
    IApplicationDbContext db,
    IOptions<EmailOptions> options,
    ICurrentUser currentUser,
    IClock clock,
    GetMailServerSettingsHandler get)
{
    public const int SingleRowId = 1;

    public async Task<Result<MailServerSettingsDto>> HandleAsync(UpdateMailServerSettingsCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var row = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            if (command.RowVersion is not null)
            {
                return Error.Concurrency("The e-mail settings");
            }

            row = new EmailSetting { Id = SingleRowId, SmtpPassword = options.Value.Smtp.Password };
            db.EmailSettings.Add(row);
        }
        else
        {
            if (command.RowVersion is null)
            {
                return Error.Concurrency("The e-mail settings");
            }

            db.ExpectRowVersion(row, command.RowVersion);
        }

        var userName = string.IsNullOrWhiteSpace(command.SmtpUserName) ? null : command.SmtpUserName.Trim();
        row.SmtpHost = command.SmtpHost.Trim();
        row.SmtpPort = command.SmtpPort;
        row.SmtpEnableSsl = command.SmtpEnableSsl;
        row.SmtpUserName = userName;
        row.SmtpPassword = userName is null ? null : command.SmtpPassword switch
        {
            null => row.SmtpPassword,
            "" => null,
            var typed => typed,
        };
        row.FromAddress = command.FromAddress.Trim();
        row.FromName = command.FromName.Trim();
        row.UpdatedAt = clock.UtcNow;
        row.UpdatedById = currentUser.UserId;

        if (await db.TrySaveAsync("The e-mail settings", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await get.HandleAsync(cancellationToken);
    }
}

/// <summary>Forget the settings saved in the app: the configuration (appsettings / user secrets) applies again.</summary>
public sealed class ResetMailServerSettingsHandler(IApplicationDbContext db, GetMailServerSettingsHandler get)
{
    public async Task<MailServerSettingsDto> HandleAsync(CancellationToken cancellationToken)
    {
        await db.EmailSettings.ExecuteDeleteAsync(cancellationToken);
        return await get.HandleAsync(cancellationToken);
    }
}
