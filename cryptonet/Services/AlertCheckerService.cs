using cryptonet.Data;
using cryptonet.Hubs; // Добавь свой неймспейс хаба
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;

namespace cryptonet.Services
{
    public class AlertCheckerService : BackgroundService
    {
        private readonly DbConnectionFactory _db;
        private readonly IHubContext<AlertHub> _hubContext; // Исправлено

        public AlertCheckerService(DbConnectionFactory db, IHubContext<AlertHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await CheckAlerts(); }
                catch (Exception ex) { Console.WriteLine("AlertChecker error: " + ex.Message); }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task CheckAlerts()
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                SELECT pa.id, pa.coin_id, pa.condition_type, pa.target_price, m.price AS current_price
                FROM price_alerts pa
                JOIN coin_market_data m ON m.coin_id = pa.coin_id
                AND m.recorded_at = (SELECT MAX(recorded_at) FROM coin_market_data WHERE coin_id = pa.coin_id)
                WHERE pa.is_triggered = 0;", (MySqlConnection)connection);

            var alerts = new List<(long id, long coinId, string condition, decimal target, decimal current)>();

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    alerts.Add((reader.GetInt64("id"), reader.GetInt64("coin_id"),
                               reader.GetString("condition_type"), reader.GetDecimal("target_price"),
                               reader.GetDecimal("current_price")));
                }
            }

            foreach (var a in alerts)
            {
                bool triggered = (a.condition == "above" && a.current >= a.target) ||
                                 (a.condition == "below" && a.current <= a.target);

                if (!triggered) continue;

                var update = new MySqlCommand("UPDATE price_alerts SET is_triggered = 1 WHERE id = @id", (MySqlConnection)connection);
                update.Parameters.AddWithValue("@id", a.id);
                update.ExecuteNonQuery();

                // SignalR уведомление
                await _hubContext.Clients.All.SendAsync("alertTriggered", $"Монета {a.coinId} достигла цены {a.current}$");
            }
        }
    }
}