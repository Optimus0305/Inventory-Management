using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Outbox;
using InventoryHold.Infrastructure.Persistence;
using InventoryHold.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryHold.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services into the DI container.
/// Keeps the WebApi project's Program.cs clean and enforces layer boundaries.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Settings ─────────────────────────────────────────────────────────
        services.Configure<MongoDbSettings>(configuration.GetSection(MongoDbSettings.SectionName));
        services.Configure<RabbitMqSettings>(configuration.GetSection(RabbitMqSettings.SectionName));
        services.Configure<OutboxSettings>(configuration.GetSection(OutboxSettings.SectionName));
        services.Configure<HoldExpirySettings>(configuration.GetSection(HoldExpirySettings.SectionName));

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddSingleton<IHoldRepository, MongoHoldRepository>();
        services.AddSingleton<IInventoryRepository, MongoInventoryRepository>();

        // ── Outbox ────────────────────────────────────────────────────────────
        services.AddSingleton<IOutboxStore, MongoOutboxStore>();

        // ── Event publisher ───────────────────────────────────────────────────
        // Registered as Scoped so each scope (e.g., per-cycle in the worker) gets
        // its own instance with a fresh connection if needed.
        services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();

        // ── Application service ───────────────────────────────────────────────
        services.AddScoped<HoldService>();

        // ── Background workers ────────────────────────────────────────────────
        services.AddHostedService<OutboxPublisherWorker>();

        return services;
    }
}
