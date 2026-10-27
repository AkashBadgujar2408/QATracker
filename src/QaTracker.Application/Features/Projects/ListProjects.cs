using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Projects;

public sealed record ListProjectsQuery(int ClientId, bool IncludeInactive);

public sealed class ListProjectsHandler(IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<ProjectDto>>> HandleAsync(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == query.ClientId, cancellationToken))
        {
            return Error.NotFound("Client");
        }

        var projects = db.Projects.AsNoTracking().Where(p => p.ClientId == query.ClientId);
        if (!query.IncludeInactive)
        {
            projects = projects.Where(p => p.IsActive);
        }

        return await projects.OrderBy(p => p.Name).Select(ProjectDto.Projection).ToListAsync(cancellationToken);
    }
}

public sealed class GetProjectHandler(IApplicationDbContext db)
{
    public async Task<Result<ProjectDto>> HandleAsync(int id, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(ProjectDto.Projection).SingleOrDefaultAsync(cancellationToken);
        return project is null ? Error.NotFound("Project") : project;
    }
}
