using Newtonsoft.Json.Linq;
using System.Text.Json.Serialization;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public sealed class PlayerGroup
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsDefault { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public bool IsShared { get; set; }

        public bool CanManage { get; set; } = true;

        [JsonIgnore]
        internal bool IsPersisted { get; set; }
    }

    public sealed class PlayerGroupsResponse
    {
        public bool Success { get; set; }

        public IReadOnlyList<PlayerGroup> Groups { get; set; } =
            Array.Empty<PlayerGroup>();

        public string Error { get; set; }

        internal static PlayerGroupsResponse Succeeded(
            IReadOnlyList<PlayerGroup> groups)
        {
            return new PlayerGroupsResponse
            {
                Success = true,
                Groups = groups
            };
        }

        internal static PlayerGroupsResponse Failure(string error)
        {
            return new PlayerGroupsResponse
            {
                Success = false,
                Error = error
            };
        }
    }

    public sealed class PlayerGroupMutationResponse
    {
        public bool Success { get; set; }

        public PlayerGroup Group { get; set; }

        public string Error { get; set; }

        internal static PlayerGroupMutationResponse Succeeded(
            PlayerGroup group)
        {
            return new PlayerGroupMutationResponse
            {
                Success = true,
                Group = group
            };
        }

        internal static PlayerGroupMutationResponse Failure(string error)
        {
            return new PlayerGroupMutationResponse
            {
                Success = false,
                Error = error
            };
        }
    }

    public sealed class CreatePlayerGroupRequest
    {
        public string Name { get; set; }
    }

    public sealed class RenamePlayerGroupRequest
    {
        public string Name { get; set; }
    }

    public sealed class MovePlayerRequest
    {
        public string FromGroupId { get; set; }

        public string ToGroupId { get; set; }

        public JObject Player { get; set; }
    }
}
