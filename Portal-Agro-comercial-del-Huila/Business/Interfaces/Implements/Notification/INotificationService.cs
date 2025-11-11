using Entity.DTOs.Notifications;

namespace Business.Interfaces.Implements.Notification
{
    public interface INotificationService
    {
        Task<int> CreateAsync(CreateNotificationRequest request, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetUnreadAsync(int userId, int take = 20, CancellationToken ct = default);
        Task<int> CountUnreadAsync(int userId, CancellationToken ct = default);
        Task<(IReadOnlyList<NotificationListItemDto> Items, int Total)> GetHistoryAsync(int userId, int page, int pageSize, CancellationToken ct = default);
        Task<bool> MarkAsReadAsync(int id, int userId, CancellationToken ct = default);
    }
}
