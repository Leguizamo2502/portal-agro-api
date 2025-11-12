using System.ComponentModel.DataAnnotations;

namespace Entity.DTOs.Auth
{
    public class LogoutRequestDto
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
