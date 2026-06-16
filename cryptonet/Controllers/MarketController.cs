using cryptonet.Data;
using cryptonet.Filters;
using cryptonet.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace cryptonet.Controllers
{
    [ServiceFilter(typeof(AuthFilter))]
    public class MarketController : Controller
    {
        private readonly DbConnectionFactory _db;

        public MarketController(DbConnectionFactory db)
        {
            _db = db;
        }

        public IActionResult Dashboard(string search = "", string sortBy = "")
        {
            var coins = new List<MarketCoinViewModel>();
            // В методе Dashboard
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            
            using var connection = _db.CreateConnection();
            connection.Open();

            // ОПТИМИЗИРОВАННЫЙ SQL: Мы используем фильтр прямо в JOIN, чтобы не грузить лишнее
            string sql = @"
        SELECT 
            c.id AS coin_id,
            c.symbol,
            c.name,
            c.market_rank,
            m.price,
            m.percent_change_24h,
            m.volume_24h,
            m.market_cap,
            CASE WHEN f.coin_id IS NULL THEN 0 ELSE 1 END AS is_favorite
        FROM coins c
        INNER JOIN coin_market_data m ON m.coin_id = c.id
        LEFT JOIN user_favorites f ON f.coin_id = c.id AND f.user_id = @userId
        WHERE m.recorded_at = (SELECT MAX(recorded_at) FROM coin_market_data WHERE coin_id = c.id)
    ";

            // Если есть поиск, добавляем его ПРЯМО в запрос к базе (так быстрее)
            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (c.name LIKE @search OR c.symbol LIKE @search)";
            }

            // Сортировка прямо в базе (MySQL делает это мгновенно)
            sql += sortBy switch
            {
                "price" => " ORDER BY m.price DESC",
                "change" => " ORDER BY m.percent_change_24h DESC",
                "marketcap" => " ORDER BY m.market_cap DESC",
                _ => " ORDER BY c.market_rank ASC"
            };

            using var command = new MySqlCommand(sql, (MySqlConnection)connection);
            command.CommandTimeout = 60;
            command.Parameters.AddWithValue("@userId", userId);

            if (!string.IsNullOrEmpty(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                coins.Add(new MarketCoinViewModel
                {
                    CoinId = reader.GetInt64("coin_id"),
                    Symbol = reader.GetString("symbol"),
                    Name = reader.GetString("name"),
                    MarketRank = reader.IsDBNull("market_rank") ? null : reader.GetInt32("market_rank"),
                    Price = reader.IsDBNull("price") ? 0 : reader.GetDecimal("price"),
                    Volume24h = reader.IsDBNull("volume_24h") ? null : reader.GetDecimal("volume_24h"),
                    PercentChange24h = reader.IsDBNull("percent_change_24h") ? null : reader.GetDecimal("percent_change_24h"),
                    MarketCap = reader.IsDBNull("market_cap") ? null : reader.GetDecimal("market_cap"),
                    IsFavorite = reader.GetInt32("is_favorite") == 1
                });
            }

            return View(coins);
        }
        
    }

}