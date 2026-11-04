using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Polls;

namespace TeamsGeneratorWebAPI.Controllers;

[ServiceFilter(typeof(PollExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PollsController(PollService service, TablePollStore store, PollPublicRateLimiter limiter) : ControllerBase
{
    [HttpPost("/api/matchday-polls/{matchdayKey}")]
    public async Task<IActionResult> Create(string matchdayKey) =>
        Ok(await service.OrganizerAsync(matchdayKey, User, true, false, HttpContext.RequestAborted));

    [HttpGet("/api/matchday-polls/{matchdayKey}")]
    public async Task<IActionResult> OrganizerRead(string matchdayKey) =>
        Ok(await service.OrganizerAsync(matchdayKey, User, false, false, HttpContext.RequestAborted));

    [HttpPost("/api/matchday-polls/{matchdayKey}/close")]
    public async Task<IActionResult> Close(string matchdayKey) =>
        Ok(await service.OrganizerAsync(matchdayKey, User, false, true, HttpContext.RequestAborted));

    [HttpPost("/api/matchday-polls/{matchdayKey}/event")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> SaveEvent(string matchdayKey, [FromBody] PollEventDetails? details) =>
        Ok(await service.SaveEventAsync(matchdayKey, User, details, HttpContext.RequestAborted));

    [HttpPost("/api/polls/voter")]
    public async Task<IActionResult> Voter()
    {
        Limit(true, true);
        return Ok(new { voterToken = await store.VoterAsync(VoterHeader(), HttpContext.RequestAborted) });
    }

    [HttpGet("/api/polls/{token}")]
    public async Task<IActionResult> Read(string token)
    {
        Limit(false);
        return Ok(await store.ReadAsync(token, VoterHeader(), false, HttpContext.RequestAborted));
    }

    [HttpPost("/api/polls/{token}/ballots")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Vote(string token, [FromBody] PollBallotRequest? ballot)
    {
        Limit(true);
        if (ballot == null) throw new PollException(400, "invalid_ballot", "Choose exactly two distinct recorded participants.");
        return Ok(await store.VoteAsync(token, VoterHeader() ?? "", ballot, HttpContext.RequestAborted));
    }

    private string? VoterHeader()
    {
        if (!Request.Headers.TryGetValue("X-Teamify-Voter", out var value)) return null;
        if (value.Count != 1 || !PollKeys.IsSecret(value[0]))
            throw new PollException(401, "invalid_voter", "Obtain a valid anonymous voter token before voting.");
        return value[0];
    }
    private void Limit(bool write, bool issuing = false) =>
        limiter.Check(HttpContext.Connection.RemoteIpAddress?.ToString(), write, issuing);
}
