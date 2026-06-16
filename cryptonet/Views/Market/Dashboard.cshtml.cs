using Microsoft.AspNetCore.Mvc.RazorPages;
using cryptonet.Services;

public class DashboardModel : PageModel
{
    private readonly CoinGeckoService _coinService;

    public List<CoinDto> Coins { get; set; } = new();

    public DashboardModel(CoinGeckoService coinService)
    {
        _coinService = coinService;
    }

    public async Task OnGetAsync()
    {
        Coins = await _coinService.GetTopCoins();
    }
}