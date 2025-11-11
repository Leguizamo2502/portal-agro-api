using Entity.DTOs.BaseDTO;

namespace Entity.DTOs.Notifications
{
    public class NotificationListItemDto : BaseDto
    {
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public bool IsRead { get; set; }
        public DateTime CreateAt { get; set; }
        public string? RelatedType { get; set; }
        public string? RelatedRoute { get; set; }
    }
}
