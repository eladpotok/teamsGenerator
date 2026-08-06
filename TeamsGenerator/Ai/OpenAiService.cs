using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeamsGenerator.Ai
{
    public class OpenAiService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private string _endpoint;
        private string _apiKey;

        public OpenAiService()
        {
            SetAiConfig();
        }

        private void SetAiConfig()
        {
            // Read the JSON file
            var json = File.ReadAllText("config.json");

            var config = JObject.Parse(json);
            _apiKey = config.Value<string>("AiApiKey");
            _endpoint = config.Value<string>("AiAudience");
        }

        public Task<string> GetResponseFromAgentForTeams(
            string prompt,
            string userInput,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendChatRequestAsync(prompt, userInput, 0.2, cancellationToken);
        }

        public Task<string> GetResponseFromAgent(
            object userInput,
            string language = MatchSummaryPrompt.DefaultLanguage,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (userInput == null)
            {
                throw new ArgumentNullException(nameof(userInput));
            }

            var factSheet = MatchSummaryAnalyzer.CreateFactSheet(userInput);
            var userMessage = "VERIFIED_FACT_SHEET_JSON_START\n"
                + factSheet
                + "\nVERIFIED_FACT_SHEET_JSON_END";

            return SendChatRequestAsync(
                MatchSummaryPrompt.Create(language),
                userMessage,
                0.25,
                cancellationToken);
        }

        private async Task<string> SendChatRequestAsync(
            string systemPrompt,
            string userInput,
            double temperature,
            CancellationToken cancellationToken)
        {
            var chatRequest = new ChatRequest
            {
                messages = new List<ChatMessage>
                    {
                        new ChatMessage
                        {
                            role = "system",
                            content = systemPrompt
                        },
                        new ChatMessage
                        {
                            role = "user",
                            content = userInput
                        }
                    },
                temperature = temperature,
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            {
                request.Headers.Add("api-key", _apiKey);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(chatRequest),
                    Encoding.UTF8,
                    "application/json");

                using (var response = await HttpClient.SendAsync(request, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var result = JObject.Parse(json);
                    var responseText = result.SelectToken("choices[0].message.content")?.Value<string>();

                    if (string.IsNullOrWhiteSpace(responseText))
                    {
                        throw new InvalidDataException("The AI response did not contain message content.");
                    }

                    return responseText;
                }
            }
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
        public double frequency_penalty { get; set; }
        public double presence_penalty { get; set; }
    }
}
