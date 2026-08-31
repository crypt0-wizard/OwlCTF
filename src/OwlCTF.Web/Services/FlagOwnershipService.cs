using System.Net.Http.Json;
using OwlCTF.Data;
using OwlCTF.Options;
using Microsoft.Extensions.Options;

namespace OwlCTF.Services;

public sealed class FlagOwnershipService(IFlagOwnershipStore store, FlagHasher hasher, ICheatIncidentNotifier notifier,
    IOptions<DynamicInstanceOptions> configured, TimeProvider clock)
{
    private readonly DynamicInstanceOptions options = configured.Value;
    public async Task<FlagOwnershipResult> CheckAsync(string submittedFlag, Guid submittingTeamId, Guid submittingUserId, Guid submittedChallengeId, CancellationToken ct)
    {
        var hash = hasher.Hash(submittedFlag);
        var owner = await store.FindIssuedFlagAsync(hash, ct);
        if (owner is null) return new(FlagOwnershipDisposition.NotInstanceFlag);
        if (owner.TeamId == submittingTeamId)
            return new(owner.ChallengeId == submittedChallengeId ? FlagOwnershipDisposition.OwnedBySubmittingTeam : FlagOwnershipDisposition.WrongChallenge, owner.TeamId);
        var incident = new CheatIncident
        {
            Id = Guid.NewGuid(), SubmittingTeamId = submittingTeamId, OwningTeamId = owner.TeamId, SubmittingUserId = submittingUserId,
            SubmittedChallengeId = submittedChallengeId, OwningChallengeId = owner.ChallengeId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            Evidence = "Submitted flag hash " + hash[..16] + "... matched instance " + owner.ChallengeInstanceId.ToString("N") + " owned by team " + owner.TeamId.ToString("N") + "."
        };
        await store.AddIncidentAsync(incident, ct);
        if (await notifier.NotifyAsync(incident, ct)) await store.MarkIncidentNotifiedAsync(incident.Id, ct);
        if (options.AutoBanOnCheat)
        {
            await store.BanTeamAsync(submittingTeamId, "Automatic action for cross-team instance flag incident " + incident.Id.ToString("N") + ".", ct);
            await store.MarkIncidentAutoBanAsync(incident.Id, ct);
        }
        return new(FlagOwnershipDisposition.OwnedByAnotherTeam, owner.TeamId);
    }
}

public sealed class WebhookCheatIncidentNotifier(IHttpClientFactory clients, IOptions<DynamicInstanceOptions> configured, ILogger<WebhookCheatIncidentNotifier> logger) : ICheatIncidentNotifier
{
    public async Task<bool> NotifyAsync(CheatIncident incident, CancellationToken ct)
    {
        if (!Uri.TryCreate(configured.Value.AdminWebhookUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps) return false;
        try
        {
            using var response = await clients.CreateClient(nameof(WebhookCheatIncidentNotifier)).PostAsJsonAsync(endpoint,
                new { type = "cross_team_instance_flag", incidentId = incident.Id, message = "Review this anti-cheat incident in the OwlCTF administration database." }, ct);
            if (response.IsSuccessStatusCode) return true;
            logger.LogWarning("Cheat webhook returned HTTP {StatusCode} for incident {IncidentId}", response.StatusCode, incident.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Cheat webhook failed for incident {IncidentId}", incident.Id); }
        return false;
    }
}
