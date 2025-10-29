namespace Entity.Domain.Models.Implements.Auth.Token
{
    public class JwtSettings
    {
        public string Key { get; set; } = null!;
        public string Issuer { get; set; } = "WebPortalAgro.API";
        public string Audience { get; set; } = "PortalAgro.Client";
        public int AccessTokenExpirationMinutes { get; set; } = 15;
        public int RefreshTokenExpirationDays { get; set; } = 7;
    }
}

