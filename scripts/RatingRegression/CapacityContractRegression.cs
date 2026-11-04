using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Capacity;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

internal static class CapacityContractRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var accounts = CallbackProxy.Create<IPremiumAccountStore>((method, args) =>
            method.Name == "IsPremiumAsync"
                ? Task.FromResult((string)args[0]! == "premium")
                : throw new InvalidOperationException("Unexpected entitlement operation."));
        var service = new AccountEntitlementService(new ConfigurationBuilder().Build(), accounts);
        var free = await service.GetAsync("free");
        var premium = await service.GetAsync("premium");
        check(free.MaximumOwnedGroups == 2 && free.MaximumPlayers == 25
            && free.MaximumGenerationPlayers == 25,
            "Free contract retains existing storage limits and adds only generation capacity");
        check(premium.MaximumOwnedGroups == 0 && premium.MaximumPlayers == 0
            && premium.MaximumGenerationPlayers == 50,
            "Premium retains zero-as-unlimited storage entitlements independently of generation capacity");
        check(free.GenerationRequestsPerMinute == 20 && premium.GenerationRequestsPerMinute == 20
            && free.MaximumConcurrentGenerations == 1 && premium.MaximumConcurrentGenerations == 1,
            "generation burst and simultaneous limits do not imply an AI summary quota");
        check(!free.CanUseAiSummary && premium.CanUseAiSummary,
            "capacity metadata does not change AI entitlement");
        var options = new CapacityOptions
        {
            PremiumGenerationPlayers = 40,
            GenerationRequestsPerMinute = 12
        };
        service = new AccountEntitlementService(new ConfigurationBuilder().Build(), accounts,
            Options.Create(options));
        premium = await service.GetAsync("premium");
        check(premium.MaximumOwnedGroups == 0 && premium.MaximumPlayers == 0
            && premium.MaximumGenerationPlayers == 40
            && premium.GenerationRequestsPerMinute == 12,
            "runtime configuration changes generation metadata without introducing storage quotas");
        check(typeof(PremiumEntitlementsResponse).GetProperty("MaximumTotalPlayers") == null
            && typeof(GetAppSetupResponse).GetProperty("GroupCapacity") == null,
            "setup does not publish removed account storage quotas or owner quota metadata");
        check(!typeof(PremiumEntitlementsResponse).GetProperties().Any(property =>
                property.Name.Contains("Quota", StringComparison.OrdinalIgnoreCase)
                && property.Name.Contains("Ai", StringComparison.OrdinalIgnoreCase)),
            "setup response does not expose AI quotas");

        var uploads = 0;
        var storage = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
        {
            if (method.Name != "UploadAsync") throw new InvalidOperationException(method.Name);
            uploads++;
            return Task.FromResult<IResponse>(new SavePlayersResponse());
        });
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            Task.FromResult<GroupAccess?>(new GroupAccess
                { OwnerId = (string)args[0]!, GroupId = "default", IsOwner = true }));
        var controller = new UserPlayersController(NullLogger<UserPlayersController>.Instance,
            storage, CallbackProxy.Create<IUsageTelemetry>((_, _) => null), service, collaboration, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        JArray Roster(int count) => new(Enumerable.Range(0, count).Select(index =>
            new JObject { ["key"] = "p" + index, ["name"] = "Player " + index }));
        check(await controller.Post("test", Roster(25), "free", 0) is OkObjectResult
            && uploads == 1, "existing Free upload boundary of 25 still succeeds");
        check(await controller.Post("test", Roster(26), "free", 0) is BadRequestObjectResult
            && uploads == 1, "existing Free upload limit still rejects 26 before persistence");
        check(await controller.Post("test", Roster(101), "premium", 0) is OkObjectResult
            && uploads == 2, "Premium uploads are not subject to the removed 100-player storage quota");
        check(await controller.Post("test", Roster(1001), "premium", 0) is OkObjectResult
            && uploads == 3, "Premium uploads are not subject to the removed 1000-player account quota");
    }
}
