using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Models;
using OwlCTF.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace OwlCTF.Controllers;

[Authorize(Roles = "Admin"), Route("admin")]
public sealed class AdminController(AppDb db, PlatformService platform, FlagHasher flags, FileStorage storage, ScoreboardService scoreboard, BrandingStorage branding, MarkdownService markdown, ContentImageStorage contentImages, SponsorLogoStorage sponsorLogos, IDbContextFactory<InstanceDbContext> instanceDbFactory, IFirstBloodDiscordClient firstBloodDiscord, IFlagOwnershipStore flagOwnership) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        return View(new AdminDashboardViewModel(settings));
    }

    [HttpGet("users")]
    public async Task<IActionResult> ManageUsers(string? query, string? sort, string? direction, CancellationToken ct)
    {
        var selectedQuery = (query ?? "").Trim();
        if (selectedQuery.Length > 100) selectedQuery = selectedQuery[..100];
        var selectedSort = sort switch { "discord" or "last-login" or "role" => sort, _ => "username" };
        var selectedDirection = direction == "desc" ? "desc" : "asc";
        var descending = selectedDirection == "desc";
        var allUsers = await db.GetUsersAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(selectedQuery)
            ? allUsers
            : allUsers.Where(user =>
                user.Username.Contains(selectedQuery, StringComparison.OrdinalIgnoreCase) ||
                user.DiscordId.Contains(selectedQuery, StringComparison.OrdinalIgnoreCase)).ToArray();

        IOrderedEnumerable<AdminUserRecord> ordered = selectedSort switch
        {
            "discord" => descending ? filtered.OrderByDescending(user => user.DiscordId, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(user => user.DiscordId, StringComparer.OrdinalIgnoreCase),
            "last-login" => descending ? filtered.OrderByDescending(user => user.LastLoginAtUtc) : filtered.OrderBy(user => user.LastLoginAtUtc),
            "role" => descending ? filtered.OrderByDescending(user => user.IsAdmin) : filtered.OrderBy(user => user.IsAdmin),
            _ => descending ? filtered.OrderByDescending(user => user.Username, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
        };

        return View("Users", new AdminUsersViewModel(
            ordered.ThenBy(user => user.Username, StringComparer.OrdinalIgnoreCase).ToArray(),
            selectedQuery,
            selectedSort,
            selectedDirection,
            allUsers.Count,
            allUsers.Count(user => user.IsAdmin)));
    }

    [HttpGet("submissions")]
    public async Task<IActionResult> SubmissionLogs(string? query, string? result, string? direction, int page = 1, CancellationToken ct = default)
    {
        var selectedQuery = (query ?? "").Trim();
        if (selectedQuery.Length > 200) selectedQuery = selectedQuery[..200];
        var selectedResult = result is "correct" or "incorrect" ? result : "all";
        var selectedDirection = direction == "asc" ? "asc" : "desc";
        var selectedPage = Math.Max(1, page);
        bool? correctFilter = selectedResult switch
        {
            "correct" => true,
            "incorrect" => false,
            _ => null
        };
        const int pageSize = 50;
        var logs = await db.GetSubmissionLogsAsync(
            selectedQuery, correctFilter, selectedDirection == "desc", selectedPage, pageSize, ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(logs.MatchCount / (double)pageSize));
        if (selectedPage > totalPages)
        {
            selectedPage = totalPages;
            logs = await db.GetSubmissionLogsAsync(
                selectedQuery, correctFilter, selectedDirection == "desc", selectedPage, pageSize, ct);
        }

        return View("SubmissionLogs", new AdminSubmissionLogsViewModel(
            logs.Attempts, logs.Summary, selectedQuery, selectedResult, selectedDirection,
            selectedPage, pageSize, logs.MatchCount));
    }

    [HttpPost("submissions/incidents/{incidentId:guid}/ban")]
    public async Task<IActionResult> BanTeamFromIncident(Guid incidentId, string? query, string? result, string? direction, int page = 1, CancellationToken ct = default)
    {
        var incident = await flagOwnership.GetIncidentAsync(incidentId, ct);
        if (incident is null)
            TempData["Error"] = "Anti-cheat incident was not found.";
        else if (incident.AutoBanApplied || incident.ManualBanAtUtc is not null)
            TempData["Message"] = "This incident has already been handled.";
        else
        {
            await flagOwnership.BanTeamAsync(incident.SubmittingTeamId, "Manual ban for cross-team instance flag incident " + incident.Id.ToString("N") + ".", ct);
            await flagOwnership.MarkIncidentManualBanAsync(incident.Id, User.UserId(), ct);
            scoreboard.Invalidate();
            TempData["Message"] = "Team banned and incident marked as handled.";
        }
        return RedirectToAction(nameof(SubmissionLogs), new { query, result, direction, page });
    }

    [HttpGet("home")]
    public async Task<IActionResult> EditHomePage(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        ViewBag.SponsorLogos = sponsorLogos.List();
        return View("HomePageForm", new SettingsInput
        {
            PlatformName = settings.PlatformName,
            AboutDescription = settings.AboutDescription,
            InstructionsDescription = settings.InstructionsDescription,
            ContactDescription = settings.ContactDescription
        });
    }

    [HttpPost("home")]
    public async Task<IActionResult> SaveHomePage(SettingsInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.SponsorLogos = sponsorLogos.List();
            return View("HomePageForm", input);
        }
        await platform.UpdateHomePageAsync(
            input.PlatformName.Trim(),
            (input.AboutDescription ?? "").Trim(),
            (input.InstructionsDescription ?? "").Trim(),
            (input.ContactDescription ?? "").Trim(), ct);
        TempData["Message"] = "Home page updated.";
        return RedirectToAction(nameof(EditHomePage));
    }

    [HttpPost("markdown/preview")]
    public IActionResult PreviewMarkdown([FromForm] string? markdownText) => Json(new { html = markdown.Render(markdownText) });

    [HttpPost("content-images")]
    [RequestSizeLimit(5_300_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_300_000)]
    public async Task<IActionResult> UploadContentImage(IFormFile? image, CancellationToken ct)
    {
        if (image is null) return BadRequest(new { error = "Choose an image first." });
        try
        {
            var saved = await contentImages.SaveAsync(image, ct);
            return Json(new { path = saved.PublicPath, fileName = saved.FileName });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("content-images/{fileName}/delete")]
    public IActionResult DeleteContentImage(string fileName)
    {
        if (contentImages.Delete(fileName))
            TempData["Message"] = "Content image removed.";
        else
            TempData["Error"] = "Content image was not found.";
        return RedirectToAction(nameof(EditHomePage));
    }

    [HttpPost("sponsors/{slot:int}/image")]
    [RequestSizeLimit(3_200_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3_200_000)]
    public async Task<IActionResult> UploadSponsorLogo(int slot, IFormFile? logo, CancellationToken ct)
    {
        if (slot is < 1 or > SponsorLogoStorage.SlotCount) return NotFound();
        if (logo is null)
        {
            TempData["Error"] = "Choose a sponsor image first.";
            return RedirectToAction(nameof(EditHomePage), null, null, "sponsor-logos");
        }
        try
        {
            await sponsorLogos.SaveAsync(slot, logo, ct);
            TempData["Message"] = $"Sponsor image {slot} updated.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(EditHomePage), null, null, "sponsor-logos");
    }

    [HttpPost("sponsors/{slot:int}/image/remove")]
    public IActionResult RemoveSponsorLogo(int slot)
    {
        if (slot is < 1 or > SponsorLogoStorage.SlotCount) return NotFound();
        var removed = sponsorLogos.Delete(slot);
        TempData[removed ? "Message" : "Error"] = removed ? "Sponsor image removed." : "Sponsor image was not found.";
        return RedirectToAction(nameof(EditHomePage), null, null, "sponsor-logos");
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> EditEventSchedule(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        return View("EventScheduleForm", new EventScheduleInput
        {
            StartsAtUtc = settings.StartsAtUtc,
            EndsAtUtc = settings.EndsAtUtc
        });
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> SaveEventSchedule(EventScheduleInput input, CancellationToken ct)
    {
        var start = AsUtc(input.StartsAtUtc);
        var end = AsUtc(input.EndsAtUtc);
        if (start is not null && end is not null && end <= start)
            ModelState.AddModelError(nameof(input.EndsAtUtc), "End must be after start.");
        if (!ModelState.IsValid) return View("EventScheduleForm", input);

        await platform.UpdateEventScheduleAsync(start, end, ct);
        scoreboard.Invalidate();
        TempData["Message"] = "Event schedule updated.";
        return RedirectToAction(nameof(EditEventSchedule));
    }

    [HttpPost("first-blood")]
    public async Task<IActionResult> SaveFirstBloodSettings(FirstBloodSettingsInput input, CancellationToken ct)
    {
        var current = await platform.GetAsync(ct);
        string? webhookUrl;
        if (input.RemoveWebhook) webhookUrl = null;
        else if (string.IsNullOrWhiteSpace(input.WebhookUrl)) webhookUrl = current.FirstBloodWebhookUrl;
        else if (!DiscordWebhookAddress.TryNormalize(input.WebhookUrl, out webhookUrl))
        {
            TempData["Error"] = "Enter a valid Discord channel webhook URL.";
            return RedirectToAction(nameof(Index));
        }

        if (input.Enabled && string.IsNullOrWhiteSpace(webhookUrl))
        {
            TempData["Error"] = "Add a Discord webhook before enabling first-blood announcements.";
            return RedirectToAction(nameof(Index));
        }

        await platform.UpdateFirstBloodSettingsAsync(input.Enabled && webhookUrl is not null, webhookUrl, ct);
        TempData["Message"] = webhookUrl is null ? "First-blood announcements disabled and webhook removed." : "First-blood Discord settings updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("flag-format")]
    public async Task<IActionResult> SaveFlagFormat(FlagFormatInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
                ?? "Enter a valid dynamic flag prefix.";
            return RedirectToAction(nameof(Index), null, null, "flag-format");
        }

        var prefix = FlagPrefixPolicy.Normalize(input.FlagPrefix);
        await platform.UpdateFlagPrefixAsync(prefix, ct);
        TempData["Message"] = $"Dynamic instance flags will now use {prefix}{{...}}.";
        return RedirectToAction(nameof(Index), null, null, "flag-format");
    }

    [HttpPost("team-capacity")]
    public async Task<IActionResult> SaveTeamCapacity(TeamCapacityInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = $"Choose a team limit from {TeamCapacityPolicy.MinimumMembers} to {TeamCapacityPolicy.MaximumMembers}.";
            return RedirectToAction(nameof(Index), null, null, "team-capacity");
        }

        await platform.UpdateTeamCapacityAsync(input.MaxTeamMembers, ct);
        TempData["Message"] = $"Teams can now have up to {input.MaxTeamMembers} member{(input.MaxTeamMembers == 1 ? "" : "s")}.";
        return RedirectToAction(nameof(Index), null, null, "team-capacity");
    }

    [HttpPost("first-blood/test")]
    public async Task<IActionResult> TestFirstBloodWebhook(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!DiscordWebhookAddress.TryNormalize(settings.FirstBloodWebhookUrl, out var webhookUrl))
        {
            TempData["Error"] = "Save a valid Discord webhook before sending a test.";
            return RedirectToAction(nameof(Index));
        }
        var result = await firstBloodDiscord.SendTestAsync(webhookUrl, settings.PlatformName, ct);
        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "Test message sent to Discord." : result.Error ?? "Discord test failed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("branding/logo")]
    [RequestSizeLimit(2_200_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_200_000)]
    public async Task<IActionResult> UploadNavbarLogo(IFormFile? logo, CancellationToken ct)
    {
        if (logo is null)
        {
            TempData["Error"] = "Choose a logo image first.";
            return RedirectToAction(nameof(Index));
        }

        string? newPath = null;
        try
        {
            var current = await platform.GetAsync(ct);
            newPath = await branding.SaveLogoAsync(logo, ct);
            await platform.UpdateNavbarLogoAsync(newPath, ct);
            branding.DeleteCustomAsset(current.NavbarLogoPath);
            TempData["Message"] = "Navbar logo updated.";
        }
        catch (InvalidOperationException ex)
        {
            branding.DeleteCustomAsset(newPath);
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("branding/logo/remove")]
    public async Task<IActionResult> RemoveNavbarLogo(CancellationToken ct)
    {
        var current = await platform.GetAsync(ct);
        await platform.UpdateNavbarLogoAsync(null, ct);
        branding.DeleteCustomAsset(current.NavbarLogoPath);
        TempData["Message"] = "Navbar logo removed. The platform name is now shown instead.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("branding/logo/adaptive")]
    public async Task<IActionResult> UseAdaptiveNavbarLogo(CancellationToken ct)
    {
        await platform.UpdateNavbarLogoAsync(BrandingStorage.AdaptiveLogoPath, ct);
        TempData["Message"] = "Theme-adaptive navbar logo enabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("branding/logo/default")]
    public async Task<IActionResult> UseDefaultNavbarLogo(CancellationToken ct)
    {
        await platform.UpdateNavbarLogoAsync(BrandingStorage.DefaultLogoPath, ct);
        TempData["Message"] = "Default OwlCTF navbar branding enabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("branding/favicon")]
    [RequestSizeLimit(1_200_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_200_000)]
    public async Task<IActionResult> UploadFavicon(IFormFile? favicon, CancellationToken ct)
    {
        if (favicon is null)
        {
            TempData["Error"] = "Choose a favicon image first.";
            return RedirectToAction(nameof(Index), null, null, "favicon-branding");
        }

        string? newPath = null;
        try
        {
            var current = await platform.GetAsync(ct);
            newPath = await branding.SaveFaviconAsync(favicon, ct);
            await platform.UpdateFaviconAsync(newPath, ct);
            branding.DeleteCustomAsset(current.FaviconPath);
            TempData["Message"] = "Browser favicon updated.";
        }
        catch (InvalidOperationException ex)
        {
            branding.DeleteCustomAsset(newPath);
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), null, null, "favicon-branding");
    }

    [HttpPost("branding/favicon/default")]
    public async Task<IActionResult> UseDefaultFavicon(CancellationToken ct)
    {
        var current = await platform.GetAsync(ct);
        await platform.UpdateFaviconAsync(BrandingStorage.DefaultFaviconPath, ct);
        branding.DeleteCustomAsset(current.FaviconPath);
        TempData["Message"] = "Default OwlCTF favicon restored.";
        return RedirectToAction(nameof(Index), null, null, "favicon-branding");
    }

    [HttpGet("teams")]
    public async Task<IActionResult> ManageTeams(string? sort, string? direction, CancellationToken ct)
    {
        var selectedSort = sort switch { "captain" or "bracket" or "members" or "score" or "solves" or "created" or "state" => sort, _ => "name" };
        var selectedDirection = direction == "desc" ? "desc" : "asc";
        var descending = selectedDirection == "desc";
        var teams = await db.GetManagedTeamsAsync(ct);

        IOrderedEnumerable<AdminManagedTeamRecord> ordered = selectedSort switch
        {
            "captain" => descending ? teams.OrderByDescending(team => team.CaptainUsername) : teams.OrderBy(team => team.CaptainUsername),
            "bracket" => descending ? teams.OrderByDescending(team => TeamBracketCatalog.Get(team.BracketKey).Name) : teams.OrderBy(team => TeamBracketCatalog.Get(team.BracketKey).Name),
            "members" => descending ? teams.OrderByDescending(team => team.MemberCount) : teams.OrderBy(team => team.MemberCount),
            "score" => descending ? teams.OrderByDescending(team => team.Score) : teams.OrderBy(team => team.Score),
            "solves" => descending ? teams.OrderByDescending(team => team.SolveCount) : teams.OrderBy(team => team.SolveCount),
            "created" => descending ? teams.OrderByDescending(team => team.CreatedAtUtc) : teams.OrderBy(team => team.CreatedAtUtc),
            "state" => descending
                ? teams.OrderByDescending(team => team.IsDisbanded ? 3 : team.IsBanned ? 2 : team.IsSuspended ? 1 : 0)
                : teams.OrderBy(team => team.IsDisbanded ? 3 : team.IsBanned ? 2 : team.IsSuspended ? 1 : 0),
            _ => descending ? teams.OrderByDescending(team => team.Name) : teams.OrderBy(team => team.Name)
        };

        return View("Teams", new AdminTeamsViewModel(ordered.ThenBy(team => team.Name).ToArray(), selectedSort, selectedDirection));
    }

    [HttpPost("teams/{id:guid}/suspension")]
    public async Task<IActionResult> SetTeamSuspension(Guid id, TeamSuspensionInput input, string? sort, string? direction, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Suspension reason must be 500 characters or fewer.";
            return RedirectToAction(nameof(ManageTeams), new { sort, direction });
        }
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (!await db.SetTeamSuspensionAsync(id, input.Suspended, reason, ct))
            TempData["Error"] = "Team was not found or has already been disbanded.";
        else
        {
            scoreboard.Invalidate();
            TempData["Message"] = input.Suspended ? "Team suspended." : "Team restored.";
        }
        return RedirectToAction(nameof(ManageTeams), new { sort, direction });
    }

    [HttpGet("challenges/new")]
    public IActionResult NewChallenge() => View("ChallengeForm", new ChallengeInput { CategoryKey = ChallengeCategoryCatalog.DefaultKey });

    [HttpGet("challenges")]
    public async Task<IActionResult> ManageChallenges(string? sort, string? direction, CancellationToken ct)
    {
        var selectedSort = sort switch { "category" or "author" or "points" or "solves" or "files" or "updated" or "created" or "state" => sort, _ => "title" };
        var selectedDirection = direction == "desc" ? "desc" : "asc";
        var descending = selectedDirection == "desc";
        var challenges = await db.GetManagedChallengesAsync(ct);

        IOrderedEnumerable<AdminManagedChallengeRecord> ordered = selectedSort switch
        {
            "category" => descending ? challenges.OrderByDescending(challenge => ChallengeCategoryCatalog.Get(challenge.CategoryKey).Name) : challenges.OrderBy(challenge => ChallengeCategoryCatalog.Get(challenge.CategoryKey).Name),
            "author" => descending ? challenges.OrderByDescending(challenge => challenge.Author) : challenges.OrderBy(challenge => challenge.Author),
            "points" => descending ? challenges.OrderByDescending(challenge => challenge.CurrentValue) : challenges.OrderBy(challenge => challenge.CurrentValue),
            "solves" => descending ? challenges.OrderByDescending(challenge => challenge.SolveCount) : challenges.OrderBy(challenge => challenge.SolveCount),
            "files" => descending ? challenges.OrderByDescending(challenge => challenge.FileCount) : challenges.OrderBy(challenge => challenge.FileCount),
            "updated" => descending ? challenges.OrderByDescending(challenge => challenge.UpdatedAtUtc) : challenges.OrderBy(challenge => challenge.UpdatedAtUtc),
            "created" => descending ? challenges.OrderByDescending(challenge => challenge.CreatedAtUtc) : challenges.OrderBy(challenge => challenge.CreatedAtUtc),
            "state" => descending ? challenges.OrderByDescending(challenge => challenge.IsVisible) : challenges.OrderBy(challenge => challenge.IsVisible),
            _ => descending ? challenges.OrderByDescending(challenge => challenge.Title) : challenges.OrderBy(challenge => challenge.Title)
        };

        return View("Challenges", new AdminChallengesViewModel(ordered.ThenBy(challenge => challenge.Title).ToArray(), selectedSort, selectedDirection));
    }

    [HttpGet("challenges/{id:guid}/edit")]
    public async Task<IActionResult> EditChallenge(Guid id, CancellationToken ct)
    {
        var item = await db.GetChallengeAsync(id, null, true, ct);
        if (item is null) return NotFound();
        ViewBag.Files = await db.GetChallengeFilesAsync(id, ct);
        await using var instanceDb = await instanceDbFactory.CreateDbContextAsync(ct);
        var config = await instanceDb.InstanceConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == id, ct);
        return View("ChallengeForm", new ChallengeInput { Id = item.Id, Title = item.Title, Slug = item.Slug, Description = item.Description, Author = item.Author, CategoryKey = item.CategoryKey, Tags = string.Join(", ", item.TagList), Initial = item.Initial, Minimum = item.Minimum, Decay = item.Decay, IsVisible = item.IsVisible,
            DynamicInstanceEnabled = config?.Enabled == true, DockerImage = config?.DockerImage, ContainerPort = config?.ContainerPort ?? 8080,
            InstanceTtlSeconds = config?.TtlSeconds ?? 1800, MaxInstanceRenewals = config?.MaxRenewals ?? 3,
            InstanceNanoCpus = config?.NanoCpus ?? 500_000_000, InstanceMemoryMb = config is null ? 256 : (int)(config.MemoryBytes / 1_048_576), FlagEnvironmentVariable = config?.FlagEnvironmentVariable ?? "FLAG" });
    }

    [HttpPost("challenges/save")]
    [RequestSizeLimit(262_144_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 262_144_000)]
    public async Task<IActionResult> SaveChallenge(ChallengeInput input, CancellationToken ct)
    {
        if (input.Id is null && !input.DynamicInstanceEnabled && string.IsNullOrWhiteSpace(input.Flag)) ModelState.AddModelError(nameof(input.Flag), "A flag is required for a new static challenge.");
        if (input.DynamicInstanceEnabled && string.IsNullOrWhiteSpace(input.DockerImage)) ModelState.AddModelError(nameof(input.DockerImage), "A Docker image is required for a dynamic challenge.");
        if (input.Minimum > input.Initial) ModelState.AddModelError(nameof(input.Minimum), "Minimum value cannot be greater than the initial value.");
        if (input.Files.Count > 5) ModelState.AddModelError(nameof(input.Files), "A challenge can contain at most five files per upload.");
        if (input.Files.Any(f => f.Length <= 0 || f.Length > storage.MaxFileBytes)) ModelState.AddModelError(nameof(input.Files), "Each file must contain data and fit within the size limit.");
        if (!ChallengeCategoryCatalog.IsValid(input.CategoryKey)) ModelState.AddModelError(nameof(input.CategoryKey), "Select a valid predefined category.");
        if (!ChallengeTagPolicy.TryNormalize(input.Tags, out var normalizedTags, out var tagError)) ModelState.AddModelError(nameof(input.Tags), tagError!);
        if (!ModelState.IsValid) return View("ChallengeForm", input);

        var existing = input.Id is { } id ? await db.GetChallengeSecretAsync(id, ct) : null;
        if (input.Id is not null && existing is null) return NotFound();
        var flagHash = string.IsNullOrWhiteSpace(input.Flag) ? existing?.FlagHash ?? flags.Hash(Guid.NewGuid().ToString("N")) : flags.Hash(input.Flag);
        try
        {
            var challengeId = await db.SaveChallengeAsync(input.Id, input.Title.Trim(), input.Slug.Trim(), input.Description.Trim(), input.Author.Trim(), input.CategoryKey, normalizedTags, input.Initial, input.Minimum, input.Decay, flagHash, input.IsVisible, ct);
            await using (var instanceDb = await instanceDbFactory.CreateDbContextAsync(ct))
            {
                var config = await instanceDb.InstanceConfigs.SingleOrDefaultAsync(x => x.ChallengeId == challengeId, ct);
                if (config is null) { config = new ChallengeInstanceConfig { ChallengeId = challengeId }; instanceDb.InstanceConfigs.Add(config); }
                config.Enabled = input.DynamicInstanceEnabled;
                config.DockerImage = (input.DockerImage ?? "").Trim();
                config.ContainerPort = input.ContainerPort;
                config.TtlSeconds = input.InstanceTtlSeconds;
                config.MaxRenewals = input.MaxInstanceRenewals;
                config.NanoCpus = input.InstanceNanoCpus;
                config.MemoryBytes = input.InstanceMemoryMb * 1_048_576L;
                config.FlagEnvironmentVariable = input.FlagEnvironmentVariable;
                config.UpdatedAtUtc = DateTime.UtcNow;
                await instanceDb.SaveChangesAsync(ct);
            }
            foreach (var upload in input.Files)
            {
                await using var stream = upload.OpenReadStream();
                var stored = await storage.SaveAsync(stream, ct);
                var originalName = Path.GetFileName(upload.FileName);
                if (originalName.Length > 255) originalName = originalName[..255];
                try { await db.AddFileAsync(new ChallengeFileRecord(Guid.NewGuid(), challengeId, originalName, stored.StorageName, stored.SizeBytes, stored.Sha256), ct); }
                catch { storage.Delete(stored.StorageName); throw; }
            }
            scoreboard.Invalidate();
            TempData["Message"] = "Challenge saved.";
            return RedirectToAction(nameof(ManageChallenges));
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            ModelState.AddModelError(nameof(input.Slug), "That slug is already in use.");
            return View("ChallengeForm", input);
        }
    }

    [HttpPost("challenges/{id:guid}/archive")]
    public async Task<IActionResult> ArchiveChallenge(Guid id, CancellationToken ct)
    {
        await db.ArchiveChallengeAsync(id, ct);
        scoreboard.Invalidate();
        TempData["Message"] = "Challenge hidden.";
        return RedirectToAction(nameof(ManageChallenges));
    }

    [HttpPost("challenges/{id:guid}/delete")]
    public async Task<IActionResult> DeleteChallenge(Guid id, string? sort, string? direction, CancellationToken ct)
    {
        await using (var instanceDb = await instanceDbFactory.CreateDbContextAsync(ct))
        {
            if (await instanceDb.Instances.AnyAsync(x => x.ChallengeId == id && x.ActiveLeaseKey != null, ct))
            {
                TempData["Error"] = "Stop or wait for all active challenge instances before deleting this challenge.";
                return RedirectToAction(nameof(ManageChallenges), new { sort, direction });
            }
        }
        var result = await db.DeleteChallengeAsync(id, ct);
        if (!result.Deleted)
            TempData["Error"] = "Challenge was not found.";
        else
        {
            foreach (var storageName in result.StorageNames)
            {
                try { storage.Delete(storageName); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            scoreboard.Invalidate();
            TempData["Message"] = "Challenge permanently deleted.";
        }
        return RedirectToAction(nameof(ManageChallenges), new { sort, direction });
    }

    [HttpPost("challenges/{id:guid}/visibility")]
    public async Task<IActionResult> SetChallengeVisibility(Guid id, bool visible, string? sort, string? direction, CancellationToken ct)
    {
        if (!await db.SetChallengeVisibilityAsync(id, visible, ct))
            TempData["Error"] = "Challenge was not found.";
        else
        {
            scoreboard.Invalidate();
            TempData["Message"] = visible ? "Challenge published." : "Challenge hidden.";
        }
        return RedirectToAction(nameof(ManageChallenges), new { sort, direction });
    }

    [HttpPost("files/{id:guid}/delete")]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken ct)
    {
        var file = await db.DeleteFileRecordAsync(id, ct);
        if (file is not null) storage.Delete(file.StorageName);
        return RedirectToAction(nameof(EditChallenge), new { id = file?.ChallengeId });
    }

    [HttpPost("users/{id:guid}/admin")]
    public async Task<IActionResult> SetAdmin(Guid id, bool enabled, string? query, string? sort, string? direction, CancellationToken ct)
    {
        if (!await db.SetAdminAsync(id, enabled, ct)) TempData["Error"] = "The last administrator cannot be removed.";
        else TempData["Message"] = "Administrator access updated. The change will take effect the next time this user signs in.";
        return RedirectToAction(nameof(ManageUsers), new { query, sort, direction });
    }

    private static DateTime? AsUtc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
