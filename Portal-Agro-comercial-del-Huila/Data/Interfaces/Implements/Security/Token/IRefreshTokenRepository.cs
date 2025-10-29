﻿using Entity.Domain.Models.Implements.Auth.Token;

namespace Data.Interfaces.Implements.Security.Token
{
    public interface IRefreshTokenRepository
    {
        Task AddAsync(RefreshToken token);
        Task<RefreshToken?> GetByHashAsync(string tokenHash);
        Task RevokeAsync(RefreshToken token, string? replacedByTokenHash = null);
        Task<IEnumerable<RefreshToken>> GetValidTokensByUserAsync(int userId);
    }
}
