namespace TeamsGenerator.Orchestration.Contracts
{
    public interface IGoalKeeperSupport : IPlayer
    {
        bool IsGoalKeeper { get; set; }
    }
}
