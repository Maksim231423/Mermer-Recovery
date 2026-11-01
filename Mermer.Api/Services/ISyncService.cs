using Mermer.Api.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Services;

public interface ISyncService
{
    Task<SyncPullResponseDto> ProcessPullAsync(string? clientId = null, CancellationToken cancellationToken = default);
    Task<SyncPushResponseDto> ProcessPushAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default);
}