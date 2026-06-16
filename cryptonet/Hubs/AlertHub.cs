using Microsoft.AspNetCore.SignalR;

namespace cryptonet.Hubs
{
    // Этот класс может быть пустым, он служит "воротами" для трафика SignalR
    public class AlertHub : Hub
    {
        // Здесь можно добавлять методы, если нужно, чтобы клиент слал данные серверу
        // Но для уведомлений нам достаточно пустого класса
    }
}