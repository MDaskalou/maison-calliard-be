using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Domain.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task RevokeActiveAsync(Guid adminUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);
}
