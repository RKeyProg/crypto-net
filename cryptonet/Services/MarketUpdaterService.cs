using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using cryptonet.Data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace cryptonet.Services
{
    public class MarketUpdaterService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CoinGeckoService _coinGecko;

        public MarketUpdaterService(IServiceProvider serviceProvider, CoinGeckoService coinGecko)
        {
            _serviceProvider = serviceProvider;
            _coinGecko = coinGecko;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("MarketUpdaterService запущен");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateMarketData();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка обновления: " + ex.Message);
                }

                // ⏱ каждые 30 минут
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task UpdateMarketData()
        {
            Console.WriteLine("Обновление началось");
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();

            using var connection = db.CreateConnection();
            connection.Open();

            var coins = await _coinGecko.GetTopCoins();

            foreach (var coin in coins)
            {
                var symbol = coin.symbol.ToLower();

                using var getCoin = new MySqlCommand(
                    "SELECT id FROM coins WHERE symbol=@symbol",
                    (MySqlConnection)connection);

                getCoin.Parameters.AddWithValue("@symbol", symbol);

                var result = getCoin.ExecuteScalar();

                long coinId;

                // 👉 если монеты нет — добавляем
                if (result == null)
                {
                    using var insertCoin = new MySqlCommand(@"
    INSERT INTO coins (symbol, name, market_rank, circulating_supply, max_supply)
    VALUES (@symbol, @name, @rank, @cs, @ms);
", (MySqlConnection)connection);

                    insertCoin.Parameters.AddWithValue("@symbol", symbol);
                    insertCoin.Parameters.AddWithValue("@name", coin.name);
                    insertCoin.Parameters.AddWithValue("@rank", coin.market_cap_rank);
                    insertCoin.Parameters.AddWithValue("@cs", coin.circulating_supply ?? 0); // Сохраняем
                    insertCoin.Parameters.AddWithValue("@ms", coin.max_supply ?? 0);        // Сохраняем

                    insertCoin.ExecuteNonQuery();

                    // получаем id
                    using var getNewId = new MySqlCommand(
                        "SELECT id FROM coins WHERE symbol=@symbol",
                        (MySqlConnection)connection);

                    getNewId.Parameters.AddWithValue("@symbol", symbol);

                    coinId = Convert.ToInt64(getNewId.ExecuteScalar());
                }
                else
                {
                    coinId = Convert.ToInt64(result);
                    using var updateCoin = new MySqlCommand(@"
    UPDATE coins 
    SET market_rank = @rank, 
        circulating_supply = @cs, 
        max_supply = @ms
    WHERE id = @id
", (MySqlConnection)connection);

                    updateCoin.Parameters.AddWithValue("@rank", coin.market_cap_rank);
                    updateCoin.Parameters.AddWithValue("@cs", coin.circulating_supply ?? 0);
                    updateCoin.Parameters.AddWithValue("@ms", coin.max_supply ?? 0);
                    updateCoin.Parameters.AddWithValue("@id", coinId);

                    updateCoin.ExecuteNonQuery();
                }

                using var insert = new MySqlCommand(@"
                    INSERT INTO coin_market_data
                    (coin_id, price, market_cap, volume_24h, percent_change_24h, recorded_at)
                    VALUES (@coinId, @price, @marketCap, @volume, @change, NOW())
                    ON DUPLICATE KEY UPDATE
                        price = @price,
                        market_cap = @marketCap,
                        volume_24h = @volume,
                        percent_change_24h = @change,
                        recorded_at = NOW()
                ", (MySqlConnection)connection);

                insert.Parameters.AddWithValue("@coinId", coinId);
                insert.Parameters.AddWithValue("@price", coin.current_price);
                insert.Parameters.AddWithValue("@marketCap", coin.market_cap ?? 0);
                insert.Parameters.AddWithValue("@volume", coin.total_volume ?? 0);
                insert.Parameters.AddWithValue("@change", coin.price_change_percentage_24h);

                try
                {
                    insert.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка вставки: " + ex.Message);
                }
            }
        }
    }
}