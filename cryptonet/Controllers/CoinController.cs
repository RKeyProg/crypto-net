using cryptonet.Data;
using cryptonet.Filters;
using cryptonet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace cryptonet.Controllers
{
    [RequireLogin]
    public class CoinController : Controller
    {
        private readonly DbConnectionFactory _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AiServiceSettings _aiServiceSettings;

        public CoinController(
            DbConnectionFactory db,
            IHttpClientFactory httpClientFactory,
            IOptions<AiServiceSettings> aiServiceSettings)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _aiServiceSettings = aiServiceSettings.Value;
        }

        // 1. ГЛАВНЫЙ МЕТОД: Загружает страницу монеты
        public async Task<IActionResult> Details(long id)
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            // Берем основные данные монеты и самую последнюю цену
            var command = new MySqlCommand(@"
                SELECT c.id AS coin_id, c.symbol, c.name, c.market_rank, 
                       c.circulating_supply, c.max_supply,
                       m.price, m.percent_change_24h, m.market_cap, m.volume_24h
                FROM coins c
                LEFT JOIN coin_market_data m ON c.id = m.coin_id
                WHERE c.id = @coinId
                ORDER BY m.recorded_at DESC LIMIT 1", (MySqlConnection)connection);

            command.Parameters.AddWithValue("@coinId", id);

            using var reader = command.ExecuteReader();
            if (!reader.Read()) return NotFound();

            var coin = new MarketCoinViewModel
            {
                CoinId = reader.GetInt64("coin_id"),
                Symbol = reader.GetString("symbol"),
                Name = reader.GetString("name"),
                MarketRank = reader.IsDBNull("market_rank") ? null : reader.GetInt32("market_rank"),
                Price = reader.IsDBNull("price") ? 0 : reader.GetDecimal("price"),
                PercentChange24h = reader.IsDBNull("percent_change_24h") ? null : reader.GetDecimal("percent_change_24h"),
                MarketCap = reader.IsDBNull("market_cap") ? null : reader.GetDecimal("market_cap"),
                Volume24h = reader.IsDBNull("volume_24h") ? null : reader.GetDecimal("volume_24h"),
                CirculatingSupply = reader.IsDBNull("circulating_supply") ? null : reader.GetDecimal("circulating_supply"),
                MaxSupply = reader.IsDBNull("max_supply") ? null : reader.GetDecimal("max_supply")
            };
            reader.Close();

            // Получаем историю за 30 дней для графика
            var priceHistory = GetPriceHistory(id, 30);
            var prices = priceHistory.Select(p => p.Price).ToList();

            // Отправляем данные для Chart.js (графика) во View
            ViewBag.PriceLabels = priceHistory.Select(p => p.Time.ToString("yyyy-MM-dd")).ToList();
            ViewBag.PriceData = prices;

            // Спрашиваем Python про тренд и прогноз (Асинхронно!)
            var ai = await GetAIAnalysisAsync(prices);

            // Закидываем ответы AI в "мешок" ViewBag, чтобы показать на странице
            ViewBag.Trend = ai.Trend;
            ViewBag.Risk = ai.Risk;
            ViewBag.Forecast = ai.Forecast;
            ViewBag.Explanation = ai.Explanation;

            // Сохраняем результат анализа в базу (кэшируем)
            SaveAI(id, ai);

            return View(coin);
        }

        [HttpGet]
        public async Task<JsonResult> PriceHistoryJson(long id, int days)
        {
            try
            {
                // 1. Получаем историю из базы (этот метод у тебя уже есть)
                var history = GetPriceHistory(id, days);

                var labels = history.Select(p => p.Time.ToString("yyyy-MM-dd")).ToList();
                var prices = history.Select(p => p.Price).ToList();

                // 2. Спрашиваем Python про прогноз именно для этого набора цен
                // Используем твой асинхронный метод GetAIAnalysisAsync
                var ai = await GetAIAnalysisAsync(prices);

                // 3. Отдаем всё это браузеру
                return new JsonResult(new
                {
                    labels = labels,
                    prices = prices,
                    forecast = ai.Forecast // Наш прогноз от AI
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка в PriceHistoryJson: " + ex.Message);
                return new JsonResult(new { error = "Не удалось загрузить данные" });
            }
        }
        // 2. ВСПОМОГАТЕЛЬНЫЙ: Получает историю цен из БД
        private List<(DateTime Time, decimal Price)> GetPriceHistory(long coinId, int days)
        {
            var result = new List<(DateTime, decimal)>();
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                SELECT price, recorded_at FROM coin_market_data 
                WHERE coin_id = @coinId AND recorded_at >= @fromDate
                ORDER BY recorded_at ASC", (MySqlConnection)connection);

            cmd.Parameters.AddWithValue("@coinId", coinId);
            cmd.Parameters.Add("@fromDate", MySqlDbType.DateTime).Value = DateTime.UtcNow.AddDays(-days);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((reader.GetDateTime("recorded_at"), reader.GetDecimal("price")));
            }
            return result;
        }

        // 3. СВЯЗЬ С PYTHON: Один метод для всех AI-нужд
        private async Task<AIResultModel> GetAIAnalysisAsync(List<decimal> prices)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                // Стучимся во Flask
                var response = await client.PostAsJsonAsync($"{_aiServiceSettings.BaseUrl.TrimEnd('/')}/analyze", new { prices });

                if (!response.IsSuccessStatusCode) return GetDefaultAIResult("AI временно отдыхает");

                return await response.Content.ReadFromJsonAsync<AIResultModel>();
            }
            catch
            {
                return GetDefaultAIResult("Python-сервис не отвечает");
            }
        }

        private AIResultModel GetDefaultAIResult(string msg) => new AIResultModel
        {
            Trend = "neutral",
            Risk = "low",
            Forecast = new List<decimal>(),
            Explanation = msg
        };

        // 4. СОХРАНЕНИЕ: Записываем труды AI в базу
        private void SaveAI(long coinId, AIResultModel ai)
        {
            try
            {
                using var connection = _db.CreateConnection();
                connection.Open();
                var cmd = new MySqlCommand(@"
                    INSERT INTO coin_ai_analysis (coin_id, period, trend, risk_level, forecast, explanation)
                    VALUES (@coinId, '30d', @trend, @risk, @forecast, @explanation)
                    ON DUPLICATE KEY UPDATE
                        trend = VALUES(trend), risk_level = VALUES(risk_level),
                        forecast = VALUES(forecast), explanation = VALUES(explanation);", (MySqlConnection)connection);

                cmd.Parameters.AddWithValue("@coinId", coinId);
                cmd.Parameters.AddWithValue("@trend", ai.Trend ?? "neutral");
                cmd.Parameters.AddWithValue("@risk", ai.Risk ?? "low");
                cmd.Parameters.AddWithValue("@forecast", string.Join(",", ai.Forecast ?? new List<decimal>()));
                cmd.Parameters.AddWithValue("@explanation", ai.Explanation ?? "");
                cmd.ExecuteNonQuery();
            }
            catch { /* Если база занята, просто пропускаем сохранение */ }
        }
    }
}