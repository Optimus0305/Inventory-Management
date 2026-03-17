using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryHold.WebApi.Workers;

/// <summary>
/// Background service that periodically queries for Active holds whose ExpiresAt
/// has elapsed and expires them, restoring inventory and publishing HoldExpired events.
///
/// This is the "proactive" expiry strategy; a complementary lazy check exists
/// in <see cref="HoldService.GetHoldAsync"/> for reads that happen before the worker runs.
///
/// TRADEOFFS vs lazy-only expiry:
/// ─────────────────────────────
/// + Inventory is freed promptly even if no one reads the hold.
/// + Downstream consumers get HoldExpired events in a timely manner.
/// - Requires a polling loop (slight overhead, acceptable at low frequency).
/// </summary>
public sealed class HoldExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HoldExpirySettings _settings;
    private readonly ILogger<HoldExpiryWorker> _logger;

    public HoldExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<HoldExpirySettings> settings,
        ILogger<HoldExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HoldExpiryWorker started (polling every {Interval}s)", _settings.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredHoldsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during hold expiry processing");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessExpiredHoldsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var holdRepo = scope.ServiceProvider.GetRequiredService<IHoldRepository>();
        var holdService = scope.ServiceProvider.GetRequiredService<HoldService>();

        var expiredHolds = await holdRepo.GetExpiredHoldsAsync(ct);

        if (expiredHolds.Count == 0)
            return;

        _logger.LogInformation("Expiring {Count} holds", expiredHolds.Count);

        foreach (var hold in expiredHolds)
        {
            try
            {
                await holdService.ExpireHoldAsync(hold, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire hold {HoldId}", hold.Id);
            }
        }
    }
}
