using Entity.DTOs.Notifications;

namespace Web.Hubs.Interfaces.Notifications
{
    public interface INotificationClient
    {
        Task NewNotification(NotificationListItemDto notification);
    }
}
