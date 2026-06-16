using System.Text;
using System.Text.Json;

namespace cryptonet.Services
{
    public class GroqService
    {
        string apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
        private readonly IHttpClientFactory _httpClientFactory;

        public GroqService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // 1. Метод для перевода и анализа каждой отдельной новости
        public async Task<(string title, string summary, string sentiment)> TranslateAndAnalyze(string rawTitle, string rawSummary)
        {
            var prompt = $@"Переведи заголовок и краткое содержание крипто-новости на русский язык. 
            Также определи настроение новости: positive, negative или neutral.
            Верни ответ СТРОГО в формате JSON: 
            {{""title"": ""перевод"", ""summary"": ""перевод"", ""sentiment"": ""positive/negative/neutral""}}
            
            Заголовок: {rawTitle}
            Описание: {rawSummary}";

            try
            {
                var content = await SendRawRequest(prompt, true);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                return (data["title"], data["summary"], data["sentiment"]);
            }
            catch
            {
                return (rawTitle, rawSummary, "neutral");
            }
        }

        public async Task<(string sentiment, string summary)> AnalyzeMarketState(List<string> titles)
        {
            var prompt = $@"Проанализируй состояние крипторынка по следующим заголовкам новостей.
    Дай краткий итог на ОДНО предложение на русском языке и определи общее настроение (positive, negative или neutral).
    Верни ответ СТРОГО в формате JSON:
    {{""sentiment"": ""positive/negative/neutral"", ""summary"": ""твой итог на русском""}}

    Новости для анализа: {string.Join(". ", titles)}";

            try
            {
                // Используем безопасный JSON-формат
                var response = await SendRawRequest(prompt, true);
                using var doc = JsonDocument.Parse(response);

                var sentiment = doc.RootElement.GetProperty("sentiment").GetString().Trim().ToLower();
                var summary = doc.RootElement.GetProperty("summary").GetString().Trim();

                return (sentiment, summary);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка ИИ при анализе рынка: {ex.Message}");
                return ("neutral", "ИИ временно не смог составить общую картину рынка.");
            }
        }

        // Вспомогательный метод для отправки запросов к API Groq
        private async Task<string> SendRawRequest(string prompt, bool isJson)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[] { new { role = "user", content = prompt } },
                response_format = isJson ? new { type = "json_object" } : null
            };

            var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
    }
}