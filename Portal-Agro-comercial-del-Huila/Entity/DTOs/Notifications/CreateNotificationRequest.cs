namespace Entity.DTOs.Notifications
{
    public class CreateNotificationRequest
    {
        public int UserId { get; set; }
        public string Title { get; set; } 
        public string Message { get; set; } 
        public string? RelatedType { get; set; }
        public string? RelatedRoute { get; set; }
    }
}
