using Data.Interfaces.Implements.Notifications;
using Data.Repository;
using Entity.Domain.Models.Implements.Notifications;
using Entity.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Data.Service.Notifications
{
    public class NotificationRepository : DataGeneric<Notification>, INotificationRepository
    {
        public NotificationRepository(ApplicationDbContext context) : base(context)
        {
        }
        public async Task<IReadOnlyList<Notification>> GetUnreadAsync(int userId, int take = 20, CancellationToken ct = default)
        {
            return await _dbSet
                .AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsDeleted && n.Active && !n.IsRead)
                .OrderByDescending(n => n.CreateAt) 
                .Take(take)
                .ToListAsync(ct);
        }

        public Task<int> CountUnreadAsync(int userId, CancellationToken ct = default)
        {
            return _dbSet
                .AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsDeleted && n.Active && !n.IsRead)
                .CountAsync(ct);
        }

        public async Task<(IReadOnlyList<Notification> Items, int Total)> GetHistoryAsync(
            int userId, int page, int pageSize, CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var baseQuery = _dbSet
                .AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsDeleted && n.Active);

            var total = await baseQuery.CountAsync(ct);

            var items = await baseQuery
                .OrderByDescending(n => n.CreateAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (items, total);
        }

        public async Task<bool> MarkAsReadAsync(int id, int userId, CancellationToken ct = default)
        {
            var n = await _dbSet.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (n == null) return false;

            if (!n.IsRead)
            {
                n.IsRead = true;
                n.ReadAtUtc = DateTime.UtcNow;
                _dbSet.Update(n);
                return await _context.SaveChangesAsync(ct) > 0;
            }
            return true; // ya estaba leída
        }

        public override async Task<Notification> AddAsync(Notification entity)
        {
            entity.IsRead = false;
            entity.ReadAtUtc = null;

            entity.CreateAt = DateTime.UtcNow;


            entity.IsDeleted = false;
            entity.Active = true;

            await base.AddAsync(entity);
            return entity;
        }
    }
}