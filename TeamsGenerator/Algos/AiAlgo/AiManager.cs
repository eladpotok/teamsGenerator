using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.AiAlgo
{
    public class PlayerJsonToAi
    {
        public string Name { get; set; }
        public List<string> Description { get; set; }
        public string Key { get; set; }
        public string ModifiedTime { get; set; }
    }

    public class TeamsAiJsonOutput
    {
        public string Name { get; set; }
        public IEnumerable<string> Players { get; set; }
        public int Rating { get; set; }
        public string Strength { get; set; }
        public string Weakness { get; set; }
    }

    public class TeamsWrapper
    {
        public List<TeamsAiJsonOutput> Teams { get; set; }
    }

    public class AiManager : AlgoManagerBase, IAlgoManager
    {
        private List<AiPlayer> _players;
        private string _prompt;
        private OpenAiService _aiService;

        public AiManager(AlgoConfig config) : base(config)
        {
            _aiService = new OpenAiService();
        }

        public List<Team> Generate(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers)
        {
            var playersToProvideInAi = players
                 .Cast<AiPlayer>()
                 .Select(player => new PlayerJsonToAi
                 {
                     Name = player.Name,
                     Description = player.Description.Split(',').ToList(),
                     Key = player.Key,
                     ModifiedTime = player.ModifyTime
                 });

            var playersKeyToName = players.ToDictionary(player => player.Key, player => player.Name);

            string promptHe = @"
יש לי רשימה של שחקני כדורגל, כאשר לכל שחקן יש תיאור חופשי של סגנון המשחק, נקודות החוזקה והחולשה שלו.  
על סמך התיאור הזה, אני רוצה שתחזיר לי רשימה של אובייקטים בפורמט JSON מדויק לפי המבנה הבא:

{
    ""name"": ""השם כפי שמופיע ב-Name"",
    ""defence"": ?, 
    ""attack"": ?, 
    ""stamina"": ?, 
    ""leadership"": ?, 
    ""passing"": ?, 
    ""key"": ""ה-Key כפי שמופיע"",
    ""modifyTime"": ""random date time"",
    ""id"": """",
    ""isArrived"": true,
    ""isGoalKeeper"": false,
    ""isLocked"": false
}

הנחיות נוספות:  
1. קבע לכל שחקן ציון בין 1 ל-10 עבור השדות:
   - attack
   - defence
   - stamina
   - leadership
   - passing  
ליד כל שדה תוסיף את ההסבר שלך, למה נתת לו כזה ציון
   על פי התיאור של סגנון המשחק שלו.

2. יש לפרש את התיאור בצורה סמנטית ולהתייחס למילים לפי משמעותן:

    שים לב לתיאור ולרמה שלו. למשל, גולר, גולר טוב, גולר מעולה, כובש מצטיין. כלומר לפי התיאור ככה הדירוג שלו
   - כשאומרים ששחקן לא עושה הגנה, או לא שומר טוב, אז הכוונה שה defence שלו נמוך
   - תפרש את המונח ""אנוכי"" כמישהו שלא מוסר לחברים שלו ופועל לבד, ולא כמישהו חזק.
   - כל איבר ברשימה ב Description הוא תיאור בפני עצמו על השחקן. תתייחס לכל תיאור בלי קשר לתיאור אחר ברשימה.
הציון כברירת מחדל הוא בין 4 ל 5. כלומר, אם אין תיאור שלילי או חיובי, אפשר לתת בין 4 ל5. 
תתבסס אך ורק על מה שכתוב בתיאור על השחקן. אל תוסיף טקסט בעצמך.

3. אל תיתן ציונים כלליים — התוצאה צריכה לשקף את סגנון השחקן בפועל.  
   השתמש בכל טווח הערכים 1–10 בהתאם לעוצמה היחסית שמובעת בתיאור.

4. אם מהתיאור עולה שהשחקן הוא שוער – קבע ""isGoalKeeper"": true.  

5. אל תשנה את שמות המפתחות – יש לשמור בדיוק על מבנה ה־JSON.  

6. אל תוסיף טקסט חיצוני, רק את הפלט בפורמט JSON תקין.  

7. לכל שחקן תייצר modifyTime אקראי (תאריך ושעה)

תבצע את זה לכל השחקנים ברשימה
שים לב: אם בתיאור יש מילים שיכולות להתפרש בכמה דרכים (כמו ‘אנוכי’ או ‘אנכי’), אל תנחש. אם זה לא ברור, תעדיף להשאיר את הציון נייטרלי ולא להוסיף מאפיינים שלא נאמרו במפורש.

";

            var promptEn = @"
I have a list of soccer players, and each player has a free-text description that explains their playing style, strengths, and weaknesses.  
Based on these descriptions, I want you to return a list of objects in exact JSON format according to the following structure:

{
    ""name"": ""the name as appears in Name"",
    ""defence"": ?, 
    ""attack"": ?, 
    ""stamina"": ?, 
    ""leadership"": ?, 
    ""passing"": ?, 
    ""key"": ""the Key as appears"",
    ""id"": ""the Id as appears"",
    ""isGoalKeeper"": false,
}

Make sure the JSON is valid and does not contain any additional fields or text.
Ensure that all quotes ("") are valid and correctly placed.

Additional instructions:

1. For each player, assign a score between 1–10 for the following fields:
   - attack
   - defence
   - stamina
   - leadership
   - passing

2. Interpret the text semantically and consider the meaning of the words carefully.

   Pay attention to the level expressed in the description.  
   For example: ""goal scorer"", ""good finisher"", ""excellent striker"", ""outstanding scorer"" — the wording indicates the rating intensity.  
   - When the description says a player does not defend well or does not mark opponents, it means their defence score should be low.  
   - Interpret the word ""selfish"" as someone who does not pass to teammates and prefers to act alone — not as someone strong.  
   - Each item in the Description list represents an independent trait. Treat each separately and do not mix them.

   The default score range is 4–5, meaning that if the description does not specify a clear strength or weakness, assign a neutral score between 4 and 5.  
   Base your decisions only on what is explicitly written in the description.  
   Do not invent or assume any extra traits.

3. Do not give vague or generic scores — the result must accurately reflect the player's actual playing style.  
   Use the entire range (1–10) depending on the intensity implied by the text.

4. If the description indicates that the player is a goalkeeper, set ""isGoalKeeper"": true.

5. Do not change any key names — the JSON structure must remain exactly as defined.

6. Do not include any external text — only valid JSON output.

You are an API that outputs only valid JSON.

Generate a single JSON object (or array, depending on your need) that matches the structure below.
Return **only the JSON**, with no explanations, no markdown formatting, and no extra spaces or line breaks.
Your only task is to output a **single valid JSON array** that exactly matches the structure below.  
Each item must be a flat object with primitive values (numbers, strings, booleans, or null only).  
Do not include arrays or nested objects inside any field.  

Strict rules:
1. Output **only valid JSON**, with no markdown, comments, or text before or after.
2. **Do not include any nested arrays or objects** inside the fields — all values must be primitive.
3. Use standard double quotes ("") for strings.
4.Use compact JSON format with no unnecessary spaces or newlines.
5.Ensure all brackets and braces are properly closed.
6.Do not invent additional keys or metadata.
7.If you cannot infer a value, use a neutral numeric value between 4–5.
8.Verify the output is valid JSON before returning.
- Always use standard straight double quotes ("") in JSON keys and string values.
-Never use curly quotes(“ or ”) or any typographic quotes.
-Return only machine-readable JSON, with no markdown, no formatting, and no text before or after.
- Make sure the output passes JSON.parse() without errors.
- Do not truncate or cut the last player's object

Example of correct output format(compact, valid JSON only):
[{ ""name"":""John"",""defence"":5,""attack"":6,""stamina"":4,""leadership"":5,""passing"":5,""key"":""A1"",""modifyTime"":""2025-10-12T14:00:00Z"",""id"":"""",""isArrived"":true,""isGoalKeeper"":false,""isLocked"":false}]

7. For each player, generate a random modifyTime (date and time).

Apply this process to all players in the list.  
Note: if a word in the description can have multiple meanings (for example “selfish” vs “vertical”), do not guess.  
If the meaning is unclear, prefer to keep the score neutral and avoid adding traits that are not explicitly mentioned.
";

            // Choose prompt based on Lang property
            _prompt = _config.Language == "he" ? promptHe : promptEn;
            string playersResponse = GetAiResponse(_prompt, playersToProvideInAi);

            var result = new List<Team>();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

           var skillWisePlayersAccordingToAi = JsonSerializer.Deserialize<IEnumerable<SkillWisePlayer>>(playersResponse, options);
            var teams = new SkillWiseManager(_config).GenerateTeams(skillWisePlayersAccordingToAi.Cast<IPlayer>().ToList(), generatedTeamWithLockedPlayers);

            var promptForTeamsResults = @"You will receive an input that contains a list of teams.  
Each team includes numeric values for:
- attack
- defence
- stamina
- leadership
- passing

Your task:
Analyze the team and generate a **new JSON** that describes the team’s overall characteristics.

Output format (exact, valid JSON — no extra text):

[
  {
    ""index"": 0, (and should be growing in each element)
    ""strength"": [""short text point"", ""short text point""], --> in title case
    ""weakness"": [""short text point"", ""short text point""], --> in title case
    ""playStyle"": ""short text that describes the playstyle (e.g. attackers, defenders, technique, vision, balanced)"", --> in title case
    ""description"": ""A short and clear paragraph describing the team based on its players' skills.""
  }
]

📝 Instructions:
1. Analyze the total attack, defence, stamina, leadership, and passing for each team.
2. according these fields, Identify the max 2–3 attributes, and takes only theses attributes that there is high distance than other (5~ points than the other following attributes) → turn them into strength points (short text like ""strong attackers"", ""attackers"", ""defenders"" and etc).
   should be in context of football team
3. according these fields, Identify the min 2–3 attributes**, and takes only theses attributes that there is high distance than other  (5~ points than the other following attributes)→ turn them into weakness points (short text like “weak defence”, “lack of leadership” and etc).
   should be in context of football team
4. Determine the **style** based on the strongest attributes.
   - For example:
     - high attack → “attacking”
     - high defence → “defensive”
     - high passing or leadership → “vision” or “organized”
     - balanced values → “balanced team”
5. Generate a **short and fluent description** of the team in natural language (1–2 sentences), according attack, defence, stamina, leadership, and passing for each team.
6. Do not include explanations, markdown, or any text outside the JSON.
7. Ensure the JSON is valid, properly quoted, and without extra spaces or lines.

If the meaning of the numbers is ambiguous, make reasonable neutral assumptions and keep the text short and clear.
";

            var teamAi = teams.Select(t => new
            {
                Index = t.Index,
                Players = t.Players,
                Attack = t.Players.Cast<SkillWisePlayer>().Sum(p => p.Attack),
                Defence = t.Players.Cast<SkillWisePlayer>().Sum(p => p.Defence),
                Leadership = t.Players.Cast<SkillWisePlayer>().Sum(p => p.Leadership),
                Passing = t.Players.Cast<SkillWisePlayer>().Sum(p => p.Passing),
                Stamina = t.Players.Cast<SkillWisePlayer>().Sum(p => p.Stamina)
            });

            string teamsResponse = GetAiResponse(promptForTeamsResults, teamAi);
            var teamsAsAiResponse = JsonSerializer.Deserialize<IEnumerable<Team>>(teamsResponse, options);

            int index = 0;
            return teams.Select(team =>
            {
                var teamDescriptionAi = teamsAsAiResponse.First(t => t.Index == index);
                return new Team()
                {
                    Index = index++,
                    Description = teamDescriptionAi.Description,
                    PlayStyle = teamDescriptionAi.PlayStyle,
                    Strength = teamDescriptionAi.Strength,
                    Weakness = teamDescriptionAi.Weakness,
                    Players = team.Players.Select(p =>
                    {
                        var originalPlayerFromInput = playersToProvideInAi.First(pl => pl.Key == p.Key);
                        return new AiPlayer()
                        {
                            Description = string.Join(", ", originalPlayerFromInput.Description),
                            Key = p.Key,
                            Id = p.Id,
                            ModifyTime = p.ModifyTime.ToString(),
                            Name = players.First(pl => pl.Key == p.Key).Name,
                            IsArrived = true
                        };
                    }).Cast<IPlayer>().ToList()
                };
            }).ToList();
        }

        private string GetAiResponse(string prompt, object playersToProvideInAi)
        {
            string response = null;
            var task = Task.Run(async () =>
            {
                response = await _aiService.GetResponseFromAgentForTeams(prompt, JsonSerializer.Serialize(playersToProvideInAi));
            });
            task.Wait();
            return response;
        }

        public List<Team> GenerateTeams(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers)
        {
            var maxRetries = 3;
            int attempts = 0;
            while (attempts < maxRetries)
            {
                try
                {
                    var teams = Generate(players, generatedTeamWithLockedPlayers);
                    return teams;
                }
                catch (Exception ex)
                {
                    attempts++;
                    if (attempts >= maxRetries)
                    {
                        throw;
                    }
                }

            }

            return null;
        }


    }
}
