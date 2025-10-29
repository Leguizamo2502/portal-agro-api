using Data.Interfaces.Implements.Notifications;
using Data.Repository;
using Entity.Domain.Models.Implements.Notifications;
using Entity.Infrastructure.Context;

namespace Data.Service.Notifications
{
    public class NotificationRepository : DataGeneric<Notification>, INotificationRepository
    {
        public NotificationRepository(ApplicationDbContext context) : base(context)
        {
        }
    }
}
