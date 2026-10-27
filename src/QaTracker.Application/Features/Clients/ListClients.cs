using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Clients;

public sealed record ListClientsQuery(string? Search, bool IncludeInactive);

public sealed class ListClientsHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<ClientDto>> HandleAsync(ListClientsQuery query, CancellationToken cancellationToken)
    {
        var clients = db.Clients.AsNoTracking();
        if (!query.IncludeInactive)
        {
            clients = clients.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            clients = clients.Where(c => c.Name.Contains(search));
        }

        return await clients.OrderBy(c => c.Name).Select(ClientDto.Projection).ToListAsync(cancellationToken);
    }
}

public sealed class GetClientHandler(IApplicationDbContext db)
{
    public async Task<Result<ClientDto>> HandleAsync(int id, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == id).Select(ClientDto.Projection).SingleOrDefaultAsync(cancellationToken);
        return client is null ? Error.NotFound("Client") : client;
    }
}
