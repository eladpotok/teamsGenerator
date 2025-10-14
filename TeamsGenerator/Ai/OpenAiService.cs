using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TeamsGenerator.Ai
{
    public class OpenAiService
    {
        private static string SummaryPrompt = @"אני אשלח לך קובץ JSON שמכיל רשימה של משחקי כדורגל. כל משחק כולל שתי קבוצות, צבע, שחקנים, שערים ומבשלים.
הסבר:
כדי לדעת את רשימת הכובשים והמבשלים, ניתן לעבור על הרשימה של topAssists ו topScorers. חשוב לא לטעות במספרים ובחישוב.
כדי לדעת את סדר הקבוצות בטבלה, אפשר להסתכל על האובייקט stats שבו מופיעות כל הקבוצות, כשלכל קבוצה יש פירוט של ניצחונות (w) , תיקויים (d), הפסדים (l), שערים (gf), ספיגות (ga) ונקודות (Points)
אני רוצה שתחזיר לי סיכום כולל של כל הערב – לא סיכום לפי משחק.
הסגנון צריך להיות של שדר ספורט מקצועי, מלהיב, עם טון חיובי או משעשע, אבל כולל גם ניתוחים ותובנות עומק. 
תמנע מתיאור מוגזם של הערב, או תיאורים מופשטים שאין להם סימוכין. תנסה לשמור על סיכום תמציתי, מעניין, מעמיק, אבל לא מוגזם מדי.
הסיכום יכתב באופן הבא:
תתייחס לשמות של שחקנים שבלטו – כובשים, מבשלים, שוערים שהפתיעו, שחקני מפתח – לא לפי משחק ספציפי אלא לפי התרומה הכללית שלהם לערב כולו.
אתה יכול לציין אירועים לפי דפוסים שונים, אך ורק אם הם קרו, כמו למשל: שחקן שהוא מלך השערים אבל הקבוצה שלו סיימה אחרונה, שחקן שבישל הרבה ולא כבש, קבוצה עם תוצאה טובה למרות שהסגל חלש, שוער שכבש, ,שחקן שבישל לשחקן אחר, והשחקן הזה בישל לו חזרה, בערך 4-5 במהלך הערב.
שים לב שאם יש קבוצה שסיימה מקום ראשון, וזה קרה בזכות המשחק האחרון שלה שבה היא נצחה והגיעה למקום הראשון - חשוב לציין
לציין אם קרה שקבוצה הפסידה 3 פעמים ברצף ויותר. ואם בכלל סיימה מקום גבוה (ראשון או שני) שווה לציין את זה.
שים לב שהכוונה בהפסד רצוף, זה לאו דווקא אם המשחק היה אחד אחרי השני ברצף, אלא ב3 ההופעות הרצופות שלה היא הפסידה.
לבסוף, תתן ציון לשחקנים שבלטו במהלך הערב, ציון בין 1 ל-10, עם השקלול הבא:
מלך השערים, מלך הבישולים, מלך השערים של הקבוצה, מלך הבישולים של הקבוצה, מיקום בטבלה (מקום ראשון - משקל גבוה וכן האלה), שחקן שכבש את השער המכריע בניצחון.
חשוב לשים לב: זה שהשחקן סיים מלך השערים של הערב, לא אומר שהוא קיבל את הדירוג הכי גבוה, זה פשוט ייתן בונוס לציון שלו. אם יש שחקן שמוגדר כשוער, והקבוצה שלו ספגה מעט שערים, או שהיו הרבה משחקים ללא ספיגות, אפשר לתת לו ציון גבוה.
חשוב מאוד להתייחס לדברים הבאים:
אל תחשוף את דירוגי השחקנים (rank) – תשתמש בהם לניתוח פנימי בלבד, אך אל תציג אותם בטקסט. אבל חשוב להשיג את השמות.
תתמקד באירועים, בשמות, בקשרים – אל תעבור משחק-משחק בצורה טכנית.
חשוב שתדייק בכמות השערים והבישולים של כל שחקן. שאם אתה מציין ששחקן היה מלך השערים אז שזה אכן יהיה הוא
חשוב שתקדיש זמן לבדוק בקפידה כל הקשרים הרלוונטיים של בישולים, כיבושים ושערים בכל המשחקים, ותוודא שכל חישוב או ניתוח נעשה בצורה יסודית. אבל לא צריך להציג בפלט את החישוב עבור כל שחקן.
לפני מתן תשובה מספרית על בישולים, שערים, או כל נתון סטטיסטי אחר, אנא וודא את המספרים על ידי בדיקת כל המשחקים מחדש, גם אם נבדקו פעם אחת.
שמבנה הסיכום יהיה: תיאור הערב, ציין מאורעות מעניינים שקרו (רק אם באמת קרו), מלך שערים, מלך בישולים.";

        private static string TeamsPrompt = @"";
        private readonly HttpClient _httpClient;
        private const string Endpoint = "https://potok-mcwfn6md-eastus2.cognitiveservices.azure.com/openai/deployments/teams-generator-gpt-4.1/chat/completions?api-version=2025-01-01-preview";

        public OpenAiService()
        {
            _httpClient = new HttpClient();
            var apiKey = GetApiKey();

            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        private string GetApiKey()
        {
            // Read the JSON file
            var json = File.ReadAllText("config.json");

            using (var doc = JsonDocument.Parse(json))
            {
                // Access a specific element
                string name = doc.RootElement.GetProperty("AiApiKey").GetString();
                return name;
            }
            return null;
        }

        public async Task<string> GetResponseFromAgentForTeams(string prompt, string userInput)
        {
            var chatRequest = new ChatRequest
            {
                messages = new List<ChatMessage>
                    {
                        new ChatMessage
                        {
                            role = "system",
                            content = prompt

                        },
                        new ChatMessage
                        {
                            role = "user",
                            content = userInput.ToString()
                        }
                    },
                temperature = 0.95,
            };

            var content = new StringContent(
                JsonSerializer.Serialize(chatRequest),
                Encoding.UTF8,
                "application/json"
            );
            var response = await _httpClient.PostAsync(Endpoint, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            return result.RootElement
                         .GetProperty("choices")[0]
                         .GetProperty("message")
                         .GetProperty("content")
                         .GetString();
        }

        public async Task<string> GetResponseFromAgent(dynamic userInput)
        {
            string prompt = @"קלט: קובץ JSON שמכיל רשימה של משחקי כדורגל. כל משחק כולל שתי קבוצות, צבע, שחקנים, שערים, ומידע נוסף כמו topScorers ו-topAssists, וכן אובייקט stats המכיל את טבלת הקבוצות (w,d,l,gf,ga,Points וכו').

מטרה: החזרת סיכום כולל של כל הערב (לא סיכום משחק-משחק). הסגנון — שדר ספורט מקצועי, מלהיב וחיובי או משעשע, עם ניתוחים ותובנות עומק; תמציתי ומדויק — ללא הגזמות או תיאורים לא מגובים בנתונים.

הכללים וההנחיות החייבים:
1. חישובים ובדיקות:
   - לפני כל הצהרה כמותית (שערים, בישולים, ""מלך השערים"", ""מלך הבישולים"" וכו') בצע חישוב מחודש על כל המשחקים המסופקים — אל תניח ושמור על דיוק מספרי.
   - עבוד רק עם המידע שבקובץ JSON. אם משהו לא ניתן לחשבון מהנתונים, ציין בקצרה שהוא ""לא ניתן לקביעה מן הנתונים"".

2. יצירת סיכום כולל — מה להכליל:
   - שמות שבלטו במהלך הערב: כובשים, מבשלים, שוערים שהפתיעו ושחקני מפתח — לא לפי משחק בודד אלא לפי התרומה הכוללת שלהם לערב.
   - אירועים מעניינים/דפוסים — הזכר רק אם הם אכן קרו בהתבסס על הנתונים (ראו סעיפים ההחלטה למטה).
   - ציון לכל שחקן שציינת (1–10) לפי קריטריון שיקלול (ראה סעיף דירוג).
   - בבסוף — פסקה קצרה ""מלך השערים"" ו""מלך הבישולים"" עם כמותם המדויקת.

3. מתי לציין שינוי מקום בראש הטבלה בעקבות המשחק האחרון:
   - אסור לומר ש""קבוצה עלתה למקום הראשון בגלל המשחק האחרון שלה"" אלא אם ניתן להראות זאת מתוך הנתונים. להצהיר כך רק אם:
     a) הנתונים מכילים טבלת סטטס לאחר כל המשחקים בערב, ו-
     b) חישבת מחדש את הטבלה *כאילו* לא נכלל משחקה האחרון של אותה קבוצה בערב — ואם חישוב זה מראה שהקבוצה לא הייתה במקום הראשון לפני משחק זה, אז אפשר לציין: ""הקבוצה הגעתה/עלתה למקום הראשון הודות לניצחון הערב (בדיקה: ללא תוצאת משחק זה מיקומה היה X)"".
   - אם אין דרך לבצע את הבדיקה הזו מהנתונים — אל תטען שהמיקום השתנה בגלל המשחק האחרון.

4. הפסד רצוף (streak of losses):
   - ציין שאותה קבוצה הפסידה 3 פעמים ברצף (או יותר) **רק אם** בתוך רשימת המשחקים מופיעות שלוש הופעות רצופות של הקבוצה והן הפסדים. חשוב: ""רציפות"" כאן משמעה בשלוש ההופעות האחרונות של אותה קבוצה בתוך ה-data-set, לא בהכרח משחק-אחר-משחק בזמן הכללי.
   - אם קבוצה שסבלה מהפסד רצוף סיימה במקום גבוה (1–2), ציין את הסתירה הזו.

5. חילופי בישולים/סחר בישולים בין שני שחקנים:
   - ציין שחקנים שבישלו אחד לשני בתדירות גבוהה **רק אם** התרחשו לפחות 3 בישולים דו-כיווניים (A->B ו-B->A יחד) במהלך הערב, או אם הסכום של הבישולים ההדדיים מהווה אחוז ניכר (למשל >=30%) מכלל הבישולים של אחד השחקנים. אם הייתה רק 1–2 החלפות — אל תציין זאת כ""מובהק"" (אפשר להזכיר בקצרה רק אם זה חלק מהקונטקסט).

6. איסור חשיפת דירוגים פנימיים:
   - **אל תציג** שדה rank או דירוג פנימי אחר. מותר/נדרש להשתמש בערכים הללו לניתוח פנימי בלבד, אך הם אסורים בפלט.

7. הימנעות מתיאורים לא רלוונטיים:
   - אל תכלול הערות מבוזרות כמו ""השחקן X נראה מודאג"" או ""האווירה הייתה חמה"" — אלא אם המידע נמצא בנתונים.
   - הימנע מתקצירי משחק-משחק טכניים; הדגש קשרים, דפוסים ושמות בולטים.

8. דיוק בפרטים:
   - ודא שכל מספר (שערים ובישולים לכל שחקן) תואם בדיוק לחישובים. אם יש אי-בהירות לגבי שחקן (למשל שם כפול), השתמש בדיוק בשם כפי שמופיע ב-JSON.

9. פורמט הפלט:
   - תחילת הסיכום: פסקת פתיחה קצרה המתארת את הרושם הכללי של הערב (2–4 שורות).
   - סעיף אירועים מעניינים (רק אם קרו).
   - ""שחקנים שבלטו"" — רשימת שחקנים עם ציון 1–10 וקצרה (שורה אחת לכל שחקן: סיבה עיקרית + מספרים מדויקים — שערים ובישולים).
   - ""מלך השערים"" ו""מלך הבישולים"" — שם + כמות.
   - סגירת פסקה עם תובנה taktical/כללית (למשל: איזו קבוצה רוצה לשים לב ל..., או איזה שחקן עלה קרוב ל-נקודה מסוימת).

10. דירוג (חישוב נקודות 1–10) — הנחיות לשקלול:
    - בונוס על היותו מלך השערים של הערב.
    - בונוס על היותו מלך הבישולים של הערב.
    - שקלול על פי מיקום הקבוצה בטבלה (מקום 1–3 משקל גבוה יותר).
    - בונוס אם השחקן כבש שער מכריע בניצחון.
    - עבור שוערים — קח בחשבון מספר משחקים ללא ספיגות ו- GA נמוך.
    - חשוב: למרות הבונוסים, דירוג כולל יכול להיות נמוך אם רעש שלילי (למשל שחקן מלך השערים שתרם מעט לקבוצה שלו והקבוצה סיימה במקום נמוך — השקלול ישקף זאת).

11. שפה וטון:
    - כתוב בעברית תקנית, סגנון שדר ספורט מקצועי ומלהיב, אבל מדויק ותמציתי. יכול להיות קריצה הומוריסטית קלה אך לא בהגזמה.

12. בדיקות נוספות:
    - לפני סיום, בצע בדיקה נוספת של סכומי השערים והבישולים של כל שחקן כדי לוודא שאין אי-התאמות (הסר חריגות נפוצות: כפילויות שם, טעויות הקלדה של שמות).

דוגמה קונקרטית (תבנית פלט — מלא בהתאם לנתונים):
(פסקת פתיחה — 2–4 שורות)
אירועים מעניינים:  
- אירוע 1 (רק אם קרה — פרטי)  
- אירוע 2 (אם קרה)  
שחקנים שבלטו:  
- שם שחקן — ציון X/10 — 2 שערים, 1 בישול — סיכום קצר למה בלט.  
- שם שחקן — ציון Y/10 — 1 שער, 3 בישולים — ...  
מלך השערים: שם — N שערים.  
מלך הבישולים: שם — M בישולים.  
סיכום takktical קצר (1–2 שורות).";
            //var content = new StringContent(JsonSerializer.Serialize(userInput), Encoding.UTF8, "application/json");

            var engPrompt = @"I will send you a JSON file that contains a list of football matches. Each match includes two teams, colors, players, goals, and assists.

            Explanation:  
            To determine the top scorers and top assisters, you should go through the `topScorers` and `topAssists` lists. It’s important not to make any mistakes in the numbers or calculations.  
            To determine the table ranking of the teams, refer to the `stats` object. It contains all the teams with detailed stats: wins (`w`), draws (`d`), losses (`l`), goals for (`gf`), goals against (`ga`), and points (`Points`).

            I want you to return a general summary of the entire evening — not a per-match summary.

            The tone should be that of a professional sports commentator: exciting, positive or humorous in style, but also insightful and analytical.  
            Avoid exaggerated descriptions of the evening or abstract/unfounded language. Keep the summary concise, engaging, insightful, but not over-the-top.

            The structure of the summary should be:

            - Highlight names of standout players — goal scorers, assisters, surprising goalkeepers, key players — not based on a specific match but on their total contribution to the entire evening.
            - You may mention interesting patterns, but only if they actually happened, for example:
              - A top scorer whose team finished last.
              - A player with many assists but no goals.
              - A team with a great result despite a weak roster.
              - A goalkeeper who scored a goal.
              - A pair of players who assisted each other in multiple matches (e.g., 4–5 times total during the evening).

            Also:
            - If a team finished first in the standings because of their last match victory, be sure to mention that.
            - If a team lost 3 matches in a row or more, and still ended up in a top position (1st or 2nd) — highlight this as well.  
              (Note: “lost in a row” means in their last 3 appearances, not necessarily back-to-back in the schedule.)

            Finally, assign a rating (1–10) to standout players based on the following criteria:
            - Top scorer of the evening.
            - Top assister of the evening.
            - Top scorer/assister within their team.
            - Team placement in the table (1st place = strong weight, and so on).
            - A player who scored a decisive match-winning goal.

            ⚠️ Important notes:
            - Do NOT display player rankings (`rank`). Use them only for internal analysis.
            - Focus on events, names, and relationships — avoid dry technical per-match breakdowns.
            - Ensure precise goal and assist counts for each player. If you claim someone is the top scorer, make sure they truly are.
            - Double-check all assists, goals, and stats across all matches carefully before citing them, even if previously verified. Do not include the raw calculations in your output — only the conclusions.
            - The summary structure should be:  
              Evening overview → Notable events (if they actually occurred) → Top Scorer → Top Assister.
            ";
            var a = JsonSerializer.Serialize(userInput);
            var chatRequest = new ChatRequest
            {
                messages = new List<ChatMessage>
                    {
                        new ChatMessage
                        {
                            role = "system",
                            content = prompt

                        },
                        new ChatMessage
                        {
                            role = "user",
                            content = userInput.ToString()
                        }
                    },
                temperature = 0.95,
            };

            var content = new StringContent(
                JsonSerializer.Serialize(chatRequest),
                Encoding.UTF8,
                "application/json"
            );
            var response = await _httpClient.PostAsync(Endpoint, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            return result.RootElement
                         .GetProperty("choices")[0]
                         .GetProperty("message")
                         .GetProperty("content")
                         .GetString();
        }

    }

    public class ChatMessage
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    public class ChatRequest
    {
        public List<ChatMessage> messages { get; set; }
        public double temperature { get; set; } = 1;
        public double top_p { get; set; } = 1;
        public double frequency_penalty { get; set; } = 0.2;
        public double presence_penalty { get; set; } = 0.3;
    }
}
