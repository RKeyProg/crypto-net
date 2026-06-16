using cryptonet.Data;
using cryptonet.Filters;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace cryptonet.Controllers
{
    [RequireLogin]
    [ServiceFilter(typeof(AuthFilter))]
    public class AlertsController : Controller
    {
        private readonly DbConnectionFactory _db;

        public AlertsController(DbConnectionFactory db)
        {
            _db = db;
        }

        // =======================
        // LIST PAGE
        // =======================
        public IActionResult Index()
        {
            var alerts = new List<dynamic>();
            var coins = new List<dynamic>();

            using var connection = _db.CreateConnection();
            connection.Open();

            // ALERTS
            var cmd = new MySqlCommand(@"
                SELECT 
                    pa.id,
                    c.name,
                    c.symbol,
                    pa.condition_type,
                    pa.target_price,
                    m.price AS current_price,
                    pa.is_triggered
                FROM price_alerts pa
                JOIN coins c ON pa.coin_id = c.id
                JOIN coin_market_data m 
                    ON m.coin_id = c.id 
                    AND m.recorded_at = (
                        SELECT MAX(recorded_at) 
                        FROM coin_market_data 
                        WHERE coin_id = c.id
                    )
                ORDER BY pa.created_at DESC;
            ", (MySqlConnection)connection);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    alerts.Add(new
                    {
                        Id = reader.GetInt64("id"),
                        Coin = reader.GetString("name"),
                        Symbol = reader.GetString("symbol"),
                        Condition = reader.GetString("condition_type"),
                        TargetPrice = reader.GetDecimal("target_price"),
                        CurrentPrice = reader.GetDecimal("current_price"),
                        IsTriggered = reader.GetBoolean("is_triggered")
                    });
                }
            }

            // COINS
            var coinsCmd = new MySqlCommand("SELECT id, name, symbol FROM coins", (MySqlConnection)connection);
            using (var reader = coinsCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    coins.Add(new
                    {
                        Id = reader.GetInt64("id"),
                        Name = reader.GetString("name"),
                        Symbol = reader.GetString("symbol")
                    });
                }
            }

            ViewBag.Coins = coins;

            return View(alerts);
        }

        // =======================
        // GET CURRENT PRICE
        // =======================
        [HttpGet]
        public IActionResult GetCurrentPrice(long coinId)
        {
            using var connection = _db.CreateConnection();
            connection.Open();
            var cmd = new MySqlCommand("SELECT price FROM coin_market_data WHERE coin_id = @id ORDER BY recorded_at DESC LIMIT 1", (MySqlConnection)connection);
            cmd.Parameters.AddWithValue("@id", coinId);

            var result = cmd.ExecuteScalar();
            // Если в базе нет цен для этой монеты, вернем 0
            decimal price = result != DBNull.Value && result != null ? Convert.ToDecimal(result) : 0m;

            return Json(new { price = price });
        }

        // =======================
        // CREATE ALERT (POST)
        // =======================
        [HttpPost]
        public IActionResult Create(long coinId, string conditionType, string targetPrice) // Принимаем string
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            // Парсим строку в decimal принудительно через точку
            if (!decimal.TryParse(targetPrice.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal finalPrice))
            {
                // Если не удалось распарсить, возвращаемся назад (или логируем ошибку)
                return RedirectToAction("Index");
            }

            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
        INSERT INTO price_alerts 
        (user_id, coin_id, condition_type, target_price, is_triggered, is_read, created_at)
        VALUES 
        (@userId, @coinId, @conditionType, @targetPrice, 0, 0, NOW());
    ", (MySqlConnection)connection);

            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@coinId", coinId);
            cmd.Parameters.AddWithValue("@conditionType", conditionType);
            cmd.Parameters.AddWithValue("@targetPrice", finalPrice); // Здесь уже число

            cmd.ExecuteNonQuery();

            return RedirectToAction("Index");
        }


        // =======================
        // DELETE ALERT
        // =======================
        [HttpPost]
        public IActionResult Delete(long id)
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                DELETE FROM price_alerts WHERE id = @id
            ", (MySqlConnection)connection);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction("Index");
        }
    }
}