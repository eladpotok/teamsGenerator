using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Security.Claims;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.GroupJoins;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.PlayerProfiles;

internal static class GroupJoinRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        await RosterCompatibilityRegression.Run(check);
        var ct = CancellationToken.None;
        var store = new Store();
        var groups = new Groups();
        var ai = new Analyzer();
        var entitlements = new Entitlements();
        var service = new GroupJoinService(store, groups, new Clock(), new GroupJoinRoster(store), ai, entitlements);
        var token = (await service.LinkAsync("owner", "group", true, ct)).Token!;
        check(token.Length == 64, "join capability has 256 bits of entropy");
        check((await service.LinkAsync("collaborator", "shared", true, ct)).Token == token, "co-organizer resolves canonical group link");
        var view = await service.PublicAsync(token, ct);
        var publicJson = JObject.FromObject(view);
        check(publicJson.Properties().Select(p => p.Name).OrderBy(x => x).SequenceEqual(new[]
        {
            "GroupName", "MatchName", "Location", "GroupLogoDataUrl", "Language", "ScheduleType",
            "RepeatDay", "RepeatTime", "EventDate", "EventTime", "EventDurationMinutes", "Rules", "Skills", "AlgorithmKey"
        }.OrderBy(x => x)), "public response is an explicit safe whitelist without roster, private ratings, owner, token or requests");
        check(view.Skills.Count == 3 && view.Skills[0].Id == "vision" && view.Rules!.NoSlideTackles, "custom configured skills and rules are public");
        check(view.ScheduleType == "repeating" && view.EventDate == null, "recurring schedule never invents a match date");
        check(view.EventDurationMinutes == 120, "group join does not depend on future match time");
        await Error(() => service.PublicAsync("bad", ct), 404, check);
        await Error(() => service.RequestsAsync("stranger", "group", ct), 403, check);
        await Error(() => service.LinkAsync("", "group", true, ct), 401, check);

        GroupJoinSubmission Submission(string name = "New player") => new(Guid.NewGuid().ToString("D"), name, true,
            "I play weekly and practice passing.", new() { ["vision"] = 6, ["passing"] = 7, ["stamina"] = 5 });
        var submission = Submission();
        await service.SubmitAsync(token, submission, ct);
        await service.SubmitAsync(token, submission, ct);
        var requests = await service.RequestsAsync("owner", "group", ct);
        check(requests.Count == 1 && ai.Calls == 0, "anonymous submit is idempotent and never invokes AI");
        check(store.Documents.Keys.All(key => key.StartsWith("group-joins/")), "submission cannot mutate roster or RSVP");
        await Error(() => service.SubmitAsync(token, Submission(), ct), 409, check);
        await Error(() => service.SubmitAsync(token, Submission() with { Ratings = new() { ["vision"] = 8 } }, ct), 400, check);
        await Error(() => service.SubmitAsync(token, Submission() with { Name = new string('n', 81) }, ct), 400, check);
        await Error(() => service.SubmitAsync(token, Submission() with { Background = new string('x', 2001) }, ct), 400, check);
        await Error(() => service.SubmitAsync(token, Submission() with { Ratings = new()
            { ["vision"] = double.NaN, ["passing"] = 5, ["stamina"] = 4 } }, ct), 400, check);
        var request = requests.Single();
        entitlements.Free.Add("collaborator");
        var beforeFreeAnalysis = JsonConvert.SerializeObject(store.Documents);
        await Error(() => service.AnalyzeAsync("collaborator", "shared", request.Id, request.Revision, ct), 403, check);
        check(ai.Calls == 0 && beforeFreeAnalysis == JsonConvert.SerializeObject(store.Documents),
            "Free co-organizer cannot call AI or mutate suggestions despite Premium owner access");
        check((await service.RequestsAsync("collaborator", "shared", ct)).Count == 1,
            "Premium-only AI does not prevent Free co-organizer from reviewing requests");
        entitlements.Free.Add("owner");
        await Error(() => service.AnalyzeAsync("owner", "group", request.Id, request.Revision, ct), 403, check);
        entitlements.Free.Remove("owner");
        var analyzed = await service.AnalyzeAsync("owner", "group", request.Id, request.Revision, ct);
        check(ai.Calls == 1 && analyzed.Suggestions!.All(s => s.InsufficientEvidence)
            && analyzed.SelfRatings["passing"] == 7, "organizer AI stores optional evidence without replacing self-ratings");
        check(!JsonConvert.SerializeObject(ai.Context).Contains("New player"), "AI group context has no player identities");
        await Error(() => service.ApproveAsync("owner", "group", request.Id, new(request.Revision, submission.Ratings), ct), 409, check);
        groups.Enabled = false;
        await Error(() => service.PublicAsync(token, ct), 403, check);
        await Error(() => service.SubmitAsync(token, Submission("Disabled"), ct), 403, check);
        await Error(() => service.ApproveAsync("owner", "group", request.Id, new(analyzed.Revision, submission.Ratings), ct), 403, check);
        await Error(() => service.AnalyzeAsync("owner", "group", request.Id, analyzed.Revision, ct), 403, check);
        groups.Enabled = true;
        groups.Deny = true;
        await Error(() => service.RejectAsync("owner", "group", request.Id, analyzed.Revision, ct), 403, check);
        groups.Deny = false;
        var final = new Dictionary<string, double>(submission.Ratings) { ["passing"] = 8 };
        var approved = await service.ApproveAsync("collaborator", "shared", request.Id, new(analyzed.Revision, final), ct);
        var rosterPath = store.Documents.Keys.Single(key => !key.StartsWith("group-joins/"));
        var roster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        var member = RosterDocument.Players(roster.Value).Single();
        check(approved.Status == "approved" && approved.ReviewedBy == "collaborator", "authorized review permanently adds group player");
        check(member["isArrived"]!.Value<bool>() == false && member["waitingListOrder"]!.Type == JTokenType.Null
            && member["isLocked"]!.Value<bool>() == false, "approval never sets attendance, waiting position or team lock");
        check(member["passing"]!.Value<double>() == 8 && member["vision"]!.Value<double>() == 6
            && approved.SelfRatings["passing"] == 7, "final edited and original self-ratings stay distinct");
        check(!member.Children<JProperty>().Any(p => p.Name is "goalkeeperSkill" or "isRecoveringFromInjury"),
            "approval never introduces a private dedicated assessment");
        check(RosterDocument.HasReceipt(roster.Value, request.Id), "roster player and idempotency receipt commit atomically");
        await service.ApproveAsync("owner", "group", request.Id, new(0, new()), ct);
        check(RosterDocument.Players((await store.ReadAsync<JToken>(rosterPath, ct))!.Value).Count == 1, "repeated approval cannot duplicate player");
        check(!store.Documents.Keys.Any(key => key.StartsWith("match-invitations/")), "membership never creates RSVP state");
        await Error(() => service.RejectAsync("owner", "group", request.Id, approved.Revision, ct), 409, check);

        check(RosterDocument.CanSave(new JArray(), "v1", null, new JArray()), "legacy rosters accept legacy clients");
        check(RosterDocument.CanSave(roster.Value, roster.ETag, null, RosterDocument.Players(roster.Value)),
            "enrolled rosters accept legacy edits retaining approved membership");
        check(!RosterDocument.CanSave(roster.Value, roster.ETag, null, new JArray()),
            "enrolled rosters reject ambiguous unversioned removals");
        check(!RosterDocument.CanSave(roster.Value, roster.ETag, "old", new JArray()), "stale saves cannot erase newly approved players");
        check(RosterDocument.CanSave(roster.Value, roster.ETag, roster.ETag, new JArray()), "current snapshots may edit or remove enrolled players");
        check(RosterDocument.CanSave(null, null, RosterDocument.MissingRevision, new JArray()), "empty roster creation has a conditional missing version");
        var removed = RosterDocument.WithPlayers(roster.Value, new JArray());
        check(RosterDocument.HasReceipt(removed, request.Id), "normal removal preserves the enrollment receipt");
        await store.WriteAsync(rosterPath, removed, roster.ETag, ct);
        await service.ApproveAsync("owner", "group", request.Id, new(0, new()), ct);
        check(RosterDocument.Players((await store.ReadAsync<JToken>(rosterPath, ct))!.Value).Count == 0, "approved request cannot resurrect a removed member");

        var retrySubmission = Submission("Retry player");
        await service.SubmitAsync(token, retrySubmission, ct);
        var retry = (await service.RequestsAsync("owner", "group", ct)).Single(r => r.Name == "Retry player");
        store.FailFinalization = true;
        await Error(() => service.ApproveAsync("owner", "group", retry.Id, new(retry.Revision, retrySubmission.Ratings), ct), 409, check);
        roster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        check(RosterDocument.Players(roster.Value).Count == 1, "approval may commit roster before final request acknowledgement");
        await store.WriteAsync(rosterPath, RosterDocument.WithPlayers(roster.Value, new JArray()), roster.ETag, ct);
        store.FailFinalization = false;
        await service.ApproveAsync("owner", "group", retry.Id, new(retry.Revision, retrySubmission.Ratings), ct);
        check(RosterDocument.Players((await store.ReadAsync<JToken>(rosterPath, ct))!.Value).Count == 0,
            "retry after partial failure and later deletion acknowledges receipt without resurrection");

        var rejectSubmission = Submission("Reject player");
        await service.SubmitAsync(token, rejectSubmission, ct);
        var rejected = (await service.RequestsAsync("owner", "group", ct)).Single(r => r.Name == "Reject player");
        await service.RejectAsync("owner", "group", rejected.Id, rejected.Revision, ct);
        await Error(() => service.ApproveAsync("owner", "group", rejected.Id, new(rejected.Revision, rejectSubmission.Ratings), ct), 409, check);
        check(RosterDocument.Players((await store.ReadAsync<JToken>(rosterPath, ct))!.Value).Count == 0, "rejection never inserts a player");

        var changedSubmission = Submission("Changed skills");
        await service.SubmitAsync(token, changedSubmission, ct);
        var changed = (await service.RequestsAsync("owner", "group", ct)).Single(r => r.Name == "Changed skills");
        groups.Config.SkillDefinitions = SkillDefinition.CreateDefaults();
        await Error(() => service.ApproveAsync("owner", "group", changed.Id, new(changed.Revision, changedSubmission.Ratings), ct), 409, check);
        groups.Config.SkillDefinitions = Groups.Skills();
        groups.Maximum = 1;
        roster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        await store.WriteAsync(rosterPath, RosterDocument.WithPlayers(roster.Value,
            new JArray(new JObject { ["key"] = "quota-player", ["name"] = "Existing quota player" })), roster.ETag, ct);
        await Error(() => service.ApproveAsync("owner", "group", changed.Id, new(changed.Revision, changedSubmission.Ratings), ct), 409, check);
        check((await service.RequestsAsync("owner", "group", ct)).Single(r => r.Id == changed.Id).Status == "pending",
            "quota conflict leaves request pending for manual review");
        groups.Maximum = 10;
        roster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        await store.WriteAsync(rosterPath, RosterDocument.WithPlayers(roster.Value, new JArray()), roster.ETag, ct);
        ai.BeforeReturn = () => groups.Deny = true;
        await Error(() => service.AnalyzeAsync("owner", "group", changed.Id, changed.Revision, ct), 403, check);
        groups.Deny = false; ai.BeforeReturn = null;
        check((await service.RequestsAsync("owner", "group", ct)).Single(r => r.Id == changed.Id).Suggestions == null,
            "permission revoked during AI cannot save suggestions");
        ai.BeforeReturn = () => entitlements.Free.Add("owner");
        await Error(() => service.AnalyzeAsync("owner", "group", changed.Id, changed.Revision, ct), 403, check);
        entitlements.Free.Remove("owner"); ai.BeforeReturn = null;
        check((await service.RequestsAsync("owner", "group", ct)).Single(r => r.Id == changed.Id).Suggestions == null,
            "Premium revoked during AI cannot persist a result");
        await service.RevokeAsync("owner", "group", ct);
        await Error(() => service.PublicAsync(token, ct), 404, check);
        await Error(() => service.SubmitAsync(token, Submission("Old link"), ct), 404, check);
        var next = (await service.LinkAsync("owner", "group", true, ct)).Token!;
        check(next != token && (await service.RequestsAsync("owner", "group", ct)).Count == 4, "link rotation preserves submitted requests");
        await Error(() => service.PublicAsync(token, ct), 404, check);

        var firstConcurrent = Submission("Concurrent one");
        var secondConcurrent = Submission("Concurrent two");
        await service.SubmitAsync(next, firstConcurrent, ct);
        await service.SubmitAsync(next, secondConcurrent, ct);
        var concurrent = (await service.RequestsAsync("owner", "group", ct)).Where(r => r.Name.StartsWith("Concurrent")).ToArray();
        var beforeConcurrent = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        store.YieldBeforeWrites = true;
        async Task ApproveWithRetry(GroupJoinRequest item)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var current = (await service.RequestsAsync("owner", "group", ct)).Single(r => r.Id == item.Id);
                    await service.ApproveAsync("owner", "group", item.Id, new(current.Revision, item.SelfRatings), ct);
                    return;
                }
                catch (InvitationException error) when (error.StatusCode == 409 && attempt < 19) { }
            }
            throw new InvalidOperationException("Concurrent approval did not converge.");
        }
        await Task.WhenAll(ApproveWithRetry(concurrent[0]), ApproveWithRetry(concurrent[0]), ApproveWithRetry(concurrent[1]));
        var afterConcurrent = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        check(RosterDocument.Players(afterConcurrent.Value).Count == 2
            && RosterDocument.Players(afterConcurrent.Value).Select(p => p["key"]!.Value<string>()).Distinct().Count() == 2,
            "overlapping same and different request approvals converge without duplicate or lost members");
        check(!await store.WriteAsync(rosterPath, RosterDocument.WithPlayers(beforeConcurrent.Value, new JArray()), beforeConcurrent.ETag, ct),
            "a roster snapshot read before concurrent approval cannot overwrite the approved members");
        check(RosterDocument.Players(afterConcurrent.Value).All(p => p["isArrived"]!.Value<bool>() == false && p["waitingListOrder"]!.Type == JTokenType.Null),
            "concurrent enrollment leaves every match attendance response unset");
        entitlements.Free.Remove("collaborator");
        await service.AnalyzeAsync("collaborator", "shared", changed.Id, changed.Revision, ct);
        var collaboratorContext = JObject.FromObject(ai.Context!);
        check(!collaboratorContext["ratingContextAvailable"]!.Value<bool>()
            && collaboratorContext["skills"]!.All(skill => skill["average"]!.Type == JTokenType.Null
                && skill["ratedPlayers"]!.Value<int>() == 0), "co-organizer AI cannot reveal the owner's private rating aggregates");
        var previewConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["GroupJoins:AllowRosterEnrollment"] = "false" }).Build();
        var preview = new GroupJoinService(store, groups, new Clock(), new GroupJoinRoster(store), ai, entitlements, previewConfiguration);
        check(!(await preview.LinkAsync("owner", "group", false, ct)).CanApprove,
            "a production-data preview explicitly reports that final enrollment is paused");
        var beforePreviewApproval = JsonConvert.SerializeObject(store.Documents);
        await Error(() => preview.ApproveAsync("owner", "group", changed.Id, new(changed.Revision, changedSubmission.Ratings), ct), 409, check);
        check(beforePreviewApproval == JsonConvert.SerializeObject(store.Documents),
            "paused preview approval cannot write a decision or change live roster format");

        var output = """[{"skillId":"vision","suggestedRating":null,"explanation":"No direct evidence.","insufficientEvidence":true}]""";
        check(GroupJoinAnalyzer.Parse(output, new[] { "vision" }).Single().SuggestedRating == null, "AI can explicitly decline to calibrate a skill");
        var fractional = """[{"skillId":"vision","suggestedRating":6.2,"explanation":"A small, tentative adjustment for described link-up play.","insufficientEvidence":false}]""";
        check(GroupJoinAnalyzer.Parse(fractional, new[] { "vision" }).Single().SuggestedRating == 6,
            "small fractional AI adjustments round down to whole numbers");
        foreach (var (value, expected) in new[] { (1.0, 1.0), (4.2, 4.0), (4.5, 5.0), (5.6, 6.0), (9.6, 10.0), (10.0, 10.0) })
        {
            var json = JsonConvert.SerializeObject(new[] { new { skillId = "vision", suggestedRating = value,
                explanation = "Tentative estimate.", insufficientEvidence = false } });
            check(GroupJoinAnalyzer.Parse(json, new[] { "vision" }).Single().SuggestedRating == expected,
                "AI whole-number rounding uses nearest integer with midpoint up, within 1..10");
        }
        var mixed = """
            [{"skillId":"attack","suggestedRating":5.5,"explanation":"A tentative adjustment for pace.","insufficientEvidence":false},
             {"skillId":"passing","suggestedRating":5.0,"explanation":"No passing evidence.","insufficientEvidence":true}]
            """;
        var normalized = GroupJoinAnalyzer.Parse(mixed, new[] { "attack", "passing" });
        check(normalized[0].SuggestedRating == 6 && !normalized[0].InsufficientEvidence,
            "a supported AI adjustment survives another skill repeating a numeric self-rating without evidence");
        check(normalized[1].SuggestedRating == null && normalized[1].InsufficientEvidence,
            "insufficient-evidence numeric output is discarded, never applied to a skill");
        foreach (var invalid in new[] { "not json", "[]", output + "[]", output.Replace("vision", "other"),
            output.Replace("null", "11").Replace("true", "false"), output.Replace("null", "\"5\""),
            output.Replace("null", "11"), output.Replace("null", "0"), output.Replace("true", "false"),
            output.Replace("\"skillId\":\"vision\"", "\"skillId\":\"vision\",\"skillId\":\"vision\""),
            output.Replace("No direct evidence.", new string('x', 501)) })
        {
            var failed = false;
            try { GroupJoinAnalyzer.Parse(invalid, new[] { "vision" }); }
            catch (Exception error) when (error is JsonException or InvalidDataException) { failed = true; }
            check(failed, "malformed or unsupported AI output cannot become ratings");
        }
        check(typeof(GroupJoinsController).IsDefined(typeof(AuthorizeAttribute), true), "all organizer actions require JWT authorization");
        check(typeof(GroupJoinsController).GetMethods().Where(method => method.IsDefined(typeof(AllowAnonymousAttribute), true))
            .Select(method => method.Name).SequenceEqual(new[] { "Public" }), "only group details are anonymous; submitting requires an account");
        var telemetry = new RequestTelemetry { Url = new Uri($"https://api.invalid/GroupJoins/Public/{token}/Requests?q=private"), Name = "POST GroupJoins/Public/token/Requests" };
        new InvitationTelemetryRedactor().Initialize(telemetry);
        check(!telemetry.Url.ToString().Contains(token) && telemetry.Url.Query == "" && telemetry.Name.Contains("[redacted]"),
            "public join capability is redacted from request telemetry");
    }

    public static async Task RunProfiles(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new Store { YieldBeforeWrites = true };
        var profiles = new PlayerProfileService(store, new Clock());
        ClaimsPrincipal User(string uid, string name = "Existing organizer", string provider = "password") =>
            new(new ClaimsIdentity(new[] { new Claim("user_id", uid), new Claim("name", name),
                new Claim("picture", "https://example.test/avatar.png"),
                new Claim("firebase", JsonConvert.SerializeObject(new { sign_in_provider = provider })) }, "firebase"));
        var user = User("one");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => profiles.EnsureAsync(user, ct)));
        var basic = concurrent[0];
        check(concurrent.All(item => item.Id == basic.Id && item.Revision == 1) && store.Documents.Count == 1,
            "concurrent bootstrap creates exactly one profile per account");
        check(basic.Name == "Existing organizer" && basic.PhotoUrl == "https://example.test/avatar.png"
            && basic.IsComplete && basic.Ratings.Count == 0, "basic identity completes an account profile without invented skills");
        await Error(() => profiles.EnsureAsync(new ClaimsPrincipal(), ct), 401, check);
        await Error(() => profiles.EnsureAsync(User("guest", provider: "anonymous"), ct), 401, check);
        await Error(() => profiles.GetAsync("", ct), 401, check);
        var malformed = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "bad"), new Claim("firebase", "{") }, "jwt"));
        await Error(() => profiles.EnsureAsync(malformed, ct), 401, check);
        var second = await profiles.EnsureAsync(User("two", "Second player"), ct);
        check(second.Id != basic.Id && second.Name == "Second player", "accounts never share a profile");
        check(second.AccountMode == null, "new and legacy profiles do not infer an organizer preference");
        var playerMode = await profiles.SetModeAsync(User("two"), new(second.Revision, "player"), ct);
        check(playerMode.IsComplete && playerMode.AccountMode == "player" && playerMode.Name == second.Name
            && playerMode.Ratings.Count == 0, "player preference persists before skills without inventing them");
        check((await profiles.EnsureAsync(User("two"), ct)).AccountMode == "player",
            "returning sessions retain the account workflow preference");
        await Error(() => profiles.SetModeAsync(User("two"), new(second.Revision, "organizer"), ct), 409, check);
        await Error(() => profiles.SetModeAsync(User("two"), new(playerMode.Revision, "admin"), ct), 400, check);
        await Error(() => profiles.SetModeAsync(User("guest", provider: "anonymous"), new(1, "player"), ct), 401, check);
        var organizerMode = await profiles.SetModeAsync(User("two"), new(playerMode.Revision, "organizer"), ct);
        check(organizerMode.AccountMode == "organizer" && organizerMode.Id == second.Id
            && organizerMode.CreatedAt == second.CreatedAt, "organizing later preserves player identity and creation time");
        var resetMode = await profiles.SetModeAsync(User("two"), new(organizerMode.Revision, null), ct);
        check(resetMode.AccountMode == null && (await profiles.GetAsync("one", ct)).AccountMode == null,
            "back to choice clears only the current account preference");
        var update = new PlayerProfileUpdate(basic.Revision, " My player name ", null, " I play weekly. ", true,
            SkillDefinition.CreateDefaults().ToDictionary(skill => skill.Id, _ => 6.5));
        var complete = await profiles.UpdateAsync(user, update, ct);
        check(complete.Ratings.Values.All(value => value == 7)
            && (await store.ReadAsync<PlayerProfile>(PlayerProfileService.Key("one"), ct))!.Value.Ratings.Values.All(value => value == 7),
            "profile updates persist whole-number skill ratings, including legacy decimal clients");
        check(complete.IsComplete && complete.Name == "My player name" && complete.Background == "I play weekly."
            && complete.Revision == 2 && complete.Id == basic.Id && complete.CreatedAt == basic.CreatedAt,
            "completion trims details and preserves stable identity and creation time");
        check((await profiles.EnsureAsync(User("one", "Changed Firebase name"), ct)).Name == "My player name",
            "returning login never overwrites edited profile details with auth defaults");
        check((await profiles.GetAsync("two", ct)).Name == "Second player", "editing one account does not touch another");
        await Error(() => profiles.UpdateAsync(user, update, ct), 409, check);
        foreach (var invalid in new[] {
            update with { Name = "bad\nname" }, update with { Name = new string('n', 81) },
            update with { Background = new string('b', 2001) },
            update with { PhotoUrl = "javascript:alert(1)" }, update with { PhotoUrl = "http://example.test/a.png" },
            update with { PhotoUrl = "https://user:pass@example.test/a.png" },
            update with { Ratings = new() }, update with { Ratings = new(update.Ratings) { ["attack"] = double.NaN } },
            update with { Ratings = new(update.Ratings) { ["attack"] = 11 } },
            update with { Ratings = new(update.Ratings) { ["extra"] = 5 } }
        }) await Error(() => profiles.UpdateAsync(user, invalid with { Revision = complete.Revision }, ct), 400, check);
        check((await profiles.GetAsync("one", ct)).Revision == 2, "failed validations never mutate profile state");
        var writes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try { await profiles.UpdateAsync(user, update with { Revision = 2, Name = "Racer " + i }, ct); return true; }
            catch (InvitationException error) when (error.StatusCode == 409) { return false; }
        }));
        check(writes.Count(written => written) == 1, "simultaneous profile edits cannot overwrite each other");
        complete = await profiles.GetAsync("one", ct);
        check(store.Documents.Keys.All(key => key.StartsWith("player-profiles/v1/accounts/")),
            "bootstrap and edit do not create groups, rosters, permissions, teams, or statistics");
        check(typeof(PlayerProfileController).IsDefined(typeof(AuthorizeAttribute), true)
            && !typeof(PlayerProfileController).GetMethods().Any(method => method.IsDefined(typeof(AllowAnonymousAttribute), true)),
            "all profile endpoints require authentication");
        check(typeof(PlayerProfile).GetProperties().All(property => property.Name is not ("Email" or "UserId" or "OwnerId")),
            "profile DTO never returns private account identifiers or email");
        var basicSave = await profiles.UpdateAsync(User("two"),
            new(resetMode.Revision, "Second player", second.PhotoUrl, "", false), ct);
        check(basicSave.IsComplete && basicSave.Ratings.Count == 0, "basic profile save never creates account skills");
        var unnamedUser = User("unnamed", "");
        var unnamed = await profiles.EnsureAsync(unnamedUser, ct);
        foreach (var name in new string?[] { null, "", "   " })
        {
            unnamed = await profiles.UpdateAsync(unnamedUser, new(unnamed.Revision, name, null, null, false), ct);
            check(unnamed.Name == "" && unnamed.PhotoUrl == null && !unnamed.IsComplete && unnamed.AccountMode == null,
                "optional name and photo may be absent without assigning roles or requiring account completion");
        }

        var groups = new Groups();
        var joins = new GroupJoinService(store, groups, new Clock(), new GroupJoinRoster(store), new Analyzer(), new Entitlements());
        var token = (await joins.LinkAsync("owner", "group", true, ct)).Token!;
        var input = new ProfileGroupJoinSubmission(Guid.NewGuid().ToString("D"), complete.Revision,
            new() { ["passing"] = 9, ["stamina"] = 6.5, ["vision"] = 8 });
        await Error(() => joins.SubmitProfileAsync("two", token, input with { ProfileRevision = second.Revision }, ct), 409, check);
        await Error(() => joins.SubmitProfileAsync("one", token, input with { ProfileRevision = 1 }, ct), 409, check);
        await joins.SubmitProfileAsync("one", token, input, ct);
        await joins.SubmitProfileAsync("one", token, input, ct);
        var request = (await joins.RequestsAsync("owner", "group", ct)).Single();
        check(request.ProfileId == complete.Id && request.Name == complete.Name && request.Background == complete.Background
            && request.SelfRatings["vision"] == 8 && request.SelfRatings["passing"] == 9
            && request.Status == "pending", "join snapshots independent group ratings even for skills also present in a legacy profile");
        var renamed = await profiles.UpdateAsync(user, update with { Revision = complete.Revision, Name = "Renamed player" }, ct);
        await Error(() => joins.SubmitProfileAsync("one", token, input with
            { ProfileRevision = renamed.Revision, SubmissionId = Guid.NewGuid().ToString("D") }, ct), 409, check);
        check((await joins.RequestsAsync("owner", "group", ct)).Single().Name == complete.Name,
            "profile edits neither duplicate pending requests nor rewrite their reviewed snapshot");
        groups.GroupId = "another-group";
        var another = (await joins.LinkAsync("owner", "another-group", true, ct)).Token!;
        await joins.SubmitProfileAsync("one", another, input with { ProfileRevision = renamed.Revision }, ct);
        check((await joins.RequestsAsync("owner", "another-group", ct)).Single().ProfileId == complete.Id,
            "same profile can request another group without duplicate profile registration");
        var publicView = JObject.FromObject(await joins.PublicAsync(token, ct));
        check(!publicView.ToString().Contains(complete.Id) && publicView["Requests"] == null,
            "public group details never expose profiles or applications");
        check(store.Documents.Keys.All(key => key.StartsWith("player-profiles/") || key.StartsWith("group-joins/") || key.StartsWith("player-memberships/")),
            "profile-backed joining still never enrolls a roster player or changes organizer access");
        check(typeof(ProfileGroupJoinSubmission).GetProperties().Select(p => p.Name).OrderBy(name => name)
            .SequenceEqual(new[] { "AlgorithmKey", "Background", "GoalkeeperSkill", "IsGoalKeeper", "IsRecoveringFromInjury",
                "Name", "Positions", "ProfileRevision", "Ratings", "Skills", "SubmissionId" }),
            "client chooses group answers and roster name but cannot choose account or profile identity");

        var photoUser = User("photo-owner");
        var photoProfile = await profiles.EnsureAsync(photoUser, ct);
        byte[] Photo(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(SKColors.Green);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(format, 100);
            return encoded.ToArray();
        }
        string DataUrl(byte[] bytes) => "data:image/png;base64," + Convert.ToBase64String(bytes);
        var png = Photo(256, 256);
        var photoUpdate = update with { Revision = photoProfile.Revision, PhotoUrl = DataUrl(png) };
        photoProfile = await profiles.UpdateAsync(photoUser, photoUpdate, ct);
        check(photoProfile.PhotoUrl == photoUpdate.PhotoUrl
            && (await profiles.EnsureAsync(photoUser, ct)).PhotoUrl == photoUpdate.PhotoUrl,
            "player uploaded PNG persists across bootstrap without auth picture overwriting it");
        var padded = new byte[512 * 1024];
        png.CopyTo(padded, 0);
        photoProfile = await profiles.UpdateAsync(photoUser, photoUpdate with
            { Revision = photoProfile.Revision, PhotoUrl = DataUrl(padded) }, ct);
        check(photoProfile.PhotoUrl == DataUrl(padded), "PNG at exact 512 KiB limit is accepted");
        foreach (var invalidPhoto in new[] {
            DataUrl(new byte[512 * 1024 + 1]), "data:image/png;base64,not-base64!",
            "data:image/svg+xml;base64,PHN2Zy8+", DataUrl(Photo(257, 256)), DataUrl(Photo(256, 257)),
            DataUrl(Photo(256, 256, SKEncodedImageFormat.Jpeg)), DataUrl(png[..(png.Length / 2)]),
            DataUrl(System.Text.Encoding.UTF8.GetBytes("not a PNG")), "data:image/png;base64,"
        })
            await Error(() => profiles.UpdateAsync(photoUser, photoUpdate with
                { Revision = photoProfile.Revision, PhotoUrl = invalidPhoto }, ct), 400, check);
        check((await profiles.GetAsync("photo-owner", ct)).Revision == photoProfile.Revision,
            "invalid/oversized/truncated photo uploads do not mutate the saved profile");
        var removed = await profiles.UpdateAsync(photoUser, photoUpdate with
            { Revision = photoProfile.Revision, PhotoUrl = null }, ct);
        check(removed.PhotoUrl == null && (await profiles.EnsureAsync(photoUser, ct)).PhotoUrl == null,
            "removing a photo persists and does not restore the provider image");
        check((await profiles.GetAsync("two", ct)).PhotoUrl == "https://example.test/avatar.png",
            "photo upload/removal cannot change another account");
        var limit = Convert.ToInt64(typeof(PlayerProfileController).GetMethod(nameof(PlayerProfileController.Update))!
            .CustomAttributes.Single(attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute)).ConstructorArguments[0].Value);
        check(limit == 750000 && System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(
            photoUpdate with { PhotoUrl = DataUrl(padded), Background = new string('א', 2000) })) < limit,
            "PUT limit accommodates maximum encoded photo and Unicode profile fields");

        check(!JsonConvert.DeserializeObject<PlayerProfile>("""{"Name":"Legacy player"}""")!.IsRecoveringFromInjury,
            "stored legacy profiles have no recovery condition by default");
        var recoveryUser = User("recovery-owner");
        var recoveryProfile = await profiles.EnsureAsync(recoveryUser, ct);
        var recoveryUpdate = update with { Revision = recoveryProfile.Revision, IsRecoveringFromInjury = true };
        recoveryProfile = await profiles.UpdateAsync(recoveryUser, recoveryUpdate, ct);
        check(recoveryProfile.IsRecoveringFromInjury
            && (await profiles.EnsureAsync(recoveryUser, ct)).IsRecoveringFromInjury
            && recoveryProfile.Ratings.Values.All(value => value == 7),
            "recovery condition persists on the account profile without reducing saved ratings");
        var legacyUpdate = JObject.FromObject(recoveryUpdate with { Revision = recoveryProfile.Revision });
        legacyUpdate.Remove(nameof(PlayerProfileUpdate.IsRecoveringFromInjury));
        recoveryProfile = await profiles.UpdateAsync(recoveryUser, legacyUpdate.ToObject<PlayerProfileUpdate>()!, ct);
        check(recoveryProfile.IsRecoveringFromInjury, "older clients omitting recovery cannot clear the saved condition");
        recoveryProfile = await profiles.UpdateAsync(recoveryUser,
            recoveryUpdate with { Revision = recoveryProfile.Revision, IsRecoveringFromInjury = false }, ct);
        check(!recoveryProfile.IsRecoveringFromInjury && !(await profiles.GetAsync("recovery-owner", ct)).IsRecoveringFromInjury
            && recoveryProfile.Ratings.Values.All(value => value == 7), "explicitly clearing recovery preserves base ratings");
        check(!(await profiles.GetAsync("two", ct)).IsRecoveringFromInjury
            && store.Documents.Keys.All(key => key.StartsWith("player-profiles/") || key.StartsWith("group-joins/") || key.StartsWith("player-memberships/")),
            "profile condition edits cannot alter another account, roster assessments or membership");

        var optionalUser = User("optional-background");
        var optionalProfile = await profiles.EnsureAsync(optionalUser, ct);
        optionalProfile = await profiles.UpdateAsync(optionalUser, update with
            { Revision = optionalProfile.Revision, Name = "No background", Background = new string('b', 2000) }, ct);
        check(optionalProfile.Background.Length == 2000, "optional background retains the 2000-character limit");
        foreach (var background in new string?[] { "", "  ", null })
        {
            optionalProfile = await profiles.UpdateAsync(optionalUser, update with
                { Revision = optionalProfile.Revision, Name = "No background", Background = background }, ct);
            check(optionalProfile.IsComplete && optionalProfile.Background == ""
                && (await profiles.EnsureAsync(optionalUser, ct)).IsComplete,
                "empty, whitespace and omitted/null backgrounds save as empty without blocking profile completion");
        }
        var omittedBackground = JObject.FromObject(update with
            { Revision = optionalProfile.Revision, Name = "No background" });
        omittedBackground.Remove(nameof(PlayerProfileUpdate.Background));
        optionalProfile = await profiles.UpdateAsync(optionalUser, omittedBackground.ToObject<PlayerProfileUpdate>()!, ct);
        check(optionalProfile.IsComplete && optionalProfile.Background == "", "clients may omit optional background entirely");
        check(!(optionalProfile with { Name = "" }).IsComplete && (optionalProfile with { Ratings = new() }).IsComplete,
            "profile completion requires a name, not global ratings");
        await joins.SubmitProfileAsync("optional-background", another, input with
            { ProfileRevision = optionalProfile.Revision, SubmissionId = Guid.NewGuid().ToString("D") }, ct);
        var optionalRequest = (await joins.RequestsAsync("owner", "another-group", ct))
            .Single(item => item.ProfileId == optionalProfile.Id);
        check(optionalRequest.Background == "" && optionalRequest.Status == "pending",
            "an authenticated complete profile with no background can request to join without automatic enrollment");

        var baselineProfile = await profiles.GetAsync("one", ct);
        var baselineJson = JsonConvert.SerializeObject(baselineProfile);
        groups.GroupId = "ratings-group-a";
        var groupAToken = (await joins.LinkAsync("owner", groups.GroupId, true, ct)).Token!;
        var baselineSubmission = input with { ProfileRevision = baselineProfile.Revision, SubmissionId = Guid.NewGuid().ToString("D") };
        await joins.SubmitProfileAsync("one", groupAToken, baselineSubmission, ct);
        var groupARequest = (await joins.RequestsAsync("owner", groups.GroupId, ct)).Single();
        await joins.ApproveAsync("owner", groups.GroupId, groupARequest.Id,
            new(groupARequest.Revision, new(baselineSubmission.Ratings) { ["passing"] = 4.5 }), ct);
        var groupARosterPath = store.Documents.Keys.Single(key =>
            !key.StartsWith("player-profiles/") && !key.StartsWith("group-joins/")
            && !key.StartsWith("player-memberships/") && !key.StartsWith("player-membership-links/"));
        var groupARoster = (await store.ReadAsync<JToken>(groupARosterPath, ct))!;
        check(RosterDocument.Players(groupARoster.Value).Single()["passing"]!.Value<double>() == 5
            && groupARequest.SelfRatings["passing"] == 9,
            "organizer final ratings apply to that group roster, not the submitted self-ratings");
        check(JsonConvert.SerializeObject(await profiles.GetAsync("one", ct)) == baselineJson,
            "organizer rating changes cannot rewrite the player's account profile");
        groups.GroupId = "ratings-group-b";
        var groupBToken = (await joins.LinkAsync("owner", groups.GroupId, true, ct)).Token!;
        await joins.SubmitProfileAsync("one", groupBToken, baselineSubmission, ct);
        var groupBRequest = (await joins.RequestsAsync("owner", groups.GroupId, ct)).Single();
        check(groupBRequest.SelfRatings["passing"] != baselineProfile.Ratings["passing"]
            && groupBRequest.SelfRatings["passing"] == 9 && groupBRequest.Status == "pending",
            "a later group uses its submitted answers, not account or previous organizer ratings");
        var basicsOnly = await profiles.UpdateAsync(user, new(baselineProfile.Revision,
            baselineProfile.Name, baselineProfile.PhotoUrl, baselineProfile.Background, baselineProfile.IsGoalKeeper), ct);
        check(basicsOnly.Ratings.OrderBy(pair => pair.Key).SequenceEqual(baselineProfile.Ratings.OrderBy(pair => pair.Key)),
            "editing basic details preserves legacy ratings without requiring or replacing them");

        var legacy = await store.ReadAsync<PlayerProfile>(PlayerProfileService.Key("one"), ct);
        await store.WriteAsync(PlayerProfileService.Key("one"), legacy!.Value with
        {
            Ratings = new(legacy.Value.Ratings) { ["attack"] = 5.6, ["defence"] = 4.2 }
        }, legacy.ETag, ct);
        var beforeRead = JsonConvert.SerializeObject(store.Documents);
        var legacyView = await profiles.EnsureAsync(user, ct);
        check(legacyView.Ratings["attack"] == 6 && legacyView.Ratings["defence"] == 4
            && (await profiles.GetAsync("one", ct)).Ratings["defence"] == 4
            && JsonConvert.SerializeObject(store.Documents) == beforeRead,
            "legacy decimal profile reads round without rewriting stored profile, roster or history");
        await RunAlgorithms(check);
        await RunGroupConditions(check);
    }

    private static async Task RunGroupConditions(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        foreach (var algorithm in new[] { 0, 1, 2, 3 })
        foreach (var keeper in new[] { false, true })
        {
            var store = new Store { YieldBeforeWrites = true };
            var groups = new Groups();
            groups.Config.SelectedAlgoKey = algorithm;
            var clock = new Clock();
            var profiles = new PlayerProfileService(store, clock);
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "conditions"),
                new Claim("name", "Condition player"), new Claim("firebase", """{"sign_in_provider":"password"}""") }, "firebase"));
            var profile = await profiles.EnsureAsync(user, ct);
            profile = await profiles.UpdateAsync(user, new(profile.Revision, profile.Name, null, "", !keeper,
                IsRecoveringFromInjury: !keeper), ct);
            var accountBefore = JsonConvert.SerializeObject(await profiles.GetAsync("conditions", ct));
            var service = new GroupJoinService(store, groups, clock, new GroupJoinRoster(store), new Analyzer(), new Entitlements());
            var memberships = new PlayerMembershipService(store);
            var token = (await service.LinkAsync("owner", "group", true, ct)).Token!;
            var view = await service.PublicAsync(token, ct);
            var input = new ProfileGroupJoinSubmission(Guid.NewGuid().ToString("D"), profile.Revision,
                view.Skills.ToDictionary(skill => skill.Id, _ => 6.0), algorithm,
                algorithm == 2 ? keeper ? new() { 0 } : new() { 0, 3 } : new(), "",
                view.Skills.ToList(), IsGoalKeeper: algorithm == 2 ? !keeper : keeper,
                IsRecoveringFromInjury: keeper, GoalkeeperSkill: keeper && algorithm != 3 ? 8.5 : null);
            var beforeInvalid = JsonConvert.SerializeObject(store.Documents);
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, 10.1 })
                await Error(() => service.SubmitProfileAsync("conditions", token, input with { GoalkeeperSkill = value }, ct), 400, check);
            check(JsonConvert.SerializeObject(store.Documents) == beforeInvalid,
                "invalid goalkeeping scores cannot mutate requests, membership pointers or roster");
            await service.SubmitProfileAsync("conditions", token, input, ct);
            await service.SubmitProfileAsync("conditions", token, input, ct);
            var request = (await service.RequestsAsync("owner", "group", ct)).Single();
            var expectedScore = keeper && algorithm != 3 ? (double?)9 : null;
            check(request.IsGoalKeeper == keeper && request.IsRecoveringFromInjury == keeper
                && request.GoalkeeperSkill == expectedScore && request.SelfRatings.Values.All(value => value == 6),
                "group role and recovery override account defaults; exclusive GK position decides positions algorithm");
            var pending = (await memberships.SummariesAsync("conditions", groups, ct)).Single();
            check(pending.Status == "pending" && pending.Profile!.IsGoalKeeper == keeper
                && pending.Profile.IsRecoveringFromInjury == keeper && pending.Profile.GoalkeeperSkill == expectedScore,
                "pending membership preserves group-specific role, recovery and dedicated score");
            await Error(() => service.ApproveAsync("stranger", "group", request.Id, new(request.Revision, input.Ratings), ct), 403, check);
            var final = input.Ratings.ToDictionary(pair => pair.Key, _ => 7.0);
            await service.ApproveAsync("owner", "group", request.Id, new(request.Revision, final), ct);
            await service.ApproveAsync("owner", "group", request.Id, new(0, new()), ct);
            var path = store.Documents.Keys.Single(key => !key.StartsWith("player-profiles/")
                && !key.StartsWith("group-joins/") && !key.StartsWith("player-memberships/")
                && !key.StartsWith("player-membership-links/"));
            var storedRoster = (await store.ReadAsync<JToken>(path, ct))!;
            var member = RosterDocument.Players(storedRoster.Value).Single();
            check(member["isGoalKeeper"]!.Value<bool>() == keeper
                && (member["isRecoveringFromInjury"]?.Value<bool>() ?? false) == keeper
                && member["goalkeeperSkill"]?.Value<double>() == expectedScore
                && final.All(pair => member[pair.Key]!.Value<double>() == pair.Value),
                "approval stores base field scores and separate goalkeeper/recovery values without reducing them twice");
            check(!member["isArrived"]!.Value<bool>() && RosterDocument.HasReceipt(storedRoster.Value, request.Id),
                "condition-aware approval still writes one atomic receipt without changing attendance");
            check(algorithm == 2
                ? member["positions"]!.Values<int>().SequenceEqual(input.Positions!)
                : member["positions"] == null,
                "only positions rosters store positions; an empty array must not override other algorithms' goalkeeper flag");
            groups.Players = new[] { new RosterPlayer("join_" + request.Id, request.Name) };
            var approved = (await memberships.SummariesAsync("conditions", groups, ct)).Single();
            check(approved.Status == "approved" && approved.Profile!.IsGoalKeeper == keeper
                && approved.Profile.IsRecoveringFromInjury == keeper && approved.Profile.GoalkeeperSkill == expectedScore
                && approved.Profile.Ratings.Values.All(value => value == 7),
                "approved membership exposes the approval-time group conditions and finalized base scores");
            var snapshot = JsonConvert.SerializeObject(approved.Profile);
            member["goalkeeperSkill"] = 2;
            member["isRecoveringFromInjury"] = !keeper;
            foreach (var skill in view.Skills) member[skill.Id] = 2;
            await store.WriteAsync(path, storedRoster.Value, storedRoster.ETag, ct);
            check(JsonConvert.SerializeObject((await memberships.SummariesAsync("conditions", groups, ct)).Single().Profile) == snapshot,
                "later private roster assessments never leak into the player's approval snapshot");
            check(JsonConvert.SerializeObject(await profiles.GetAsync("conditions", ct)) == accountBefore,
                "group conditions and approval never rewrite the global account profile");
        }
    }

    private static async Task RunAlgorithms(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        foreach (var algorithm in new[] { 0, 1, 2, 3 })
        {
            var store = new Store();
            var groups = new Groups();
            groups.Config.SelectedAlgoKey = algorithm;
            var analyzer = new Analyzer();
            var service = new GroupJoinService(store, groups, new Clock(), new GroupJoinRoster(store), analyzer, new Entitlements());
            var token = (await service.LinkAsync("owner", "group", true, ct)).Token!;
            var view = await service.PublicAsync(token, ct);
            check(view.AlgorithmKey == algorithm && view.Skills.Count == (algorithm == 1 ? 1 : algorithm == 3 ? 0 : 3),
                "join contract exposes exactly the selected algorithm's fields");
            var submission = new GroupJoinSubmission(Guid.NewGuid().ToString("D"), "Algorithm player", false,
                "Group-specific description", view.Skills.ToDictionary(skill => skill.Id, _ => 6.0), algorithm,
                algorithm == 2 ? new() { 1, 3 } : new(), view.Skills.ToList());
            await Error(() => service.SubmitAsync(token, submission with { AlgorithmKey = (algorithm + 1) % 4 }, ct), 409, check);
            await Error(() => service.SubmitAsync(token, submission with { Ratings = new(submission.Ratings) { ["extra"] = 7 } }, ct), 400, check);
            if (algorithm == 2)
                foreach (var positions in new List<int>?[] { null, new(), new() { 6 }, new() { 1, 1 } })
                    await Error(() => service.SubmitAsync(token, submission with { Positions = positions }, ct), 400, check);
            else await Error(() => service.SubmitAsync(token, submission with { Positions = new() { 1 } }, ct), 400, check);
            if (algorithm is 0 or 2)
            {
                var previousName = groups.Config.SkillDefinitions[0].Name;
                groups.Config.SkillDefinitions[0].Name = "Changed meaning";
                await Error(() => service.SubmitAsync(token, submission, ct), 409, check);
                groups.Config.SkillDefinitions[0].Name = previousName;
            }
            await service.SubmitAsync(token, submission, ct);
            var request = (await service.RequestsAsync("owner", "group", ct)).Single();
            check(request.SubmittedAlgorithm == algorithm && request.Positions.SequenceEqual(submission.Positions!),
                "request snapshots the algorithm and preferred positions");
            if (algorithm == 3)
            {
                await Error(() => service.AnalyzeAsync("owner", "group", request.Id, request.Revision, ct), 400, check);
                check(analyzer.Calls == 0, "description-only groups never call numeric skill analysis");
            }
            groups.Config.SelectedAlgoKey = (algorithm + 1) % 4;
            await Error(() => service.ApproveAsync("owner", "group", request.Id, new(request.Revision, submission.Ratings), ct), 409, check);
            groups.Config.SelectedAlgoKey = algorithm;
            if (algorithm is 0 or 2)
            {
                var previousName = groups.Config.SkillDefinitions[0].Name;
                groups.Config.SkillDefinitions[0].Name = "New meaning";
                await Error(() => service.ApproveAsync("owner", "group", request.Id, new(request.Revision, submission.Ratings), ct), 409, check);
                groups.Config.SkillDefinitions[0].Name = previousName;
            }
            await service.ApproveAsync("owner", "group", request.Id, new(request.Revision, submission.Ratings), ct);
            var path = store.Documents.Keys.Single(key => !key.StartsWith("group-joins/"));
            var member = RosterDocument.Players((await store.ReadAsync<JToken>(path, ct))!.Value).Single();
            check((algorithm == 2 ? member["positions"]!.Values<int>().SequenceEqual(submission.Positions!) : member["positions"] == null)
                && member["description"]!.Value<string>() == submission.Background,
                "approved group roster retains its own positions and description");
            check(member["attack"] == null && (algorithm != 3 || member["rank"] == null),
                "enrollment never invents unused skills or description-only numeric ratings");
            if (algorithm == 1) check(member["rank"]!.Value<double>() == 6, "rank algorithm preserves the supplied rank");
        }
    }

    public static async Task RunMemberships(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new Store();
        var groups = new Groups { InvitationsEnabled = true };
        var clock = new Clock();
        var joins = new GroupJoinService(store, groups, clock, new GroupJoinRoster(store), new Analyzer(), new Entitlements());
        var memberships = new PlayerMembershipService(store);
        var invitations = new InvitationService(store, groups, clock);
        var profile = new PlayerProfile { Id = PlayerProfileService.Id("player"), Name = "Player",
            Ratings = new() { ["passing"] = 7, ["stamina"] = 5 } };
        var token = (await joins.LinkAsync("owner", "group", true, ct)).Token!;
        var input = new GroupJoinSubmission(Guid.NewGuid().ToString("D"), "Player", false, "",
            new() { ["vision"] = 6, ["passing"] = 7, ["stamina"] = 5 });
        await joins.SubmitAsync(token, input, ct, profile);
        var request = (await joins.RequestsAsync("owner", "group", ct)).Single();
        groups.Players = new[] { new RosterPlayer("join_" + request.Id, "Player"), new RosterPlayer("other", "Player") };
        var invitation = await invitations.CreateAsync("owner", new("group", "2026-10-20T15:00:00Z", "UTC"), ct);
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == null,
            "pending request and duplicate display name do not establish identity");
        await Error(() => invitations.RespondOwnAsync("player", invitation.Token, new("in"), ct), 403, check);
        store.FailMembership = true;
        await Error(() => joins.ApproveAsync("owner", "group", request.Id, new(request.Revision, input.Ratings), ct), 409, check);
        check((await joins.RequestsAsync("owner", "group", ct)).Single().Status == "approving"
            && (await memberships.ListAsync("player", ct)).All(item => item.PlayerKeys.Count == 0),
            "failed membership write keeps approval resumable rather than reporting success");
        store.FailMembership = false;
        await joins.ApproveAsync("owner", "group", request.Id, new(0, new()), ct);
        var own = await invitations.OwnAsync("player", invitation.Token, ct);
        check(own.PlayerKey == "join_" + request.Id && (await memberships.ListAsync("player", ct)).Single().PlayerKeys.Count == 1,
            "retried approval records the verified membership once after atomic roster receipt");
        check((await memberships.ListAsync("stranger", ct)).Count == 0
            && (await invitations.OwnAsync("stranger", invitation.Token, ct)).PlayerKey == null,
            "memberships and self identity are isolated to the authenticated account");
        var beforeRead = JsonConvert.SerializeObject(store.Documents);
        await invitations.OwnAsync("player", invitation.Token, ct);
        check(beforeRead == JsonConvert.SerializeObject(store.Documents), "identity GET never writes RSVP, membership or profile");
        await invitations.RespondAsync(invitation.Token, new("other", "in"), ct);
        var waiting = await invitations.RespondOwnAsync("player", invitation.Token, new("in"), ct);
        check(waiting.Players.Single(p => p.Key == own.PlayerKey).WaitingListPosition == 1,
            "authenticated RSVP shares existing capacity and FIFO waiting list");
        await invitations.RespondAsync(invitation.Token, new("other", "out"), ct);
        var promoted = (await invitations.OwnAsync("player", invitation.Token, ct)).Invitation;
        check(promoted.Players.Single(p => p.Key == own.PlayerKey).WaitingListPosition == null
            && promoted.Counts.In == 1, "authenticated waiting player is promoted by existing withdrawal logic");
        var countBeforeNoop = store.Documents.Count;
        await invitations.RespondOwnAsync("player", invitation.Token, new("in"), ct);
        check(store.Documents.Count == countBeforeNoop, "repeat self response creates no new documents");
        await Error(() => invitations.RespondOwnAsync("player", invitation.Token, new("maybe"), ct), 400, check);
        await Error(() => invitations.OwnAsync("player", "bad-token", ct), 404, check);
        groups.InvitationsEnabled = false;
        await Error(() => invitations.RespondOwnAsync("player", invitation.Token, new("out"), ct), 409, check);
        groups.InvitationsEnabled = true;
        store.BeforeInvitationConflict = () => groups.Players = new[] { new RosterPlayer("other", "Player") };
        await Error(() => invitations.RespondOwnAsync("player", invitation.Token, new("out"), ct), 403, check);
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == null
            && (await memberships.ListAsync("player", ct)).Single().PlayerKeys.Contains(own.PlayerKey!),
            "CAS retries recheck current roster; removed player's history link remains but grants no current identity");
        var anotherGroup = (await groups.LoadAsync("another-owner", "group", ct)) with
            { Players = new[] { new RosterPlayer(own.PlayerKey!, "Player") } };
        check(await memberships.ResolveAsync("player", anotherGroup, ct) == null,
            "same group ID and roster key under a different owner cannot leak a membership");
        groups.Players = new[] { new RosterPlayer(own.PlayerKey!, "Renamed player") };
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == own.PlayerKey,
            "organizer renaming cannot break or transfer the stable identity");
        var indexKey = store.Documents.Keys.Single(key => key.StartsWith("player-memberships/"));
        store.Documents.Remove(indexKey);
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == own.PlayerKey,
            "older retained approved profile request plus roster receipt resolves safely without global scanning");
        await joins.ApproveAsync("owner", "group", request.Id, new(0, new()), ct);
        var stateKey = store.Documents.Keys.Single(key => key.StartsWith("group-joins/v1/groups/"));
        var storedState = (await store.ReadAsync<GroupJoinState>(stateKey, ct))!;
        storedState.Value.Requests.Clear();
        await store.WriteAsync(stateKey, storedState.Value, storedState.ETag, ct);
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == own.PlayerKey,
            "durable account link survives cleanup of old request history");
        await Error(() => joins.SubmitAsync(token, input with { SubmissionId = Guid.NewGuid().ToString("D"), Name = "Duplicate" },
            ct, profile), 409, check);
        groups.Players = Array.Empty<RosterPlayer>();
        await joins.SubmitAsync(token, input with { SubmissionId = Guid.NewGuid().ToString("D"), Name = "Player rejoined" }, ct, profile);
        var rejoined = (await joins.RequestsAsync("owner", "group", ct)).Single();
        await joins.ApproveAsync("owner", "group", rejoined.Id, new(rejoined.Revision, input.Ratings), ct);
        groups.Players = new[] { new RosterPlayer(own.PlayerKey!, "Player"), new RosterPlayer("join_" + rejoined.Id, "Player rejoined") };
        await Error(() => invitations.OwnAsync("player", invitation.Token, ct), 409, check);
        check((await memberships.ListAsync("player", ct)).Single().PlayerKeys.Count == 2,
            "rejoining retains both historical keys and never guesses between two current entries");
        groups.Players = groups.Players.Skip(1).ToArray();
        check((await invitations.OwnAsync("player", invitation.Token, ct)).PlayerKey == "join_" + rejoined.Id,
            "only current rejoined key is selected after old entry is removed");
        await joins.SubmitAsync(token, input with { SubmissionId = Guid.NewGuid().ToString("D"), Name = "Rejected player" },
            ct, profile with { Id = PlayerProfileService.Id("rejected") });
        var rejected = (await joins.RequestsAsync("owner", "group", ct)).Single(item => item.Name == "Rejected player");
        await joins.RejectAsync("owner", "group", rejected.Id, rejected.Revision, ct);
        groups.Players = new[] { new RosterPlayer("join_" + rejected.Id, "Rejected player") };
        check((await invitations.OwnAsync("rejected", invitation.Token, ct)).PlayerKey == null,
            "rejected request and matching roster key without receipt cannot establish membership");
        check(JObject.FromObject(new OwnInvitationResponseRequest("in")).Properties().Select(p => p.Name)
            .SequenceEqual(new[] { "Status" }), "self-response body has no client-selected account, owner, group or player key");
        foreach (var method in new[] { nameof(InvitationsController.Own), nameof(InvitationsController.RespondOwn) })
            check(!typeof(InvitationsController).GetMethod(method)!.IsDefined(typeof(AllowAnonymousAttribute), true),
                "self invitation route retains controller authorization");
    }

    public static async Task RunDualMode(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new Store();
        var groups = new Groups { InvitationsEnabled = true, Maximum = 0 };
        var clock = new Clock();
        var joins = new GroupJoinService(store, groups, clock, new GroupJoinRoster(store), new Analyzer(), new Entitlements());
        var memberships = new PlayerMembershipService(store);
        var profiles = new PlayerProfileService(store, clock);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "dual"),
            new Claim("firebase", """{"sign_in_provider":"password"}""") }, "firebase"));
        var profile = await profiles.EnsureAsync(user, ct);
        profile = await profiles.SetModeAsync(user, new(profile.Revision, "organizer"), ct);
        var before = JsonConvert.SerializeObject(profile);
        var token = (await joins.LinkAsync("owner", "group", true, ct)).Token!;
        var ratings = new Dictionary<string, double> { ["vision"] = 3, ["passing"] = 4, ["stamina"] = 5 };
        var input = new ProfileGroupJoinSubmission(Guid.NewGuid().ToString("D"), profile.Revision, ratings, Name: "Roster alias");
        await Error(() => joins.SubmitProfileAsync("dual", token, input with { Name = null }, ct), 400, check);
        await joins.SubmitProfileAsync("dual", token, input, ct);
        var pending = (await memberships.SummariesAsync("dual", groups, ct)).Single();
        check(pending.Status == "pending" && pending.Profile!.Name == "Roster alias" && pending.Profile.Ratings["vision"] == 3,
            "optional global identity does not block an existing organizer's group-specific pending request");
        check(JsonConvert.SerializeObject(await profiles.GetAsync("dual", ct)) == before,
            "joining does not change optional profile fields, organizer preference, identity or account data");
        check((await memberships.SummariesAsync("stranger", groups, ct)).Count == 0,
            "membership status and submitted skills are account-isolated");
        var beforeReads = JsonConvert.SerializeObject(store.Documents);
        await memberships.SummariesAsync("dual", groups, ct);
        check(beforeReads == JsonConvert.SerializeObject(store.Documents), "membership GET is strictly read-only");
        await Error(() => joins.SubmitProfileAsync("dual", token, input with { SubmissionId = Guid.NewGuid().ToString("D") }, ct), 409, check);
        var request = (await joins.RequestsAsync("owner", "group", ct)).Single();
        var final = new Dictionary<string, double>(ratings) { ["vision"] = 9 };
        await joins.ApproveAsync("owner", "group", request.Id, new(request.Revision, final), ct);
        groups.Players = new[] { new RosterPlayer("join_" + request.Id, "Roster alias") };
        var approved = (await memberships.SummariesAsync("dual", groups, ct)).Single();
        check(approved.Status == "approved" && approved.Profile!.Ratings["vision"] == 9
            && approved.Profile.Skills.Count == 3, "Premium unlimited quota accepts enrollment and exposes only that group's approved skills");
        check(!JsonConvert.SerializeObject(approved).Contains("ProfileId")
            && !JsonConvert.SerializeObject(approved).Contains("ReviewedBy"),
            "membership DTO does not expose account identifiers or private review information");
        await Error(() => joins.SubmitProfileAsync("dual", token, input with { SubmissionId = Guid.NewGuid().ToString("D") }, ct), 409, check);
        var invitations = new InvitationService(store, groups, clock);
        var invitation = await invitations.CreateAsync("owner", new("group", "2026-10-20T15:00:00Z", "UTC"), ct);
        await Error(() => invitations.RespondAsync(invitation.Token, new("join_" + request.Id, "in"), ct), 403, check);
        await invitations.RespondOwnAsync("dual", invitation.Token, new("in"), ct);
        check((await invitations.OwnAsync("dual", invitation.Token, ct)).Invitation.Counts.In == 1,
            "only the linked account can RSVP to its current roster entry");
        var bindingKey = store.Documents.Keys.Single(key => key.StartsWith("player-membership-links/"));
        store.Documents.Remove(bindingKey);
        await Error(() => invitations.RespondAsync(invitation.Token, new("join_" + request.Id, "out"), ct), 403, check);
        groups.GroupId = "second";
        groups.Config.SelectedAlgoKey = 1;
        groups.Players = Array.Empty<RosterPlayer>();
        var secondToken = (await joins.LinkAsync("owner", "second", true, ct)).Token!;
        await joins.SubmitProfileAsync("dual", secondToken, input with
            { AlgorithmKey = 1, Ratings = new() { ["rank"] = 2 } }, ct);
        var secondRequest = (await joins.RequestsAsync("owner", "second", ct)).Single();
        await joins.RejectAsync("owner", "second", secondRequest.Id, secondRequest.Revision, ct);
        var second = (await memberships.SummariesAsync("dual", groups, ct)).Single(item => item.GroupKey != approved.GroupKey);
        check(second.Status == "rejected" && second.Profile!.AlgorithmKey == 1 && second.Profile.Ratings.Count == 1
            && second.Profile.Ratings["rank"] == 2, "a second group's independent algorithm and rejection never inherit first-group skills");
        var firstIndex = store.Documents.Keys.Single(key => key.StartsWith("player-memberships/") && key.EndsWith(approved.GroupKey));
        store.Documents.Remove(firstIndex);
        var migration = new PlayerMembershipBackfill(store);
        var beforeDryRun = JsonConvert.SerializeObject(store.Documents);
        var dryRun = await migration.RunAsync();
        check(!dryRun.Applied && dryRun.RequestAccounts == 2 && dryRun.ApprovedRequests == 1
            && beforeDryRun == JsonConvert.SerializeObject(store.Documents), "migration defaults to read-only dry run");
        await migration.RunAsync(true);
        check(store.Documents.ContainsKey(firstIndex) && store.Documents.ContainsKey(bindingKey),
            "additive backfill restores legacy account discovery, approved snapshots and identity bindings");
        var afterBackfill = JsonConvert.SerializeObject(store.Documents);
        await migration.RunAsync(true);
        check(afterBackfill == JsonConvert.SerializeObject(store.Documents), "repeated backfill is idempotent without rewriting accounts or rosters");
        var secondStateKey = "group-joins/v1/groups/" + InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { "owner", "second" }));
        var secondState = (await store.ReadAsync<GroupJoinState>(secondStateKey, ct))!;
        secondState.Value.Requests.Clear();
        await store.WriteAsync(secondStateKey, secondState.Value, secondState.ETag, ct);
        check((await memberships.SummariesAsync("dual", groups, ct)).Single(item => item.GroupKey == second.GroupKey).Status == "rejected",
            "request cleanup preserves the applicant's final rejection status");
        groups.GroupId = "group";
        groups.Config.SelectedAlgoKey = 0;
        groups.Players = new[] { new RosterPlayer("join_" + request.Id, "Roster alias") };
        var firstStateKey = "group-joins/v1/groups/" + InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { "owner", "group" }));
        var firstState = (await store.ReadAsync<GroupJoinState>(firstStateKey, ct))!;
        firstState.Value.Requests.Clear();
        await store.WriteAsync(firstStateKey, firstState.Value, firstState.ETag, ct);
        await Error(() => invitations.RespondAsync(invitation.Token, new("join_" + request.Id, "out"), ct), 403, check);
        await invitations.RespondOwnAsync("dual", invitation.Token, new("out"), ct);
        groups.Players = Array.Empty<RosterPlayer>();
        check((await memberships.SummariesAsync("dual", groups, ct)).Single(item => item.GroupKey == approved.GroupKey).Status == "removed",
            "historical membership and skills never imply active membership after roster removal");
    }

    private static async Task Error(Func<Task> operation, int expected, Action<bool, string> check)
    {
        try { await operation(); throw new InvalidOperationException($"Expected status {expected}."); }
        catch (InvitationException error) { check(error.StatusCode == expected, $"expected {expected}, got {error.StatusCode}: {error.Message}"); }
    }
    private sealed class Clock : IInvitationClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-10-15T15:00:00Z"); }
    private sealed class Entitlements : IAccountEntitlementService
    {
        public HashSet<string> Free { get; } = new(StringComparer.Ordinal);
        public Task<AccountEntitlements> GetAsync(string userId, string? verifiedEmail = null,
            CancellationToken cancellationToken = default, bool? paidActive = null) =>
            Task.FromResult(new AccountEntitlements { IsPremium = !Free.Contains(userId) });
    }
    private sealed class Groups : IInvitationGroupSource
    {
        public string GroupId = "group";
        public bool Enabled = true, Deny;
        public bool InvitationsEnabled;
        public IReadOnlyList<RosterPlayer> Players = Array.Empty<RosterPlayer>();
        public int Maximum = 10;
        public UserConfigResponse Config = new() { SkillDefinitions = Skills(), MatchdayRules = new() { NoSlideTackles = true } };
        public static List<SkillDefinition> Skills() => new() {
            new() { Id = "vision", Name = "Vision" }, new() { Id = "passing", Name = "Passing" }, new() { Id = "stamina", Name = "Stamina" } };
        public Task<InvitationGroup> ResolveAsync(string user, string group, CancellationToken ct)
        {
            if (Deny || user is not ("owner" or "collaborator")) throw new InvitationException(403, "Access denied.");
            return LoadAsync("owner", GroupId, ct);
        }
        public Task<InvitationGroup> LoadAsync(string owner, string group, CancellationToken ct) => Task.FromResult(
            new InvitationGroup(owner, group, "Squad", "Weekly match", "Pitch", null, 1, Players, InvitationsEnabled, "he")
            { Configuration = Config, MaximumPlayers = Maximum, GroupJoinRequestsEnabled = Enabled });
    }
    private sealed class Analyzer : IGroupJoinAnalyzer
    {
        public int Calls;
        public object? Context;
        public Action? BeforeReturn;
        public Task<List<GroupJoinSuggestion>> AnalyzeAsync(GroupJoinRequest request, object groupContext, string language, CancellationToken ct)
        {
            Calls++; Context = groupContext; BeforeReturn?.Invoke();
            return Task.FromResult(request.Skills.Select(skill => new GroupJoinSuggestion(skill.Id, null, "Insufficient evidence.", true)).ToList());
        }
    }
    private sealed class Store : IInvitationDocumentStore
    {
        public readonly Dictionary<string, (string Json, string ETag)> Documents = new();
        public bool FailFinalization;
        public bool YieldBeforeWrites;
        public bool FailMembership;
        public Action? BeforeInvitationConflict;
        private int _revision;
        public Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken ct)
        {
            lock (Documents) return Task.FromResult(Documents.TryGetValue(key, out var value)
                ? new StoredDocument<T>(JsonConvert.DeserializeObject<T>(value.Json)!, value.ETag) : null);
        }
        public async Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken ct)
        {
            if (YieldBeforeWrites) await Task.Yield();
            lock (Documents)
            {
                if (FailMembership && key.StartsWith("player-memberships/")) return false;
                if (BeforeInvitationConflict != null && value is InvitationState)
                {
                    var callback = BeforeInvitationConflict;
                    BeforeInvitationConflict = null;
                    callback();
                    return false;
                }
                if (FailFinalization && value is GroupJoinState state && state.Requests.Any(r => r.Name == "Retry player" && r.Status == "approved"))
                    return false;
                if (Documents.TryGetValue(key, out var current) ? current.ETag != etag : etag != null) return false;
                Documents[key] = (JsonConvert.SerializeObject(value), (++_revision).ToString());
                return true;
            }
        }
        public Task<DocumentPage> ListAsync(string prefix, string? continuation, int pageSize, CancellationToken ct)
        {
            var offset = continuation == null ? 0 : int.Parse(continuation);
            var keys = Documents.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
            return Task.FromResult(new DocumentPage(keys.Skip(offset).Take(pageSize).ToArray(),
                offset + pageSize < keys.Length ? (offset + pageSize).ToString() : null));
        }
    }
}
