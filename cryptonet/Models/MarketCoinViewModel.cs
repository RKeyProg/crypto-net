namespace cryptonet.Models
{
    public class MarketCoinViewModel
    {
        public long CoinId { get; set; }
        public string Symbol { get; set; }
        public string Name { get; set; }
        public int? MarketRank { get; set; }

        // Цена и торговые данные
        public decimal Price { get; set; }
        public decimal? PercentChange24h { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? Volume24h { get; set; }

        // Основные характеристики
        public decimal? CirculatingSupply { get; set; }
        public decimal? MaxSupply { get; set; }

        // AI-анализ
        public string Trend { get; set; }
        public string RiskLevel { get; set; }
        public string Forecast { get; set; }
        public string Explanation { get; set; }

        public bool IsFavorite { get; set; }
    }
}
