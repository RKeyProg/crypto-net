using cryptonet.Data;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace cryptonet.Controllers
{
    [Route("News")] // Это заставит контроллер отвечать на /News
    public class NewsController : Controller
    {
        private readonly DbConnectionFactory _db;

        public NewsController(DbConnectionFactory db) => _db = db;

        [HttpGet("")] // Это значит, что метод Index сработает на /News
        public IActionResult Index([FromQuery] string sentiment, [FromQuery] string search)
        {
            var news = new List<dynamic>();
            MarketAnalysisViewModel aiSummary = null;

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            var aiCmd = new MySqlCommand("SELECT sentiment, summary FROM market_ai_analysis ORDER BY created_at DESC LIMIT 1", connection);
            using (var aiReader = aiCmd.ExecuteReader())
            {
                if (aiReader.Read())
                {
                    aiSummary = new MarketAnalysisViewModel
                    {
                        Sentiment = aiReader.GetString("sentiment"),
                        Summary = aiReader.GetString("summary")
                    };
                }
            }

            // Заглушка, если анализа еще нет в базе
            if (aiSummary == null)
            {
                aiSummary = new MarketAnalysisViewModel
                {
                    Sentiment = "neutral",
                    Summary = "ИИ анализирует последние новости для формирования вердикта..."
                };
            }

            // 2. Запрос к новостям с фильтрами
            var sql = "SELECT title, summary, source, url, sentiment, published_at FROM news WHERE 1=1";

            if (!string.IsNullOrEmpty(sentiment))
                sql += " AND sentiment = @sentiment";

            if (!string.IsNullOrEmpty(search))
                sql += " AND (title LIKE @search OR summary LIKE @search)";

            sql += " ORDER BY published_at DESC";

            var newsCmd = new MySqlCommand(sql, connection);
            if (!string.IsNullOrEmpty(sentiment)) newsCmd.Parameters.AddWithValue("@sentiment", sentiment);
            if (!string.IsNullOrEmpty(search)) newsCmd.Parameters.AddWithValue("@search", "%" + search + "%");

            using (var reader = newsCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    news.Add(new
                    {
                        Title = reader.GetString("title"),
                        Summary = reader.GetString("summary"),
                        Source = reader.GetString("source"),
                        Url = reader.GetString("url"),
                        Sentiment = reader.GetString("sentiment"),
                        PublishedAt = reader.GetDateTime("published_at")
                    });
                }
            }

            ViewBag.AI = aiSummary;
            ViewBag.SelectedSentiment = sentiment;
            ViewBag.SearchQuery = search;

            return View(news);
        }
    }
    public class MarketAnalysisViewModel
    {
        public string Sentiment { get; set; }
        public string Summary { get; set; }
    }
}