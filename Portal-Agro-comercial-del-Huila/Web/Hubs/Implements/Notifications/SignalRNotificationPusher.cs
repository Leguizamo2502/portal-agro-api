using Business.Interfaces.Implements.Notification;
using Entity.DTOs.Notifications;
using Microsoft.AspNetCore.SignalR;
using Web.Hubs.Interfaces.Notifications;

namespace Web.Hubs.Implements.Notifications
{
    public class SignalRNotificationPusher : INotificationPusher
    {
        private readonly IHubContext<NotificationHub, INotificationClient> _hub;

        public SignalRNotificationPusher(IHubContext<NotificationHub, INotificationClient> hub)
        {
            _hub = hub;
        }

        public Task PushToUserAsync(int userId, NotificationListItemDto dto, CancellationToken ct = default)
        {
            // Clients.User(...) matchea el claim NameIdentifier/sub del token
            return _hub.Clients.User(userId.ToString()).NewNotification(dto);
        }
    }
}
