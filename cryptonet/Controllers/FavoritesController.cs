using cryptonet.Data;
using cryptonet.Filters;
using cryptonet.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace cryptonet.Controllers
{
    [RequireLogin]
    public class FavoritesController : Controller
    {
        private readonly DbConnectionFactory _db;
        public FavoritesController(DbConnectionFactory db) => _db = db;

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            var favorites = new List<MarketCoinViewModel>();

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            // Простой запрос: только избранные монеты пользователя без всяких групп
            var command = new MySqlCommand(@"
        SELECT 
            c.id AS coin_id, c.name, c.symbol, m.price, m.percent_change_24h
        FROM user_favorites f
        JOIN coins c ON f.coin_id = c.id
        JOIN coin_market_data m ON m.coin_id = c.id
        WHERE f.user_id = @userId
        AND m.recorded_at = (SELECT MAX(recorded_at) FROM coin_market_data WHERE coin_id = c.id)
        ORDER BY c.name ASC
    ", connection);

            command.Parameters.AddWithValue("@userId", userId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                favorites.Add(new MarketCoinViewModel
                {
                    CoinId = reader.GetInt64("coin_id"),
                    Name = reader.GetString("name"),
                    Symbol = reader.GetString("symbol"),
                    Price = reader.GetDecimal("price"),
                    PercentChange24h = reader.IsDBNull(reader.GetOrdinal("percent_change_24h")) ? 0 : reader.GetDecimal("percent_change_24h")
                });
            }

            return View(favorites); // Теперь возвращаем обычный List
        }

        // Метод для создания новой группы
        [HttpPost]
        public IActionResult CreateGroup(string groupName)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();
            var cmd = new MySqlCommand("INSERT INTO favorite_groups (user_id, name) VALUES (@uid, @name)", connection);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@name", groupName);
            cmd.ExecuteNonQuery();
            return RedirectToAction("Index");
        }


        [HttpPost]
        public IActionResult Add(long coinId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            // Просто добавляем монету в избранное пользователя
            var command = new MySqlCommand(@"
        INSERT IGNORE INTO user_favorites (user_id, coin_id) 
        VALUES (@userId, @coinId)", connection);

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@coinId", coinId);
            command.ExecuteNonQuery();

            return Redirect(Request.Headers["Referer"].ToString() ?? "/Market/Dashboard");
        }

        [HttpPost]
        public IActionResult Remove(long coinId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            var command = new MySqlCommand("DELETE FROM user_favorites WHERE user_id = @userId AND coin_id = @coinId", (MySqlConnection)connection);
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@coinId", coinId);
            command.ExecuteNonQuery();

            return Redirect(Request.Headers["Referer"].ToString() ?? "/Favorites/Index");
        }
    }
}