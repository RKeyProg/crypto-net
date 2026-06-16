using cryptonet.Data;
using cryptonet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace cryptonet.Services
{
    public class DailyAiUpdater : BackgroundService
    {
        private readonly DbConnectionFactory _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AiServiceSettings _aiServiceSettings;

        public DailyAiUpdater(
            DbConnectionFactory db,
            IHttpClientFactory httpClientFactory,
            IOptions<AiServiceSettings> aiServiceSettings)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _aiServiceSettings = aiServiceSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Сначала анализируем монеты (твой старый код)
                await UpdateAllCoins();

                // Потом красим новости в ленте
                await UpdateIndividualNewsSentiment();

                // И наконец делаем общее резюме для плашки сверху
                await UpdateMarketNewsAI();

                // Раз в сутки (или можно поставить меньше для тестов, например TimeSpan.FromHours(1))
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task UpdateMarketNewsAI()
        {
            try
            {
                using var connection = _db.CreateConnection();
                connection.Open();

                // Достаем заголовки новостей за последние 24 часа
                var cmd = new MySqlCommand(@"
            SELECT title FROM news 
            WHERE published_at >= NOW() - INTERVAL 1 DAY", (MySqlConnection)connection);

                var titles = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    titles.Add(reader.GetString("title"));
                }
                reader.Close();

                if (titles.Count == 0) return;

                // Склеиваем новости в один текст для анализа
                string fullText = string.Join(". ", titles);

                // Отправляем во Flask на новый эндпоинт
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsJsonAsync($"{_aiServiceSettings.BaseUrl.TrimEnd('/')}/analyze-news", new { text = fullText });

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<NewsAiResult>(content);

                    // Сохраняем в market_ai_analysis
                    var saveCmd = new MySqlCommand(@"
                INSERT INTO market_ai_analysis (sentiment, summary) 
                VALUES (@sentiment, @summary)", (MySqlConnection)connection);

                    saveCmd.Parameters.AddWithValue("@sentiment", result?.Sentiment ?? "neutral");
                    saveCmd.Parameters.AddWithValue("@summary", result?.Summary ?? "Нет данных для анализа");
                    saveCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("NEWS AI ERROR: " + ex.Message);
            }
        }

        // Вспомогательный класс для десериализации ответа от Python
        public class NewsAiResult
        {
            public string Sentiment { get; set; }
            public string Summary { get; set; }
        }

        private async Task UpdateIndividualNewsSentiment()
        {
            try
            {
                using var connection = _db.CreateConnection();
                connection.Open();

                // 1. Берем новости, которые еще не анализировались (статус neutral)
                var cmd = new MySqlCommand(@"
            SELECT id, title FROM news 
            WHERE sentiment = 'neutral' 
            ORDER BY published_at DESC LIMIT 50", (MySqlConnection)connection);

                var newsList = new List<(long Id, string Title)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        newsList.Add((reader.GetInt64("id"), reader.GetString("title")));
                    }
                }

                if (newsList.Count == 0) return;

                var client = _httpClientFactory.CreateClient();

                // 2. Проходим по каждой новости и спрашиваем Python
                foreach (var news in newsList)
                {
                    var response = await client.PostAsJsonAsync($"{_aiServiceSettings.BaseUrl.TrimEnd('/')}/analyze-news", new { text = news.Title });

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<NewsAiResult>(json);

                        // 3. Обновляем статус в базе
                        var updateCmd = new MySqlCommand(@"
                    UPDATE news SET sentiment = @s WHERE id = @id", (MySqlConnection)connection);
                        updateCmd.Parameters.AddWithValue("@s", result?.Sentiment ?? "neutral");
                        updateCmd.Parameters.AddWithValue("@id", news.Id);
                        updateCmd.ExecuteNonQuery();
                    }
                }
                Console.WriteLine($"[AI] Анализ настроений завершен для {newsList.Count} новостей.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ОШИБКА АНАЛИЗА НОВОСТЕЙ: " + ex.Message);
            }
        }
        private async Task UpdateAllCoins()
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand("SELECT id FROM coins", (MySqlConnection)connection);
            using var reader = cmd.ExecuteReader();

            var coinIds = new System.Collections.Generic.List<long>();

            while (reader.Read())
            {
                coinIds.Add(reader.GetInt64("id"));
            }

            foreach (var coinId in coinIds)
            {
                var prices = GetPriceHistory(coinId, 30);

                var ai = await GetAI(prices);

                SaveAI(coinId, ai);

                Console.WriteLine($"AI обновлён для монеты {coinId}");
            }
        }

        private List<decimal> GetPriceHistory(long coinId, int days)
        {
            var result = new System.Collections.Generic.List<decimal>();

            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                SELECT price 
                FROM coin_market_data
                WHERE coin_id = @coinId
                ORDER BY recorded_at DESC
                LIMIT @days
            ", (MySqlConnection)connection);

            cmd.Parameters.AddWithValue("@coinId", coinId);
            cmd.Parameters.AddWithValue("@days", days);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                result.Add(reader.GetDecimal("price"));
            }

            result.Reverse(); // чтобы были по порядку

            return result;
        }

        private async Task<AIResultModel> GetAI(List<decimal> prices)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                var response = await client.PostAsJsonAsync(
                    $"{_aiServiceSettings.BaseUrl.TrimEnd('/')}/analyze",
                    new { prices = prices }
                );

                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine("AI RESPONSE: " + content);

                if (!response.IsSuccessStatusCode)
                    return null;

                return JsonSerializer.Deserialize<AIResultModel>(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("AI ERROR: " + ex.Message);
                return null;
            }
        }

        private void SaveAI(long coinId, AIResultModel ai)
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                INSERT INTO coin_ai_analysis 
                (coin_id, period, trend, risk_level, forecast, explanation)
                VALUES (@coinId, '30d', @trend, @risk, @forecast, @explanation)
                ON DUPLICATE KEY UPDATE
                    trend = VALUES(trend),
                    risk_level = VALUES(risk_level),
                    forecast = VALUES(forecast),
                    explanation = VALUES(explanation);
            ", (MySqlConnection)connection);

            cmd.Parameters.AddWithValue("@coinId", coinId);
            cmd.Parameters.AddWithValue("@trend", ai?.Trend ?? "");
            cmd.Parameters.AddWithValue("@risk", ai?.Risk ?? "");
            cmd.Parameters.AddWithValue("@forecast", string.Join(",", ai?.Forecast ?? new System.Collections.Generic.List<decimal>()));
            cmd.Parameters.AddWithValue("@explanation", ai?.Explanation ?? "");

            cmd.ExecuteNonQuery();
        }
    }
}