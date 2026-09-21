# Teamify
Teamify creates balanced teams for soccer and other sports using multiple team-generation algorithms.


# Algorithms Explanation

## Back And Forth Algorithm:
Set randomly an order of the teams. The first team pick a player, the second team pick the next player, the third team pick two players.
After this round, it goes back. After the third team pick two players, then the second team pick a player, and then the first team pick two players. and so on.
The team picks a player according to the rank order.
Here is an example of the algorithm selection:

![BackToForthAlgo-Explanation](https://user-images.githubusercontent.com/32292032/227537484-b3272fe0-9454-491f-8dba-d8332f3b43c8.png)


## Skill Wise Algorithm:
I believe that variety of teams should be picked by uniform distrubtion according to skills.
Here we define player by 5 skills: **Leadership** (field domination, increase the moral), **Attack** (finishing, key-pass), **Defence** (close to the goalkeeper, physics, aggression), **Stamina** (fit, pace, speed) and **Passing** (passing accuracy, playmaker).

The teams are ordered randomly, and then the algo picks randomly one of the skills above.
Now we pick top 3 players with the highest rank in the specific skill, and spread them in each team.

Note:
While picking 3 players ordered by skill rank, it goes like this:
1. We pick the highest skill rank.
2. If there are at least two players with the same skill rank, we pick the one with the higher avarage rank (avarage rank means avarage of all other skills rank)
3. If there are at least two players still, we pick randomly.

After this step. we have 3 teams, and each team contain one player according to the random skill.
Now we pick a new skill (of the rest skills), and put the players in the same way.
But now, the order of the picking teams is not random, but ordered by the team total rank (calculated by the summary of the players rank in the team).

# Development 

## Add your own algorithm
First, think of an algorithm which makes sense and could be helpful and disribute it!

In `Algos` folder you could find `AlgoType` enum which declares the existing algo types. Add you new enum value that suitable to your algorithm name.
Now, Create your own folder for your algorithm with the name of your algo with a postfix of Algo.
For instace, if you create an algorithm with the name **TheBestAlgoEver** you should add enum with the name `TheBestAlgoEver` and folder with the name `TheBestAlgoEverAlgo`.

Here are 3 interfaces you should implement:

1. `IAlgoManager` is the one who implements teams generation.
2. `IPlayerReader` is the one who implements the players reading. 
3. `IPlayer` is an interface which reflects the player model.

`IPlayerReader` receives the `path` string as parameter and returns collection of `IPlayer` with the relevant data (according to the implementation).

In case your algorithm engine works with local files, you should create a file with the name `players` with any extension you desire. The file path is provided to your `IPlayersReader` implementor and reading the players as you wish.

## Add a CLI command
Under CLI folder you can find all of the commands and the printer classes.
Creation of a new command for CLI requires these steps:

1. Create an implementation of `IPrinterOptionCallback`.
2. Fill the `Description` and `DoCommand`.
3. Add your command to the `optionsCallback` dictionary in `Program.cs` file. 


# Run

## Endpoint (Standalone)
This runner is an endpoint application which runs on Windows machine only. 
In order to run the user should maintain a config.json file.
The file contains an array of `shirtColors` which is an array of objects based on `name` which is the color name, and `whatsappSymbol` which is the color symbol that integrated with whastapp app.

After this file is created, the user should edit the `player` file which is stored in the algo folder.

Then all the user need is to run the app, choose the relevant algorithm and receive its teams.

## Web Service
In order that any one at the world could use your algorithm, you should maintain the web service.
In `WebAppAPI` we should add our algorithm to the both dictionary (follow the existing items), and in addition we need to add a new `AlgoType` enum.

## Player groups and compatibility

Each account has a built-in `default` player group plus up to 19 custom
groups. The Default group deliberately uses the original player blob names,
so clients that do not know about groups continue to read and write the same
roster without a migration. Existing accounts use their legacy matchday name
as the Default group's initial display name. That value is persisted once;
later group renames and matchday-name changes are independent. Matchday names
continue to label scoreboard graphics.

`groupId` is optional on the existing `GET /UserPlayers` and
`POST /UserPlayers/Upload` endpoints. Omitting it, sending an empty value, or
sending `default` selects the legacy roster. Custom group IDs use isolated
blob paths and must first be created through `UserPlayers/Groups`.

The group API supports listing, creating, renaming, and deleting groups.
The built-in group's display name can be renamed, but its `default` ID and
legacy player blob paths never change. The built-in group cannot be deleted.
Deletion is rejected while any algorithm roster in the group contains a
player. `POST /UserPlayers/Move` moves every stored algorithm representation
of a player between groups, writing destination copies before removing source
copies so an interrupted operation cannot lose the player.
`POST /UserPlayers/Copy` copies those representations to another group while
leaving the source roster unchanged.

Group-scoped settings include the matchday name, location, player limit, team
count, schedule, shirt colors, algorithm, skill definitions, and descriptive
matchday rules. All remaining
preferences are global per organizer, including scoreboard behavior, timer
behavior, language, and chemistry. The Default group continues to use the
legacy `{uid}_config` blob. Custom groups use
`{uid}_player_groups/{groupId}/config`, and co-organizers resolve to the same
owner-backed group configuration through membership authorization while
retaining their own global preferences. A custom group without a saved
configuration inherits the owner's legacy Default-group configuration as a
migration fallback, except matchday rules, which start unset for every distinct
group; its first save creates an independent group configuration.
The app exposes these settings separately: **Account Settings** is available
from the main header, while **Group settings** is available from the active
player group's management menu. Configuration saves include a `scope` query
parameter so each screen updates only its own settings.

### Resetting chemistry

App Settings > Match > Team balancing offers **Reset chemistry** with a Yes/No
confirmation. `POST /Teams/Chemistry/Reset` resets only the authenticated
organizer's account-wide chemistry; it does not accept an owner ID or require
chemistry to be enabled. No match history, scores, rosters or saved teams are
deleted, and other accounts are unaffected.

A separate reset marker in the Matches table records a storage-generated cutoff.
Team generation ignores chemistry evidence from matchdays completed at or before
that cutoff, including when an old matchday is finalized again. Both chemistry
scores and the displayed learning-history count use the same cutoff. Matchdays
completed afterward start building new evidence. Normal settings saves do not
change the cutoff, and the app invalidates its cached chemistry scores while
preserving lineups and lineup undo.

### Descriptive matchday rules API

`config.matchdayRules` is nullable and is returned by both `GET /Config` and
`GET /Home` for the authorized active group. These rules are display/share
content only: they never change timers, scoring, team generation, or automatic
match completion. The existing operational timer preferences remain global.
Rules retain a `language` field (`en` by default, or `he`) in the API contract.
The current frontend uses the organizer's App Settings language for the editor
and rules graphics, overriding the stored rules language when displaying them.

Send the object with `POST /Config/Upload?scope=group&groupId=...`:

```json
{
  "language": "en",
  "timeLimitEnabled": false,
  "minutes": 8,
  "goalLimitEnabled": false,
  "goals": 2,
  "extraTimeEnabled": false,
  "extraMinutes": 2,
  "goldenGoal": false,
  "endMode": "on-time",
  "endOnCorner": false,
  "endOnBallOut": false,
  "endOnGoalkeeperHold": false,
  "endOnOther": false,
  "endOtherText": "",
  "tieResolution": "none",
  "noSlideTackles": false,
  "noDangerousPlay": false,
  "rotateGoalkeepers": false,
  "respectDecisions": false,
  "customText": ""
}
```

The example is the value of `matchdayRules`, not the entire configuration
request. All rule switches default to false. Enabled numeric limits must be
integers: minutes 1-120, goals 1-50, extra minutes 1-30. Extra time is normalized
off without a time limit, and golden goal is normalized off without extra time.
Only effectively enabled numeric limits are range-checked. `tieResolution`
must be `none`, `penalties`, or `longest-playing-off`. `customText` must be a
non-null string of at most 1200 UTF-16 code units and 20 nonempty lines;
whitespace-only lines do not count. Domain validation failures return the
existing unsuccessful save response; malformed JSON/types use the API's
standard model-binding errors.

`endMode` is `on-time` (default) or `stoppage`. With a time limit and `stoppage`,
at least one end-event flag is required. These describe waiting after time expires
for any selected event: corner, throw-in/goal kick, goalkeeper holding the ball,
or the supplied other condition. The rule also applies after extra time; goal
limits and golden goals still end play immediately. `endOtherText` must be a
non-null string of at most 300 UTF-16 code units, and must be nonblank when Other
is active. Without a time limit, `endMode` normalizes to `on-time` and event
choices are ignored for display. Existing blobs without these fields retain
their prior right-on-time behavior.

Omitting `matchdayRules` or sending null preserves existing group rules for
older clients. To clear rules, send the full all-disabled object above. A
non-null object replaces the previous rules (it is not a partial patch).
Global-only saves ignore incoming rules and preserve stored group rules.
Co-organizers use the same owner-backed rules after existing group access
checks; rules never fall back to another group's or organizer's rules.

## Premium entitlements

Premium behavior is centralized behind `IAccountEntitlementService`, using the
existing Premium account store when enforcement is enabled. API and UI gates
cover AI summaries, chemistry, the AI team-building algorithm, sharing styles,
group logos, match invitations, and free-account roster/group limits.

Match invitations additionally require the account-wide
`config.enableMatchInvitations` opt-in (default `false`). `GET /Home` returns
`entitlements.canUseMatchInvitations` for the requester and a separate
`invitationAccess.enabled` for the selected group's canonical owner policy.
Shared-group availability never overwrites the requester's global preferences.
Existing public links close without losing responses when the owner disables
invitations or loses Premium, and future occurrences reopen on re-enable.
Public responses remain anonymous and use the owner's live language.
See [the invitation API and rollout notes](TeamsGeneratorWebAPI/Invitations/README.md).

## Co-organizers and private player data

Group owners can create a seven-day, single-use co-organizer invitation link
and QR code. Invitation tokens are random and only their SHA-256 hash is
stored. Redemption requires an authenticated account. Shared groups expose
one canonical roster and generated-team snapshot, which active clients refresh
every five seconds while visible.

Player identity, attendance, and generated-team placement are shared.
Skill ratings and other assessments are stripped from co-organizer responses
and stored in organizer-specific assessment overlays.

`goalkeeperSkill` (nullable 1-10) and `isRecoveringFromInjury` (default false)
are organizer-private assessments, including for group owners. They are not
global skill definitions or reflection-generated editor controls. The roster
overlay preserves these fields; shared team snapshots, public match JSON, swaps,
and shared player JSON omit them, including on reads of older snapshots.
Clients must rehydrate team-detail and locked-player assessments from the
current organizer's roster, not treat shared snapshots as ratings.
Anonymous roster reads retain shared names and attendance but omit assessments;
an authenticated identity claim, never a supplied UID alone, is required to
load an assessment overlay. Saving nonempty dedicated assessments or moving/
copying their private overlays also requires a signed-in organizer.

SkillWise and BackAndForth use `isGoalKeeper`; Positions uses `positions`
containing `GK` (enum value 0), with no separate goalkeeper flag. In Positions,
the dedicated rating applies only to sole goalkeepers (`positions: [0]`).
Mixed GK/outfield players retain field-skill contributions, even if a previously
saved goalkeeper rating remains stored; they still count for GK distribution.
An explicit goalkeeper level replaces all balancing contributions for an
eligible goalkeeper.
Without it, legacy skill averages/rank remain the fallback. Base attributes and
the BackAndForth rank are returned unchanged; `EffectiveRank` is internal and
JSON-ignored. Recovery subtracts 0.5 once from effective skills or the goalkeeper
level/rank, floored at 1. Effective numeric inputs are finite and bounded 1-10;
invalid nonfinite goalkeeper levels become null. Team rank includes goalkeeper
contributions. Displayed team skill averages exclude explicitly rated eligible
goalkeepers rather than label their scalar as attack/defence ability; an all-GK
team has an empty skill-average map. Null-rated legacy and mixed-position
players retain field-skill contributions. Returned player base attributes
remain unchanged.
AI assessments use the same effective values before balancing, not an additional
model-inferred recovery penalty. In description-based AI mode, an explicit
goalkeeper level also identifies a goalkeeper; no additional player roles exist.

The dependency-free regression executable is
`scripts\RatingRegression\RatingRegression.csproj`. Run with
`dotnet run --project scripts\RatingRegression`. If the local API holds build
outputs open, pass a separate `-p:OutputPath=...`. On machines without .NET 6,
set `DOTNET_ROLL_FORWARD=Major` to use an installed newer runtime.

Roster writes are serialized by the client. An account-, group-, and
algorithm-scoped pending snapshot remains on the device until the API confirms
the write, so interrupted mobile saves can be retried after suspension or
relaunch without losing attendance state.

## AI match reports

AI reports are generated from a server-verified fact sheet. The writing prompt
establishes the winner concisely and then prioritizes the strongest evidence
from across the evening, preventing one leading team from crowding out more
meaningful player, partnership, comeback, or opposing-team stories. Generated
report text is cached only on the device that requested it.

## Backend usage telemetry

The web API sends privacy-safe custom events to Application Insights through
`IUsageTelemetry`. Events include the client version, deployment environment,
anonymous user ID when available, operation outcome, and aggregate counts.
Player names, match content, email addresses, and locations must not be added
to telemetry.

The main events are:

- `AppSetupLoaded`, `ConfigurationSaved`
- `PlayerRosterLoaded`, `PlayerRosterSaved`
- `TeamsGenerated`, `TeamsLoaded`, `TeamsSaved`
- `MatchdayStarted`, `MatchRecorded`, `MatchEdited`, `MatchDeleted`
- `PlayerSwapRecorded`, `MatchdayCompleted`
- `ShareImageGenerated`, `AiReportGenerated`

Use custom event counts for usage and the `outcome` property for reliability.
Measurements such as `player_count`, `team_count`, `match_count`,
`image_bytes`, and `duration_ms` support aggregate analysis.
