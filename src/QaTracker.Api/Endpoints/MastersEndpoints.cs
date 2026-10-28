using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Clients;
using QaTracker.Application.Features.Environments;
using QaTracker.Application.Features.Lookups;
using QaTracker.Application.Features.Modules;
using QaTracker.Application.Features.Projects;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.Endpoints;

public sealed record ClientRequest(string Name, string? ContactPerson, string? Email, string? Phone);
public sealed record UpdateClientRequest(string Name, string? ContactPerson, string? Email, string? Phone, string RowVersion);
public sealed record ProjectRequest(string Name, string Key, bool RequireLiveVerification = false);
public sealed record UpdateProjectRequest(string Name, string Key, bool RequireLiveVerification, string RowVersion);
public sealed record ModuleRequest(string Name);
public sealed record UpdateModuleRequest(string Name, string RowVersion);
public sealed record EnvironmentRequest(string Name, EnvironmentType Type, string Url);
public sealed record UpdateEnvironmentRequest(string Name, EnvironmentType Type, string Url, string RowVersion);
public sealed record MembersRequest(IReadOnlyList<int> UserIds);

/// <summary>
/// Masters (plan 14 CLIENTS / PROJECTS / LOOKUPS). Everyone signed in can read (developers view); only testers and
/// admins create, edit and (de)activate. POST returns the created item so pickers can quick-add and select it.
/// </summary>
public static class MastersEndpoints
{
    public static IEndpointRouteBuilder MapMastersEndpoints(this IEndpointRouteBuilder app)
    {
        MapClients(app.MapGroup("/api/clients").WithTags("Clients"));
        MapProjects(app.MapGroup("/api/projects").WithTags("Projects"));
        MapModules(app.MapGroup("/api/modules").WithTags("Modules"));
        MapEnvironments(app.MapGroup("/api/environments").WithTags("Environments"));
        MapLookups(app.MapGroup("/api/lookups").WithTags("Lookups"));
        return app;
    }

    private static void MapClients(RouteGroupBuilder clients)
    {
        clients.MapGet("/", async (string? search, bool? includeInactive, ListClientsHandler handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListClientsQuery(search, includeInactive ?? false), ct)));

        clients.MapGet("/{id:int}", async (int id, GetClientHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, ct)).ToHttp(client => TypedResults.Ok(client)));

        clients.MapPost("/", async (ClientRequest request, CreateClientHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateClientCommand(request.Name, request.ContactPerson, request.Email, request.Phone), ct))
                    .ToHttp(client => TypedResults.Created($"/api/clients/{client.Id}", client)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        clients.MapPut("/{id:int}", async (int id, UpdateClientRequest request, UpdateClientHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(
                    new UpdateClientCommand(id, request.Name, request.ContactPerson, request.Email, request.Phone, request.RowVersion), ct))
                    .ToHttp(client => TypedResults.Ok(client)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        clients.MapPatch("/{id:int}/active", async (int id, SetActiveRequest request, SetClientActiveHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetClientActiveCommand(id, request.IsActive), ct)).ToHttp(client => TypedResults.Ok(client)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        clients.MapGet("/{clientId:int}/projects", async (int clientId, bool? includeInactive, ListProjectsHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListProjectsQuery(clientId, includeInactive ?? false), ct)).ToHttp(projects => TypedResults.Ok(projects)));

        clients.MapPost("/{clientId:int}/projects", async (int clientId, ProjectRequest request, CreateProjectHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateProjectCommand(clientId, request.Name, request.Key, request.RequireLiveVerification), ct))
                    .ToHttp(project => TypedResults.Created($"/api/projects/{project.Id}", project)))
            .RequireAuthorization(Policies.TesterOrAdmin);
    }

    private static void MapProjects(RouteGroupBuilder projects)
    {
        projects.MapGet("/{id:int}", async (int id, GetProjectHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, ct)).ToHttp(project => TypedResults.Ok(project)));

        projects.MapPut("/{id:int}", async (int id, UpdateProjectRequest request, UpdateProjectHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(
                    new UpdateProjectCommand(id, request.Name, request.Key, request.RequireLiveVerification, request.RowVersion), ct))
                    .ToHttp(project => TypedResults.Ok(project)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        projects.MapPatch("/{id:int}/active", async (int id, SetActiveRequest request, SetProjectActiveHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetProjectActiveCommand(id, request.IsActive), ct)).ToHttp(project => TypedResults.Ok(project)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        projects.MapGet("/{id:int}/modules", async (int id, bool? includeInactive, ListModulesHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListModulesQuery(id, includeInactive ?? false), ct)).ToHttp(modules => TypedResults.Ok(modules)));

        projects.MapPost("/{id:int}/modules", async (int id, ModuleRequest request, CreateModuleHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateModuleCommand(id, request.Name), ct))
                    .ToHttp(module => TypedResults.Created($"/api/modules/{module.Id}", module)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        projects.MapGet("/{id:int}/environments", async (int id, bool? includeInactive, ListEnvironmentsHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListEnvironmentsQuery(id, includeInactive ?? false), ct)).ToHttp(environments => TypedResults.Ok(environments)));

        projects.MapPost("/{id:int}/environments", async (int id, EnvironmentRequest request, CreateEnvironmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateEnvironmentCommand(id, request.Name, request.Type, request.Url), ct))
                    .ToHttp(environment => TypedResults.Created($"/api/environments/{environment.Id}", environment)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        projects.MapGet("/{id:int}/members", async (int id, ListMembersHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, ct)).ToHttp(members => TypedResults.Ok(members)));

        projects.MapPut("/{id:int}/members", async (int id, MembersRequest request, SetMembersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetMembersCommand(id, request.UserIds ?? []), ct)).ToHttp(members => TypedResults.Ok(members)))
            .RequireAuthorization(Policies.TesterOrAdmin);
    }

    private static void MapModules(RouteGroupBuilder modules)
    {
        modules.MapPut("/{id:int}", async (int id, UpdateModuleRequest request, UpdateModuleHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateModuleCommand(id, request.Name, request.RowVersion), ct)).ToHttp(module => TypedResults.Ok(module)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        modules.MapPatch("/{id:int}/active", async (int id, SetActiveRequest request, SetModuleActiveHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetModuleActiveCommand(id, request.IsActive), ct)).ToHttp(module => TypedResults.Ok(module)))
            .RequireAuthorization(Policies.TesterOrAdmin);
    }

    private static void MapEnvironments(RouteGroupBuilder environments)
    {
        environments.MapPut("/{id:int}", async (int id, UpdateEnvironmentRequest request, UpdateEnvironmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateEnvironmentCommand(id, request.Name, request.Type, request.Url, request.RowVersion), ct))
                    .ToHttp(environment => TypedResults.Ok(environment)))
            .RequireAuthorization(Policies.TesterOrAdmin);

        environments.MapPatch("/{id:int}/active", async (int id, SetActiveRequest request, SetEnvironmentActiveHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetEnvironmentActiveCommand(id, request.IsActive), ct)).ToHttp(environment => TypedResults.Ok(environment)))
            .RequireAuthorization(Policies.TesterOrAdmin);
    }

    private static void MapLookups(RouteGroupBuilder lookups)
    {
        lookups.MapGet("/cascade", async (int? clientId, int? projectId, CascadeHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new CascadeQuery(clientId, projectId), ct)))
            .WithSummary("Active clients, plus projects of clientId, plus modules/environments/members of projectId.");

        lookups.MapGet("/similar", async (MasterKind kind, string name, int? parentId, SimilarNamesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SimilarNamesQuery(kind, name, parentId), ct)).ToHttp(similar => TypedResults.Ok(similar)))
            .WithSummary("Existing names that look like `name` (duplicate warning for quick-add).");

        lookups.MapGet("/project-key", async (int clientId, string name, SuggestProjectKeyHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ProjectKeySuggestionQuery(clientId, name), ct)).ToHttp(suggestion => TypedResults.Ok(suggestion)))
            .WithSummary("A free project key for a new project of this client.");

        lookups.MapGet("/users", async (UserRole? role, UserLookupHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(role, ct)))
            .WithSummary("Active users (optionally of one role) for member and assignee pickers.");
    }
}
