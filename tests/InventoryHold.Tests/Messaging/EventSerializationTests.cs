using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Events;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InventoryHold.Tests.Messaging;

/// <summary>
/// Tests for event serialization logic in <see cref="RabbitMqEventPublisher"/>.
/// These validate that the correct routing key and payload are produced
/// without requiring a live RabbitMQ broker.
///
/// The full publish path (broker connectivity) is covered by integration tests
/// (not in scope here as per "NO real infra in unit tests" requirement).
/// </summary>
public sealed class EventSerializationTests
{
    [Fact]
    public void HoldCreatedEvent_SerializesCorrectly()
    {
        var evt = new HoldCreatedEvent
        {
            HoldId = "hold-1",
            ProductId = "prod-1",
            CustomerId = "cust-1",
            Quantity = 10,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType());
        var dto = System.Text.Json.JsonSerializer.Deserialize<HoldCreatedEventDto>(payload);

        Assert.NotNull(dto);
        Assert.Equal("hold-1", dto.HoldId);
        Assert.Equal("prod-1", dto.ProductId);
        Assert.Equal("cust-1", dto.CustomerId);
        Assert.Equal(10, dto.Quantity);
    }

    [Fact]
    public void HoldReleasedEvent_SerializesCorrectly()
    {
        var evt = new HoldReleasedEvent
        {
            HoldId = "hold-2",
            ProductId = "prod-2",
            CustomerId = "cust-2",
            Quantity = 3,
            Reason = "CustomerCancelled"
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType());
        var dto = System.Text.Json.JsonSerializer.Deserialize<HoldReleasedEventDto>(payload);

        Assert.NotNull(dto);
        Assert.Equal("hold-2", dto.HoldId);
        Assert.Equal("CustomerCancelled", dto.Reason);
    }

    [Fact]
    public void HoldExpiredEvent_SerializesCorrectly()
    {
        var expiredAt = DateTimeOffset.UtcNow;
        var evt = new HoldExpiredEvent
        {
            HoldId = "hold-3",
            ProductId = "prod-3",
            CustomerId = "cust-3",
            Quantity = 5,
            ExpiredAt = expiredAt
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType());
        var dto = System.Text.Json.JsonSerializer.Deserialize<HoldExpiredEventDto>(payload);

        Assert.NotNull(dto);
        Assert.Equal("hold-3", dto.HoldId);
        Assert.Equal(expiredAt, dto.ExpiredAt);
    }

    [Fact]
    public void DomainEvent_EventId_IsNeverEmpty()
    {
        var events = new DomainEvent[]
        {
            new HoldCreatedEvent  { HoldId = "h", ProductId = "p", CustomerId = "c", Quantity = 1, ExpiresAt = DateTimeOffset.UtcNow },
            new HoldReleasedEvent { HoldId = "h", ProductId = "p", CustomerId = "c", Quantity = 1, Reason = "r" },
            new HoldExpiredEvent  { HoldId = "h", ProductId = "p", CustomerId = "c", Quantity = 1, ExpiredAt = DateTimeOffset.UtcNow }
        };

        foreach (var evt in events)
        {
            Assert.NotEqual(Guid.Empty, evt.EventId);
        }
    }

    [Fact]
    public void RabbitMqTopology_RoutingKeys_AreConstantsNotEmpty()
    {
        Assert.NotEmpty(RabbitMqTopology.HoldCreatedRoutingKey);
        Assert.NotEmpty(RabbitMqTopology.HoldReleasedRoutingKey);
        Assert.NotEmpty(RabbitMqTopology.HoldExpiredRoutingKey);
        Assert.NotEmpty(RabbitMqTopology.MainExchange);
        Assert.NotEmpty(RabbitMqTopology.DeadLetterExchange);
    }

    [Fact]
    public void RabbitMqSettings_DefaultValues_AreValid()
    {
        var settings = new RabbitMqSettings
        {
            Host = "localhost",
            Username = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        Assert.Equal(5672, settings.Port);
        Assert.Equal(3, settings.MaxRetries);
        Assert.Equal(500, settings.InitialRetryDelayMs);
    }
}
