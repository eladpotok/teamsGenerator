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
count, schedule, shirt colors, algorithm, and skill definitions. All remaining
preferences are global per organizer, including scoreboard behavior, timer
behavior, language, and chemistry. The Default group continues to use the
legacy `{uid}_config` blob. Custom groups use
`{uid}_player_groups/{groupId}/config`, and co-organizers resolve to the same
owner-backed group configuration through membership authorization while
retaining their own global preferences. A custom group without a saved
configuration inherits the owner's legacy Default-group configuration as a
migration fallback; its first save creates an independent group configuration.
The app exposes these settings separately: **Account Settings** is available
from the main header, while **Group settings** is available from the active
player group's management menu. Configuration saves include a `scope` query
parameter so each screen updates only its own settings.

## Premium entitlements

Premium behavior is centralized behind `IAccountEntitlementService`. The
current provider intentionally grants Premium to every account, so existing
behavior is unchanged until a billing-backed provider replaces it. Both API
and UI gates cover AI summaries, chemistry, the AI team-building algorithm,
and the free-account limit of 25 players. Older API responses that do not
include entitlements are treated as Premium during deployment.

## Co-organizers and private player data

Group owners can create a seven-day, single-use co-organizer invitation link
and QR code. Invitation tokens are random and only their SHA-256 hash is
stored. Redemption requires an authenticated account. Shared groups expose
one canonical roster and generated-team snapshot, which active clients refresh
every five seconds while visible.

Player identity, attendance, and generated-team placement are shared.
Skill ratings and other assessments are stripped from co-organizer responses
and stored in organizer-specific assessment overlays.

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
