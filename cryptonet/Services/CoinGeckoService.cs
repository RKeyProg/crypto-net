namespace cryptonet.Services
{
    using System.Text.Json;
    using System.Text.Json.Serialization; // Добавили для атрибутов

    public class CoinGeckoService
    {
        private readonly HttpClient _http;

        public CoinGeckoService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<CoinDto>> GetTopCoins()
        {
            var url = "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=100&page=1&sparkline=false";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "cryptonet-app");
            request.Headers.Add("Accept", "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Ошибка от API: " + json);
                return new List<CoinDto>();
            }

            // ИСПРАВЛЕНИЕ: Добавляем поддержку чтения чисел из строк и гибкую десериализацию
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString |
                                 JsonNumberHandling.WriteAsString
            };

            var coins = JsonSerializer.Deserialize<List<CoinDto>>(json, options);

            Console.WriteLine($"Получено монет: {coins?.Count}");

            return coins ?? new List<CoinDto>();
        }
    }

    public class CoinDto
    {
        public string id { get; set; }
        public string symbol { get; set; }
        public string name { get; set; }
        public string image { get; set; }
        public double? current_price { get; set; }
        public double? price_change_percentage_24h { get; set; }
        public double? total_volume { get; set; }
        public double? market_cap { get; set; }
        public int market_cap_rank { get; set; }

        // Добавляем вот эти две строки:
        public double? circulating_supply { get; set; }
        public double? max_supply { get; set; }
    }
}