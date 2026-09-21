using Newtonsoft.Json;
using TeamsGenerator.API;
using TeamsGenerator.Algos.PositionsAlgo;

namespace TeamsGenerator.Orchestration.Contracts
{
    public abstract class PlayerRatingAssessment
    {
        private double? _goalkeeperSkill;

        [EditableInClient(Show = false)]
        public double? GoalkeeperSkill
        {
            get => _goalkeeperSkill;
            set => _goalkeeperSkill = PlayerRatings.NormalizeGoalkeeperSkill(value);
        }

        [EditableInClient(Show = false)]
        public bool IsRecoveringFromInjury { get; set; }

        [JsonIgnore]
        [EditableInClient(Show = false)]
        public abstract double EffectiveRank { get; }

        protected double EffectiveValue(double baseValue) =>
            Math.Max(1, PlayerRatings.Clamp(baseValue)
                - (IsRecoveringFromInjury ? 0.5 : 0));
    }

    public static class PlayerRatings
    {
        public static double? NormalizeGoalkeeperSkill(double? value) =>
            value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 1, 10) : null;

        public static double Clamp(double value) =>
            double.IsFinite(value) ? Math.Clamp(value, 1, 10) : SkillDefinition.DefaultValue;

        public static double GetRank(IPlayer player) =>
            player is PlayerRatingAssessment assessment
                ? assessment.EffectiveRank
                : player.Rank;

        public static bool IsGoalkeeper(IPlayer player) =>
            player is IGoalKeeperSupport goalkeeper && goalkeeper.IsGoalKeeper
            || player is PositionsPlayer positions && positions.Positions?.Contains(Position.GK) == true;

        public static bool HasGoalkeeperRating(IPlayer player) =>
            IsGoalkeeper(player)
            && (player is not PositionsPlayer positions || positions.Positions.Count == 1)
            && player is PlayerRatingAssessment assessment
            && assessment.GoalkeeperSkill.HasValue;
    }
}
