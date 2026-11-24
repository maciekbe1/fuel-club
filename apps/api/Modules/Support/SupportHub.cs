using System.Security.Claims;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Support;

public sealed record SupportMessageAddedEvent(Guid CaseId, int Seq);

public sealed record SupportCaseChangedEvent(Guid CaseId, string Status);

public sealed record SupportQueueChangedEvent;

/// <summary><c>Author</c>: Customer or Support - who is typing.</summary>
public sealed record SupportTypingEvent(Guid CaseId, string Author);

/// <summary>
/// Not a response body: documents the hub's server-to-client events (event name = property name,
/// camelCase) so the frontend gets generated types (ADR 0014).
/// </summary>
public sealed record SupportHubContract(
    SupportMessageAddedEvent MessageAdded,
    SupportCaseChangedEvent CaseChanged,
    SupportQueueChangedEvent QueueChanged,
    SupportTypingEvent Typing
);

public static class SupportHubOpenApi
{
    /// <summary>
    /// The hub has no OpenAPI operation: its event payloads are added as a schema so the frontend's
    /// generated types cover them (CLAUDE.md: no hand-written DTO types).
    /// </summary>
    public static void AddSupportHubContract(
        this Microsoft.AspNetCore.OpenApi.OpenApiOptions options
    ) => options.AddHubContract<SupportHubContract>();

    /// <summary>Adds a hub's event contract record as a named schema (any module's hub).</summary>
    public static void AddHubContract<TContract>(
        this Microsoft.AspNetCore.OpenApi.OpenApiOptions options
    ) =>
        options.AddDocumentTransformer(
            async (document, context, cancellationToken) =>
            {
                var schema = await context.GetOrCreateSchemaAsync(
                    typeof(TContract),
                    null,
                    cancellationToken
                );
                document.Components ??= new();
                document.Components.Schemas ??=
                    new Dictionary<string, Microsoft.OpenApi.IOpenApiSchema>();
                document.Components.Schemas[typeof(TContract).Name] = schema;
            }
        );
}

/// <summary>
/// Notifications and the ephemeral "typing" signal for support conversations (ADR 0014). Writes
/// nothing: messages are stored by POST only, and their text never passes through here. Recipients
/// are looked up in the database at send time (by user id), so a former assignee drops out at once.
/// </summary>
[Authorize(Roles = $"{RoleNames.User},{RoleNames.Support}")]
public sealed class SupportHub(AppDbContext db) : Hub
{
    public const string Path = "/api/support/hub";
    public const string AgentsGroup = "support-agents";
    private const string LastTypingKey = "last-typing";

    /// <summary>At most one typing signal per second and connection; more are dropped.</summary>
    public static readonly TimeSpan TypingInterval = TimeSpan.FromSeconds(1);

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(RoleNames.Support) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AgentsGroup);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// "I am typing in this case." Forwarded to the other side only while the caller is the owner of
    /// the open case or the assignee of the assigned case; otherwise ignored without an answer. Not
    /// stored, not logged.
    /// </summary>
    public async Task Typing(Guid caseId)
    {
        // Per case: an agent switching between two cases keeps both signals.
        var now = DateTimeOffset.UtcNow;
        var last =
            Context.Items.TryGetValue(LastTypingKey, out var stored)
            && stored is Dictionary<Guid, DateTimeOffset> map
                ? map
                : (Dictionary<Guid, DateTimeOffset>)(
                    Context.Items[LastTypingKey] = new Dictionary<Guid, DateTimeOffset>()
                );
        if (last.TryGetValue(caseId, out var previous) && now - previous < TypingInterval)
            return;

        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return;
        var row = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new
            {
                c.UserId,
                c.Status,
                c.AssignedToUserId,
            })
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (row is not { Status: SupportCaseStatus.Assigned, AssignedToUserId: { } assignee })
            return;

        string? recipient = null,
            author = null;
        if (row.UserId == userId && Context.User!.IsInRole(RoleNames.User))
            (recipient, author) = (assignee, nameof(SupportAuthor.Customer));
        else if (assignee == userId && Context.User!.IsInRole(RoleNames.Support))
            (recipient, author) = (row.UserId, nameof(SupportAuthor.Support));
        if (recipient is null)
            return;
        // Recorded only when forwarded: an ignored signal does not hold back a valid one.
        last[caseId] = now;
        await Clients.User(recipient).SendAsync("typing", new SupportTypingEvent(caseId, author!));
    }
}

/// <summary>
/// Sends hub events after a change committed (callers: endpoints via Response.OnCompleted, the
/// worker after its commit). Recipients and the case status are read when sending, so late or
/// reordered sends never carry a stale status or reach a former assignee. Best effort: a failed send
/// is logged (error type only) and the client catches up through the cursor.
/// </summary>
public sealed class SupportNotifier(
    IHubContext<SupportHub> hub,
    IServiceScopeFactory scopes,
    ILogger<SupportNotifier> logger
)
{
    /// <summary>
    /// Runs the send once the response is complete - after the endpoint's transaction committed (the
    /// idempotency filter commits before the response is written). An error response means the
    /// change was rolled back or never made: nothing is sent.
    /// </summary>
    public static void AfterResponse(HttpContext http, Func<SupportNotifier, Task> send)
    {
        var notifier = http.RequestServices.GetRequiredService<SupportNotifier>();
        http.Response.OnCompleted(() =>
            http.Response.StatusCode < StatusCodes.Status400BadRequest
                ? send(notifier)
                : Task.CompletedTask
        );
    }

    public Task MessageAddedAsync(Guid caseId, int seq) =>
        SafeAsync(async () =>
        {
            if (await CaseAsync(caseId) is not { } current)
                return;
            foreach (var user in current.Parties)
                await hub
                    .Clients.User(user)
                    .SendAsync("messageAdded", new SupportMessageAddedEvent(caseId, seq));
        });

    /// <param name="formerAssignee">The agent who just lost the case (release, idle return), if any.</param>
    /// <param name="queue">Also tell the agents to refresh the queue (one batch sends it once).</param>
    public Task CaseChangedAsync(Guid caseId, string? formerAssignee = null, bool queue = true) =>
        SafeAsync(async () =>
        {
            if (await CaseAsync(caseId) is { } current)
            {
                var users = new HashSet<string>(current.Parties);
                if (formerAssignee is not null && formerAssignee != current.Assignee)
                    users.Add(formerAssignee);
                foreach (var user in users)
                    await hub
                        .Clients.User(user)
                        .SendAsync(
                            "caseChanged",
                            new SupportCaseChangedEvent(caseId, current.Status.ToString())
                        );
            }
            if (queue)
                await QueueAsync();
        });

    /// <summary>After a housekeeping run committed: one event per case, one queue refresh.</summary>
    public async Task NotifyAsync(HousekeepingResult result)
    {
        foreach (var id in result.ExpiredIds)
            await CaseChangedAsync(id, queue: false);
        foreach (var returned in result.ReturnedCases)
            await CaseChangedAsync(returned.Id, returned.FormerAssignee, queue: false);
        if (result.ExpiredIds.Count + result.ReturnedCases.Count > 0)
            await QueueChangedAsync();
    }

    public Task QueueChangedAsync() => SafeAsync(QueueAsync);

    private Task QueueAsync() =>
        hub
            .Clients.Group(SupportHub.AgentsGroup)
            .SendAsync("queueChanged", new SupportQueueChangedEvent());

    private sealed record CurrentCase(
        SupportCaseStatus Status,
        string? Assignee,
        IReadOnlyCollection<string> Parties
    );

    /// <summary>The status, the owner and the current assignee, read now - never kept from earlier.</summary>
    private async Task<CurrentCase?> CaseAsync(Guid caseId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new
            {
                c.UserId,
                c.AssignedToUserId,
                c.Status,
            })
            .SingleOrDefaultAsync();
        if (row is null)
            return null;
        var parties = new HashSet<string> { row.UserId };
        if (row.AssignedToUserId is { } assignee)
            parties.Add(assignee);
        return new CurrentCase(row.Status, row.AssignedToUserId, parties);
    }

    private async Task SafeAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception error)
        {
            logger.LogWarning("Support notification failed: {Error}", error.GetType().Name);
        }
    }
}

/// <summary>
/// The hubs (each module registers a GuardedHub: support, ADR 0014; loyalty, ADR 0021) accept
/// browsers only from the app's own origins (Support:HubAllowedOrigins): a browser sends no CSRF
/// header over a WebSocket, so the Origin check stops a foreign page from using the session cookie.
/// Required on negotiation (POST) and the WebSocket upgrade, where browsers always send Origin.
/// The transport requests after it (SSE and long-polling GETs, which a same-origin browser sends
/// without Origin) need the connection token from that guarded negotiation; a foreign Origin on
/// them is still refused.
/// </summary>
public sealed class HubOriginGuard(
    RequestDelegate next,
    IOptions<SupportOptions> options,
    IEnumerable<GuardedHub> hubs
)
{
    private readonly GuardedHub[] _hubs = [.. hubs];

    public async Task InvokeAsync(HttpContext http)
    {
        var hub = _hubs.FirstOrDefault(h => http.Request.Path.StartsWithSegments(h.Path));
        if (hub is not null)
        {
            var origin = http.Request.Headers.Origin.ToString();
            var required =
                http.Request.Path.StartsWithSegments($"{hub.Path}/negotiate")
                || http.WebSockets.IsWebSocketRequest;
            var allowed = string.IsNullOrEmpty(origin)
                ? !required
                : options.Value.HubAllowedOrigins.Contains(
                    origin,
                    StringComparer.OrdinalIgnoreCase
                );
            if (!allowed)
            {
                await ApiProblems
                    .Problem(StatusCodes.Status403Forbidden, "Origin not allowed", hub.ErrorCode)
                    .ExecuteAsync(http);
                return;
            }
        }
        await next(http);
    }
}
