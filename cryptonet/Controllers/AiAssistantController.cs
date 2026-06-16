using cryptonet.Data;
using cryptonet.Filters;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Text;
using System.Text.Json;

namespace cryptonet.Controllers
{
    [RequireLogin]
    public class AiAssistantController : Controller
    {
        private readonly DbConnectionFactory _db;
        private readonly IHttpClientFactory _httpClientFactory;

        string apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
        public AiAssistantController(DbConnectionFactory db, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            
            var history = GetChatHistory(userId);
            return View(history);
        }

        [HttpPost]
        public async Task<IActionResult> Ask(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return RedirectToAction("Index");

            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            string userContext = GetUserContext(userId);

            var url = "https://api.groq.com/openai/v1/chat/completions";

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[]
                {
                    new { role = "system", content = $"Ты крипто-ассистент CryptoNet. Ты помогаешь анализировать портфель. Данные юзера: {userContext}. Отвечай на русском языке." },
                    new { role = "user", content = question }
                }
            };

            string answer;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseString);
                    answer = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content").GetString();

                    // СОХРАНЕНИЕ: записываем результат в твою таблицу анализа
                    int portfolioId = GetPortfolioId(userId);
                    if (portfolioId > 0)
                    {
                        SaveToDatabase(portfolioId, question, answer);
                    }
                }
                else
                {
                    answer = "Ошибка API: " + response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                answer = "Ошибка подключения: " + ex.Message;
            }

            // Перенаправляем на Index, чтобы увидеть обновленную историю
            return RedirectToAction("Index");
        }

        private string GetUserContext(int userId)
        {
            var sb = new StringBuilder();
            try
            {
                using var connection = (MySqlConnection)_db.CreateConnection();
                connection.Open();
                var cmd = new MySqlCommand(@"
                    SELECT c.symbol, pa.quantity 
                    FROM portfolio_assets pa
                    JOIN portfolios p ON pa.portfolio_id = p.id
                    JOIN coins c ON pa.coin_id = c.id
                    WHERE p.user_id = @userId", connection);
                cmd.Parameters.AddWithValue("@userId", userId);

                using var reader = cmd.ExecuteReader();
                sb.Append("Состав портфеля: ");
                while (reader.Read()) sb.Append($"{reader["symbol"]}: {reader["quantity"]}; ");
            }
            catch { return "Данные портфеля временно недоступны."; }

            return sb.Length > 15 ? sb.ToString() : "Портфель пуст.";
        }

        private int GetPortfolioId(int userId)
        {
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();
            var cmd = new MySqlCommand("SELECT id FROM portfolios WHERE user_id = @uid LIMIT 1", connection);
            cmd.Parameters.AddWithValue("@uid", userId);
            var res = cmd.ExecuteScalar();
            return res != null ? Convert.ToInt32(res) : 0;
        }

        private void SaveToDatabase(int portfolioId, string question, string answer)
        {
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();
            var cmd = new MySqlCommand(@"
                INSERT INTO portfolio_ai_analysis (portfolio_id, risk_level, explanation, recommendations) 
                VALUES (@pid, 'medium', @q, @a)", connection);
            cmd.Parameters.AddWithValue("@pid", portfolioId);
            cmd.Parameters.AddWithValue("@q", question);
            cmd.Parameters.AddWithValue("@a", answer);
            cmd.ExecuteNonQuery();
        }

        private List<dynamic> GetChatHistory(int userId)
        {
            var history = new List<dynamic>();
            try
            {
                using var connection = (MySqlConnection)_db.CreateConnection();
                connection.Open();
                var cmd = new MySqlCommand(@"
                    SELECT pa.explanation, pa.recommendations, pa.created_at 
                    FROM portfolio_ai_analysis pa
                    JOIN portfolios p ON pa.portfolio_id = p.id
                    WHERE p.user_id = @uid 
                    ORDER BY pa.created_at ASC", connection);
                cmd.Parameters.AddWithValue("@uid", userId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    history.Add(new
                    {
                        Question = reader["explanation"].ToString(),
                        Answer = reader["recommendations"].ToString(),
                        Date = Convert.ToDateTime(reader["created_at"]).ToString("HH:mm")
                    });
                }
            }
            catch { }
            return history;
        }
    }
}