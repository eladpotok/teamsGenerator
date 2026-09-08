using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.API
{
    public class PremiumEntitlementsResponse
    {
        public bool IsPremium { get; set; }
        public bool CanUseAiSummary { get; set; }
        public bool CanUseChemistry { get; set; }
        public bool CanUseAiAlgorithm { get; set; }
        public int MaximumPlayers { get; set; }
    }

    public class GetAppSetupResponse
    {
        public UserConfigResponse Config { get; set; }
        public List<WebAppAlgoInfo> Algos { get; set; }
        public PremiumEntitlementsResponse Entitlements { get; set; }
        public string ServerVersion => "1.0.0";
        public string ClientVersion => "8.0.0";
    }
}
