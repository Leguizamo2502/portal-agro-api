using Entity.DTOs.Notifications;

namespace Business.Interfaces.Implements.Notification
{
    public interface INotificationPusher
    {
        Task PushToUserAsync(int userId, NotificationListItemDto dto, CancellationToken ct = default);
    }
}
