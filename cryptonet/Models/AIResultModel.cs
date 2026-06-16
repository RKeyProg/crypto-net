namespace cryptonet.Models
{
    public class AIResultModel
    {
        public string Trend { get; set; }
        public List<decimal> Forecast { get; set; }
        public string Risk { get; set; }
        public string Explanation { get; set; }
    }
}
