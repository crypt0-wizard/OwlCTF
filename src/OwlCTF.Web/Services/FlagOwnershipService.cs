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
        return new(FlagOwnershipDisposition.OwnedByAnotherTeam, owner.TeamId, owner.ChallengeInstanceId, owner.ChallengeId, hash);
    }

    public async Task<bool> ReportCrossTeamMatchAsync(
        FlagOwnershipResult result,
        Guid submittingTeamId,
        Guid submittingUserId,
        Guid submittedChallengeId,
        long submissionAttemptId,
        CancellationToken ct)
    {
        if (result.Disposition != FlagOwnershipDisposition.OwnedByAnotherTeam
            || result.OwningTeamId is not Guid owningTeamId
            || result.ChallengeInstanceId is not Guid challengeInstanceId
            || result.OwningChallengeId is not Guid owningChallengeId
            || string.IsNullOrWhiteSpace(result.FlagHash))
            return false;
        var incident = new CheatIncident
        {
            Id = Guid.NewGuid(), SubmissionAttemptId = submissionAttemptId, SubmittingTeamId = submittingTeamId, OwningTeamId = owningTeamId, SubmittingUserId = submittingUserId,
            SubmittedChallengeId = submittedChallengeId, OwningChallengeId = owningChallengeId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            Evidence = "Submitted flag hash " + result.FlagHash[..16] + "... matched instance " + challengeInstanceId.ToString("N") + " owned by team " + owningTeamId.ToString("N") + "."
        };
        await store.AddIncidentAsync(incident, ct);
        if (await notifier.NotifyAsync(incident, ct)) await store.MarkIncidentNotifiedAsync(incident.Id, ct);
        if (!options.AutoBanOnCheat) return false;
        await store.BanTeamAsync(submittingTeamId, "Automatic action for cross-team instance flag incident " + incident.Id.ToString("N") + ".", ct);
        await store.MarkIncidentAutoBanAsync(incident.Id, ct);
        return true;
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
