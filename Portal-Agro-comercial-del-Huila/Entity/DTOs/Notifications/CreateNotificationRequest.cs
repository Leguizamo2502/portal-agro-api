namespace Entity.DTOs.Notifications
{
    public class CreateNotificationRequest
    {
        public int UserId { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string? RelatedType { get; set; }
        public string? RelatedRoute { get; set; }
    }
}
