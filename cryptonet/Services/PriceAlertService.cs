using cryptonet.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace cryptonet.Services
{
    public class PriceAlertService : BackgroundService
    {
        private readonly DbConnectionFactory _db;

        public PriceAlertService(DbConnectionFactory db)
        {
            _db = db;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckAlerts();
                await Task.Delay(15000, stoppingToken); // каждые 15 сек
            }
        }

        private async Task CheckAlerts()
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            var cmd = new MySqlCommand(@"
                SELECT pa.id, pa.coin_id, pa.target_price, pa.condition_type, m.price
                FROM price_alerts pa
                JOIN coin_market_data m ON pa.coin_id = m.coin_id
                WHERE pa.is_triggered = 0
            ", (MySqlConnection)connection);

            using var reader = cmd.ExecuteReader();

            var toTrigger = new List<long>();

            while (reader.Read())
            {
                long id = reader.GetInt64("id");
                decimal target = reader.GetDecimal("target_price");
                decimal current = reader.GetDecimal("price");
                string condition = reader.GetString("condition_type");

                bool triggered =
                    (condition == "above" && current >= target) ||
                    (condition == "below" && current <= target);

                if (triggered)
                    toTrigger.Add(id);
            }

            reader.Close();

            foreach (var id in toTrigger)
            {
                var update = new MySqlCommand(@"
                    UPDATE price_alerts
                    SET is_triggered = 1
                    WHERE id = @id
                ", (MySqlConnection)connection);

                update.Parameters.AddWithValue("@id", id);
                update.ExecuteNonQuery();
            }
        }
    }
}