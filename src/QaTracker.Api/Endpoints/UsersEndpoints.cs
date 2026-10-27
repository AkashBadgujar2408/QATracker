using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Features.Users;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.Endpoints;

public sealed record UpdateUserRequest(string FullName, string Email, UserRole Role, string? AvatarColor, string RowVersion);

public sealed record SetActiveRequest(bool IsActive);

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/", async (string? search, UserRole? role, bool? isActive, ListUsersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new ListUsersQuery(search, role, isActive), ct)))
            .WithSummary("List users (filter by text, role, active).");

        group.MapPost("/", async (CreateUserCommand command, CreateUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToHttp(created =>
                    TypedResults.Created($"/api/users/{created.User.Id}", created)))
            .WithSummary("Create a user with a temporary password (returned once).");

        group.MapPut("/{id:int}", async (int id, UpdateUserRequest request, UpdateUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(
                    new UpdateUserCommand(id, request.FullName, request.Email, request.Role, request.AvatarColor, request.RowVersion),
                    ct)).ToHttp(user => TypedResults.Ok(user)))
            .WithSummary("Edit name, e-mail, role, avatar colour (409 if changed meanwhile).");

        group.MapPost("/{id:int}/reset-password", async (int id, ResetPasswordHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ResetPasswordCommand(id), ct)).ToHttp(result => TypedResults.Ok(result)))
            .WithSummary("Issue a new temporary password (returned once) and end the user's sessions.");

        group.MapPatch("/{id:int}/active", async (int id, SetActiveRequest request, SetUserActiveHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetUserActiveCommand(id, request.IsActive), ct)).ToHttp(user => TypedResults.Ok(user)))
            .WithSummary("Activate or deactivate a user.");

        return app;
    }
}
