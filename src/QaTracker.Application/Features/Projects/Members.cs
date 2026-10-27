using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Projects;

/// <summary>A person who usually works on the project (notified when testing completes – plan 8.3).</summary>
public sealed record MemberDto(int UserId, string UserName, string FullName, UserRole Role, string? AvatarColor, bool IsActive);

public sealed class ListMembersHandler(IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(int projectId, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        return await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.User.FullName)
            .Select(m => new MemberDto(m.UserId, m.User.UserName, m.User.FullName, m.User.Role, m.User.AvatarColor, m.User.IsActive))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Replace the member list of a project with exactly these users.</summary>
public sealed record SetMembersCommand(int ProjectId, IReadOnlyList<int> UserIds);

public sealed class SetMembersValidator : AbstractValidator<SetMembersCommand>
{
    public SetMembersValidator()
    {
        RuleFor(x => x.UserIds).NotNull().Must(ids => ids.Count <= 200).WithMessage("A project can have at most 200 members.");
    }
}

public sealed class SetMembersHandler(
    IValidator<SetMembersCommand> validator,
    IApplicationDbContext db,
    IClock clock,
    ListMembersHandler list)
{
    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(SetMembersCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (!await db.Projects.AnyAsync(p => p.Id == command.ProjectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        var wanted = command.UserIds.Distinct().ToHashSet();
        var current = await db.ProjectMembers.Where(m => m.ProjectId == command.ProjectId).ToListAsync(cancellationToken);
        var added = wanted.Except(current.Select(m => m.UserId)).ToList();

        if (added.Count > 0)
        {
            var activeUsers = await db.Users.Where(u => added.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
            var unknown = added.Except(activeUsers).ToList();
            if (unknown.Count > 0)
            {
                return Error.Validation(nameof(command.UserIds), $"Unknown or inactive user id(s): {string.Join(", ", unknown)}.");
            }
        }

        db.ProjectMembers.RemoveRange(current.Where(m => !wanted.Contains(m.UserId)));
        var now = clock.UtcNow;
        foreach (var userId in added)
        {
            db.ProjectMembers.Add(new ProjectMember { ProjectId = command.ProjectId, UserId = userId, AddedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await list.HandleAsync(command.ProjectId, cancellationToken);
    }
}
