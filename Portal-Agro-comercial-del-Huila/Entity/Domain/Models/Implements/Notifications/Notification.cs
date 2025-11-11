using Entity.Domain.Models.Base;
using Entity.Domain.Models.Implements.Auth;

namespace Entity.Domain.Models.Implements.Notifications
{
    public class Notification : BaseModel
    {

        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;

        public bool IsRead { get; set; }
        public DateTime? ReadAtUtc { get; set; }

        // Contexto opcional
        public string? RelatedType { get; set; }
        public string? RelatedRoute { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }
    }

}
