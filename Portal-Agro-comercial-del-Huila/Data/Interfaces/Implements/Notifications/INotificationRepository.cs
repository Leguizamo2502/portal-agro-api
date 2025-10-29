using Data.Interfaces.IRepository;
using Entity.Domain.Models.Implements.Notifications;

namespace Data.Interfaces.Implements.Notifications
{
    public interface INotificationRepository : IDataGeneric<Notification>
    {
    }
}
