using System.Linq.Expressions;
using FluentValidation;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.Clients;

public sealed record ClientDto(
    int Id,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    bool IsActive,
    int ProjectCount,
    string RowVersion)
{
    public static readonly Expression<Func<Client, ClientDto>> Projection = c => new ClientDto(
        c.Id,
        c.Name,
        c.ContactPerson,
        c.Email,
        c.Phone,
        c.IsActive,
        c.Projects.Count,
        Convert.ToBase64String(c.RowVersion));
}

/// <summary>Fields shared by create and update (quick-add only needs <see cref="Name"/>).</summary>
public interface IClientFields
{
    string Name { get; }
    string? ContactPerson { get; }
    string? Email { get; }
    string? Phone { get; }
}

internal static class ClientRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IClientFields
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        validator.RuleFor(x => x.ContactPerson).MaximumLength(100);
        validator.RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        validator.RuleFor(x => x.Phone).MaximumLength(30);
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
