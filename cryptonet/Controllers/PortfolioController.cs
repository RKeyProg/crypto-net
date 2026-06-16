using cryptonet.Data;
using cryptonet.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using cryptonet.Filters;
using Microsoft.AspNetCore.Http;

namespace cryptonet.Controllers
{
    [RequireLogin]
    [ServiceFilter(typeof(AuthFilter))]
    public class PortfolioController : Controller
    {
        private readonly DbConnectionFactory _db;

        public PortfolioController(DbConnectionFactory db)
        {
            _db = db;
        }

        // ========================================================
        // 1. ГЛАВНАЯ СТРАНИЦА ПОРТФЕЛЯ
        // ========================================================
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            var assets = new List<PortfolioAssetViewModel>();
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            // Получаем или создаем ID портфеля
            long portfolioId = GetOrCreatePortfolioId(connection, userId.Value);

            // Запрос активов с актуальными ценами из coin_market_data
            var cmd = new MySqlCommand(@"
                SELECT 
                    pa.coin_id, 
                    pa.quantity, 
                    pa.buy_price, 
                    c.name, 
                    c.symbol, 
                    m.price AS current_price
                FROM portfolio_assets pa
                JOIN coins c ON pa.coin_id = c.id
                JOIN coin_market_data m ON c.id = m.coin_id
                WHERE pa.portfolio_id = @pid
                AND m.recorded_at = (
                    SELECT MAX(recorded_at) 
                    FROM coin_market_data 
                    WHERE coin_id = c.id
                )", connection);

            cmd.Parameters.AddWithValue("@pid", portfolioId);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    assets.Add(new PortfolioAssetViewModel
                    {
                        CoinId = Convert.ToInt64(reader["coin_id"]),
                        Name = reader["name"].ToString(),
                        Symbol = reader["symbol"].ToString(),
                        Quantity = Convert.ToDecimal(reader["quantity"]),
                        BuyPrice = Convert.ToDecimal(reader["buy_price"]),
                        CurrentPrice = Convert.ToDecimal(reader["current_price"])
                    });
                }
            }

            // Сортируем: самые дорогие активы вверху
            var sortedAssets = assets.OrderByDescending(a => a.CurrentValue).ToList();

            ViewBag.TotalValue = sortedAssets.Sum(a => a.CurrentValue);
            ViewBag.TotalProfitLoss = sortedAssets.Sum(a => a.ProfitLoss);

            // Список всех доступных монет для выпадающего списка в модалке
            var allCoins = new List<dynamic>();
            var coinCmd = new MySqlCommand("SELECT id, name, symbol FROM coins ORDER BY name", connection);
            using (var coinReader = coinCmd.ExecuteReader())
            {
                while (coinReader.Read())
                {
                    allCoins.Add(new
                    {
                        Id = Convert.ToInt64(coinReader["id"]),
                        Name = coinReader["name"].ToString(),
                        Symbol = coinReader["symbol"].ToString()
                    });
                }
            }

            ViewBag.AllCoins = allCoins;
            return View(sortedAssets);
        }

        // ========================================================
        // 2. ДОБАВЛЕНИЕ / DCA (Усреднение цены)
        // ========================================================
        [HttpPost]
        public IActionResult Add(long coinId, decimal quantity, decimal buyPrice)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            long portfolioId = GetOrCreatePortfolioId(connection, userId.Value);

            // Проверяем, есть ли уже такая монета в портфеле
            var checkCmd = new MySqlCommand(@"
                SELECT id, quantity, buy_price 
                FROM portfolio_assets 
                WHERE portfolio_id = @pid AND coin_id = @cid", connection);

            checkCmd.Parameters.AddWithValue("@pid", portfolioId);
            checkCmd.Parameters.AddWithValue("@cid", coinId);

            using var reader = checkCmd.ExecuteReader();
            if (reader.Read())
            {
                // Если есть — усредняем (DCA)
                var assetId = Convert.ToInt64(reader["id"]);
                var oldQty = Convert.ToDecimal(reader["quantity"]);
                var oldPrice = Convert.ToDecimal(reader["buy_price"]);
                reader.Close();

                var newQty = oldQty + quantity;
                var newAvgPrice = ((oldQty * oldPrice) + (quantity * buyPrice)) / newQty;

                var updateCmd = new MySqlCommand(@"
                    UPDATE portfolio_assets 
                    SET quantity = @q, buy_price = @p 
                    WHERE id = @id", connection);

                updateCmd.Parameters.AddWithValue("@q", newQty);
                updateCmd.Parameters.AddWithValue("@p", newAvgPrice);
                updateCmd.Parameters.AddWithValue("@id", assetId);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                // Если нет — просто добавляем
                reader.Close();
                var insertCmd = new MySqlCommand(@"
                    INSERT INTO portfolio_assets (portfolio_id, coin_id, quantity, buy_price)
                    VALUES (@pid, @cid, @q, @p)", connection);

                insertCmd.Parameters.AddWithValue("@pid", portfolioId);
                insertCmd.Parameters.AddWithValue("@cid", coinId);
                insertCmd.Parameters.AddWithValue("@q", quantity);
                insertCmd.Parameters.AddWithValue("@p", buyPrice);
                insertCmd.ExecuteNonQuery();
            }

            TempData["Success"] = "Актив успешно добавлен";
            return RedirectToAction("Index");
        }

        // ========================================================
        // 3. РЕДАКТИРОВАНИЕ И УДАЛЕНИЕ
        // ========================================================
        [HttpPost]
        public IActionResult Update(long coinId, decimal quantity, decimal buyPrice)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                UPDATE portfolio_assets pa
                JOIN portfolios p ON pa.portfolio_id = p.id
                SET pa.quantity = @q, pa.buy_price = @p
                WHERE p.user_id = @uid AND pa.coin_id = @cid", connection);

            cmd.Parameters.AddWithValue("@q", quantity);
            cmd.Parameters.AddWithValue("@p", buyPrice);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@cid", coinId);

            cmd.ExecuteNonQuery();
            TempData["Success"] = "Актив обновлен";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult Delete(long coinId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                DELETE pa FROM portfolio_assets pa
                JOIN portfolios p ON pa.portfolio_id = p.id
                WHERE p.user_id = @uid AND pa.coin_id = @cid", connection);

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@cid", coinId);

            cmd.ExecuteNonQuery();
            return RedirectToAction("Index");
        }

        // ========================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ========================================================
        private long GetOrCreatePortfolioId(MySqlConnection conn, int userId)
        {
            var cmd = new MySqlCommand("SELECT id FROM portfolios WHERE user_id = @uid", conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            var result = cmd.ExecuteScalar();

            if (result != null) return Convert.ToInt64(result);

            var insertCmd = new MySqlCommand("INSERT INTO portfolios (user_id) VALUES (@uid); SELECT LAST_INSERT_ID();", conn);
            insertCmd.Parameters.AddWithValue("@uid", userId);
            return Convert.ToInt64(insertCmd.ExecuteScalar());
        }
    }
}