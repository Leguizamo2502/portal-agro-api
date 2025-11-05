using Data.Interfaces.IRepository;
using Entity.Domain.Models.Implements.Notifications;

namespace Data.Interfaces.Implements.Notifications
{
    public interface INotificationRepository : IDataGeneric<Notification>
    {
        Task<IReadOnlyList<Notification>> GetUnreadAsync(int userId, int take = 20, CancellationToken ct = default);
        Task<int> CountUnreadAsync(int userId, CancellationToken ct = default);

        Task<(IReadOnlyList<Notification> Items, int Total)> GetHistoryAsync(
            int userId, int page, int pageSize, CancellationToken ct = default);

        Task<bool> MarkAsReadAsync(int id, int userId, CancellationToken ct = default);
    }
}
