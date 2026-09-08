using Atlas.Api.Auth;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ApiUserEndpoints
{
    public static IEndpointRouteBuilder MapApiUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/api-users").RequireAuthorization(AuthPolicies.PlatformAdmin);

        group.MapGet("/", async (int? page, int? pageSize, HttpContext http, AtlasDbContext db) =>
        {
            if (Pagination.Validate(page, pageSize, out var paging) is { } problem)
            {
                return problem;
            }

            var users = await db.ApiUsers.OrderBy(u => u.CreatedAtUtc).ToPageAsync(http, paging);
            return Results.Ok(users.Select(ToResponse).ToList());
        });

        group.MapPost("/", async (CreateApiUserRequest request, AtlasDbContext db, TimeProvider clock) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                errors["name"] = ["Name is required."];
            }
            ApiRole role = default;
            if (string.IsNullOrWhiteSpace(request.Role)
                || !ApiInput.DefinedEnum(request.Role, out role))
            {
                errors["role"] = [$"Role must be one of: {string.Join(", ", Enum.GetNames<ApiRole>())}."];
            }
            else if (ApiUser.RoleRequiresClient(role) && (request.ClientId is null || request.ClientId == Guid.Empty))
            {
                errors["clientId"] = [$"ClientId is required for the {role} role."];
            }
            else if (!ApiUser.RoleRequiresClient(role) && request.ClientId is not null)
            {
                errors["clientId"] = ["ClientId must not be set for platform admins."];
            }
            var now = clock.GetUtcNow();
            if (request.ExpiresAtUtc is { } expiry && (expiry <= now || expiry > now.AddDays(365)))
                errors["expiresAtUtc"] = ["Expiration must be in the future, no more than 365 days away."];
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            if (request.ClientId is not null && await db.Clients.FindAsync(request.ClientId) is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["clientId"] = [$"Client '{request.ClientId}' does not exist."],
                });
            }

            var user = new ApiUser
            {
                Name = request.Name!.Trim(),
                ApiKey = $"atlas_{Guid.NewGuid():N}",
                Role = role,
                ClientId = request.ClientId,
                ExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime(),
                CreatedAtUtc = now,
            };
            db.ApiUsers.Add(user);
            await db.SaveChangesAsync();

            // The full key is returned exactly once, on creation.
            return Results.Created($"/api/api-users/{user.Id}", new ApiUserCreatedResponse(
                user.Id, user.Name, user.Role.ToString(), user.ClientId, user.IsActive, user.CreatedAtUtc, user.ApiKey,
                user.ExpiresAtUtc, user.ReplacesApiUserId, user.Version));
        });

        group.MapPost("/{id:guid}/deactivate", async (Guid id, AtlasDbContext db) =>
        {
            var user = await db.ApiUsers.FindAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            user.IsActive = false;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(user));
        });

        group.MapGet("/{id:guid}", async (Guid id, AtlasDbContext db) =>
        {
            var user = await db.ApiUsers.FindAsync(id);
            return user is null ? Results.NotFound() : Results.Ok(ToResponse(user));
        });
        group.MapPost("/{id:guid}/replace", async (Guid id, AtlasDbContext db, TimeProvider clock) =>
        {
            var old = await db.ApiUsers.FindAsync(id);
            if (old is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            if (!old.IsActive || old.ExpiresAtUtc <= now || await db.ApiUsers.AnyAsync(x => x.ReplacesApiUserId == id))
                return Results.Problem(statusCode: 409, detail: "Only an active, unexpired key can be replaced once.");
            var replacement = new ApiUser { Name = old.Name, Role = old.Role, ClientId = old.ClientId,
                ApiKey = $"atlas_{Guid.NewGuid():N}", ExpiresAtUtc = old.ExpiresAtUtc, ReplacesApiUserId = old.Id, CreatedAtUtc = now };
            old.IsActive = false;
            db.ApiUsers.Add(replacement);
            await db.SaveChangesAsync();
            return Results.Created($"/api/api-users/{replacement.Id}", new ApiUserCreatedResponse(replacement.Id, replacement.Name,
                replacement.Role.ToString(), replacement.ClientId, true, now, replacement.ApiKey,
                replacement.ExpiresAtUtc, old.Id, replacement.Version));
        });
        return app;
    }

    private static ApiUserResponse ToResponse(ApiUser u) =>
        new(u.Id, u.Name, u.Role.ToString(), u.ClientId, u.IsActive, u.CreatedAtUtc, MaskKey(u.ApiKey), u.ExpiresAtUtc, u.ReplacesApiUserId, u.Version);

    /// <summary>Keys are only listed masked; the full secret is shown once on creation.</summary>
    private static string MaskKey(string key) =>
        key.Length <= 4 ? "****" : $"****{key[^4..]}";
}

public record CreateApiUserRequest(string? Name, string? Role, Guid? ClientId, DateTimeOffset? ExpiresAtUtc = null);

public record ApiUserResponse(
    Guid Id,
    string Name,
    string Role,
    Guid? ClientId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    string ApiKeyMasked,
    DateTimeOffset? ExpiresAtUtc = null,
    Guid? ReplacesApiUserId = null,
    int Version = 1);

public record ApiUserCreatedResponse(
    Guid Id,
    string Name,
    string Role,
    Guid? ClientId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    string ApiKey,
    DateTimeOffset? ExpiresAtUtc = null,
    Guid? ReplacesApiUserId = null,
    int Version = 1);
