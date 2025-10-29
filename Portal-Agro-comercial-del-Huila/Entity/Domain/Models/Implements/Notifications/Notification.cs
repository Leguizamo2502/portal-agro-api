﻿using Entity.Domain.Models.Base;
using Entity.Domain.Models.Implements.Auth;

namespace Entity.Domain.Models.Implements.Notifications
{
    public class Notification : BaseModel
    {
        public int UserId { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public bool IsRead { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public User? User { get; set; }
    }

}
