using Business.Interfaces.Implements.Notification;
using Data.Interfaces.Implements.Notifications;
using Entity.Domain.Models.Implements.Notifications;
using Entity.DTOs.Notifications;

namespace Business.Services.Notifications
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _repo;

        public NotificationService(INotificationRepository repo)
        {
            _repo = repo;
        }

        public async Task<int> CreateAsync(CreateNotificationRequest request, CancellationToken ct = default)
        {
            if (request.UserId <= 0) throw new ArgumentException("UserId inválido");
            if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Title es obligatorio");
            if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("Message es obligatorio");

            var entity = new Notification
            {
                UserId = request.UserId,
                Title = request.Title.Trim(),
                Message = request.Message.Trim(),
                RelatedType = string.IsNullOrWhiteSpace(request.RelatedType) ? null : request.RelatedType.Trim(),
                RelatedRoute = string.IsNullOrWhiteSpace(request.RelatedRoute) ? null : request.RelatedRoute.Trim(),
                IsRead = false,
                ReadAtUtc = null,
                CreateAt = DateTime.UtcNow,
                IsDeleted = false,
                Active = true
            };

            var saved = await _repo.AddAsync(entity);
            return saved.Id;
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetUnreadAsync(int userId, int take = 20, CancellationToken ct = default)
        {
            var items = await _repo.GetUnreadAsync(userId, take, ct);
            return items.Select(MapToListItem).ToList();
        }

        public Task<int> CountUnreadAsync(int userId, CancellationToken ct = default)
        {
            return _repo.CountUnreadAsync(userId, ct);
        }

        public async Task<(IReadOnlyList<NotificationListItemDto> Items, int Total)> GetHistoryAsync(int userId, int page, int pageSize, CancellationToken ct = default)
        {
            var (entities, total) = await _repo.GetHistoryAsync(userId, page, pageSize, ct);
            var dtos = entities.Select(MapToListItem).ToList();
            return (dtos, total);
        }

        public Task<bool> MarkAsReadAsync(int id, int userId, CancellationToken ct = default)
        {
            return _repo.MarkAsReadAsync(id, userId, ct);
        }

        private static NotificationListItemDto MapToListItem(Notification n) => new()
        {
            Id = n.Id,
            Title = n.Title,
            Message = n.Message,
            IsRead = n.IsRead,
            CreateAt = n.CreateAt,
            RelatedType = n.RelatedType,
            RelatedRoute = n.RelatedRoute
        };
    }
}