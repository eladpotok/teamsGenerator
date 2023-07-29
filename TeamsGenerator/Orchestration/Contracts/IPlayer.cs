namespace TeamsGenerator.Orchestration.Contracts
{
    public interface IPlayer
    {
        string Name { get; }
        double Rank { get; }
        string Key { get; }
        string Id { get; }
        string ModifyTime { get; }
        bool IsArrived { get; }
        bool IsGoalKeeper { get; }
    }
}
