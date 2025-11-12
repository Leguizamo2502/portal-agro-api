using System.ComponentModel.DataAnnotations;

namespace Entity.DTOs.Auth
{
    public class RefreshTokensRequestDto
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
