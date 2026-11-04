using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using System.Text;

namespace TeamsGeneratorWebAPI.Polls;

public sealed class TablePollStore
{
    private readonly TableClient table;
    private readonly TimeProvider clock;
    private const int Attempts = 12;

    public TablePollStore(TableServiceClient tables, TimeProvider clock)
    {
        table = tables.GetTableClient("MatchdayPolls");
        table.CreateIfNotExists();
        this.clock = clock;
    }

    internal TablePollStore(TableClient table, TimeProvider clock)
    {
        this.table = table;
        this.clock = clock;
    }

    public async Task<string> VoterAsync(string? existing, CancellationToken ct)
    {
        if (existing != null)
        {
            await ValidateVoterAsync(existing, ct);
            return existing;
        }
        var token = PollKeys.NewSecret();
        var hash = PollKeys.Hash(token);
        await table.AddEntityAsync(new TableEntity("voters-" + hash[..2], hash), ct);
        return token;
    }

    public async Task ValidateVoterAsync(string token, CancellationToken ct)
    {
        if (!PollKeys.IsSecret(token))
            throw new PollException(401, "invalid_voter", "Obtain a valid anonymous voter token before voting.");
        var hash = PollKeys.Hash(token);
        if (!(await table.GetEntityIfExistsAsync<TableEntity>("voters-" + hash[..2], hash, cancellationToken: ct)).HasValue)
            throw new PollException(401, "invalid_voter", "Obtain a valid anonymous voter token before voting.");
    }

    public async Task<PollEntity?> FindAsync(string matchdayKey, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<PollEntity>(PollKeys.Partition(matchdayKey), "poll", cancellationToken: ct);
        return response.HasValue ? response.Value : null;
    }

    public async Task<PollEntity> CreateAsync(string matchdayKey, IReadOnlyList<PollCandidate> candidates, CancellationToken ct)
    {
        var partition = PollKeys.Partition(matchdayKey);
        var json = JsonConvert.SerializeObject(candidates.Select(c => new PollTally { Key = c.Key, Name = c.Name }));
        // Leave room for growing counters within Azure Table's 64 KiB string-property limit.
        if (candidates.Count > 200 || Encoding.Unicode.GetByteCount(json) > 48000)
            throw new PollException(400, "too_many_candidates", "The recorded participant list is too large for a poll.");
        var entity = new PollEntity { PartitionKey = partition, MatchdayKey = matchdayKey,
            Token = partition[4..] + "." + PollKeys.NewSecret(), TalliesJson = json, ClosesAt = clock.GetUtcNow().AddHours(48) };
        try { await table.AddEntityAsync(entity, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        return await FindAsync(matchdayKey, ct)
            ?? throw new InvalidOperationException("The persisted poll is missing.");
    }

    private async Task<PollEntity> ReadTokenAsync(string token, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<PollEntity>(PollKeys.TokenPartition(token), "poll", cancellationToken: ct);
        if (!response.HasValue || response.Value.Token != token)
            throw new PollException(404, "poll_not_found", "The poll was not found.");
        return response.Value;
    }

    public async Task<PollSnapshot> ReadAsync(string token, string? voter, bool close, CancellationToken ct)
    {
        if (voter != null) await ValidateVoterAsync(voter, ct);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var poll = await ReadTokenAsync(token, ct);
            if (!poll.Closed && (close || poll.ClosesAt <= clock.GetUtcNow()))
            {
                poll.Closed = true;
                try { await table.UpdateEntityAsync(poll, poll.ETag, TableUpdateMode.Replace, ct); }
                catch (RequestFailedException ex) when (ex.Status == 412) { continue; }
            }
            var voted = voter != null && (await table.GetEntityIfExistsAsync<PollBallotEntity>(
                poll.PartitionKey, "ballot-" + PollKeys.Hash(voter), cancellationToken: ct)).HasValue;
            return Snapshot(poll, voted);
        }
        throw Contention();
    }

    public async Task<PollSnapshot> VoteAsync(string token, string voter, PollBallotRequest ballot, CancellationToken ct)
    {
        await ValidateVoterAsync(voter, ct);
        var row = "ballot-" + PollKeys.Hash(voter);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var poll = await ReadTokenAsync(token, ct);
            if (poll.Closed || poll.ClosesAt <= clock.GetUtcNow())
            {
                await ReadAsync(token, null, false, ct);
                throw new PollException(409, "poll_closed", "This poll is closed.");
            }

            var tallies = JsonConvert.DeserializeObject<List<PollTally>>(poll.TalliesJson)!;
            var first = tallies.SingleOrDefault(c => c.Key == ballot.FirstChoiceKey);
            var second = tallies.SingleOrDefault(c => c.Key == ballot.SecondChoiceKey);
            if (first == null || second == null || first == second)
                throw new PollException(400, "invalid_ballot", "Choose exactly two distinct recorded participants.");
            if ((await table.GetEntityIfExistsAsync<PollBallotEntity>(poll.PartitionKey, row, cancellationToken: ct)).HasValue)
                throw new PollException(409, "already_voted", "This browser has already voted in this poll.");
            checked
            {
                first.FirstChoiceVotes++;
                second.SecondChoiceVotes++;
                poll.BallotCount++;
            }
            poll.TalliesJson = JsonConvert.SerializeObject(tallies);
            try
            {
                // Every ballot and close competes on the same header ETag. The unique ballot row
                // and aggregate update commit together, including retries after ambiguous responses.
                await table.SubmitTransactionAsync(new[]
                {
                    new TableTransactionAction(TableTransactionActionType.UpdateReplace, poll, poll.ETag),
                    new TableTransactionAction(TableTransactionActionType.Add, new PollBallotEntity
                    {
                        PartitionKey = poll.PartitionKey, RowKey = row,
                        FirstChoiceKey = first.Key, SecondChoiceKey = second.Key
                    })
                }, ct);
                return await ReadAsync(token, voter, false, ct);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412) { }
        }
        throw Contention();
    }

    public async Task SaveEventAsync(string token, PollEventDetails details, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(details.Validate());
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var poll = await ReadTokenAsync(token, ct);
            // A poll's event is immutable; re-sharing must never replace it with next week's settings.
            if (poll.EventDetailsJson != null) return;
            poll.EventDetailsJson = json;
            try { await table.UpdateEntityAsync(poll, poll.ETag, TableUpdateMode.Replace, ct); return; }
            catch (RequestFailedException ex) when (ex.Status == 412) { }
        }
        throw Contention();
    }

    public static PollSnapshot Snapshot(PollEntity poll, bool voted)
    {
        var tallies = JsonConvert.DeserializeObject<List<PollTally>>(poll.TalliesJson)!;
        List<PollResult>? results = null;
        if (poll.Closed)
        {
            results = new();
            var ordered = tallies.OrderByDescending(c => checked(c.FirstChoiceVotes * 5 + c.SecondChoiceVotes * 3))
                .ThenByDescending(c => c.FirstChoiceVotes).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var c = ordered[i];
                var points = checked(c.FirstChoiceVotes * 5 + c.SecondChoiceVotes * 3);
                var rank = i > 0 && results[i - 1].Points == points && results[i - 1].FirstChoiceVotes == c.FirstChoiceVotes
                    ? results[i - 1].Rank : i + 1;
                results.Add(new(c.Key, c.Name, c.FirstChoiceVotes, c.SecondChoiceVotes, points, rank));
            }
        }
        return new(poll.Token, poll.MatchdayKey, poll.Closed ? "closed" : "open", poll.ClosesAt,
            tallies.Select(c => new PollCandidate(c.Key, c.Name)).ToArray(), voted,
            poll.Closed ? poll.BallotCount : null, results,
            EventDetails: poll.EventDetailsJson == null ? null : JsonConvert.DeserializeObject<PollEventDetails>(poll.EventDetailsJson));
    }

    private static PollException Contention() => new(503, "poll_busy", "The poll is busy. Please retry.");
}
