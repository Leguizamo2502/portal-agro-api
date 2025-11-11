using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Web.Hubs.Interfaces.Notifications;

namespace Web.Hubs.Implements.Notifications
{
    [Authorize] 
    public class NotificationHub : Hub<INotificationClient>
    {
        public override Task OnConnectedAsync()
        {
            // Opcional: logging
            return base.OnConnectedAsync();
        }
    }
}
