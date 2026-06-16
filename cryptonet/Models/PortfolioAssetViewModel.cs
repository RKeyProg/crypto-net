namespace cryptonet.Models
{
    public class PortfolioAssetViewModel
    {
        public long CoinId { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }

        public decimal Quantity { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal CurrentPrice { get; set; }

        // Расчёты
        public decimal CurrentValue => Quantity * CurrentPrice;
        public decimal ProfitLoss => CurrentValue - (Quantity * BuyPrice);
    }
}