using InventoryHold.Domain.Events;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InventoryHold.Tests.Outbox;

/// <summary>
/// Tests for <see cref="OutboxPublisherWorker"/> behaviour:
/// - Successful publish marks message as published
/// - Failed publish records failure without re-throwing
/// - Messages exceeding MaxRetries are skipped
/// - Unknown event types are recorded as failures
/// </summary>
public sealed class OutboxPublisherWorkerTests
{
    private readonly Mock<IOutboxStore> _outboxStore = new();
    private readonly Mock<IEventPublisher> _publisher = new();

    private OutboxPublisherWorker CreateWorker(int maxRetries = 3)
    {
        var settings = Options.Create(new OutboxSettings
        {
            BatchSize = 10,
            PollingIntervalSeconds = 1,
            MaxRetries = maxRetries
        });

        // Build a service provider that returns our mocks
        var services = new ServiceCollection();
        services.AddSingleton(_outboxStore.Object);
        services.AddScoped<IEventPublisher>(_ => _publisher.Object);
        var sp = services.BuildServiceProvider();

        return new OutboxPublisherWorker(
            sp.GetRequiredService<IServiceScopeFactory>(), settings, NullLogger<OutboxPublisherWorker>.Instance);
    }

    private static OutboxMessage BuildMessage(string eventType, int retryCount = 0)
    {
        var evt = new HoldCreatedEvent
        {
            HoldId = "h1",
            ProductId = "p1",
            CustomerId = "c1",
            Quantity = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        return new OutboxMessage
        {
            Id = evt.EventId,
            EventType = eventType,
            Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()),
            RetryCount = retryCount
        };
    }

    [Fact]
    public async Task ProcessBatch_SuccessfulPublish_MarksMessagePublished()
    {
        var message = BuildMessage(nameof(HoldCreatedEvent));

        _outboxStore.Setup(s => s.GetUnpublishedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);

        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Run the worker for a short time (it polls continuously)
        var worker = CreateWorker();

        // Use reflection to call the private ProcessBatchAsync directly via ExecuteAsync
        // Instead, we expose this indirectly by letting it run for one cycle
        try { await worker.StartAsync(cts.Token); } catch { }
        await Task.Delay(200); // let one cycle run
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        _outboxStore.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessBatch_FailedPublish_RecordsFailureAndDoesNotThrow()
    {
        var message = BuildMessage(nameof(HoldCreatedEvent));

        _outboxStore.Setup(s => s.GetUnpublishedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);

        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var worker = CreateWorker();

        try { await worker.StartAsync(cts.Token); } catch { }
        await Task.Delay(200);
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        _outboxStore.Verify(s => s.RecordFailureAsync(message.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _outboxStore.Verify(s => s.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBatch_ExceededMaxRetries_SkipsMessage()
    {
        var message = BuildMessage(nameof(HoldCreatedEvent), retryCount: 5);

        _outboxStore.Setup(s => s.GetUnpublishedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var worker = CreateWorker(maxRetries: 5);

        try { await worker.StartAsync(cts.Token); } catch { }
        await Task.Delay(200);
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxStore.Verify(s => s.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxStore.Verify(s => s.RecordFailureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBatch_UnknownEventType_RecordsFailure()
    {
        var message = BuildMessage("UnknownEventType");

        _outboxStore.Setup(s => s.GetUnpublishedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var worker = CreateWorker();

        try { await worker.StartAsync(cts.Token); } catch { }
        await Task.Delay(200);
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        _outboxStore.Verify(s => s.RecordFailureAsync(message.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
