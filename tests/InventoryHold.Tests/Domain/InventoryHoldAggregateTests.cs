using InventoryHold.Domain.Aggregates;
using InventoryHold.Domain.Events;
using InventoryHold.Domain.Exceptions;
using Xunit;

namespace InventoryHold.Tests.Domain;

/// <summary>
/// Tests for the InventoryHold aggregate's state machine and domain event emission.
/// No infrastructure dependencies — pure domain logic.
/// </summary>
public sealed class InventoryHoldAggregateTests
{
    [Fact]
    public void Create_ValidInputs_EmitsHoldCreatedEvent()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 5, TimeSpan.FromMinutes(15));

        var events = hold.DrainDomainEvents();

        Assert.Single(events);
        var e = Assert.IsType<HoldCreatedEvent>(events[0]);
        Assert.Equal("h1", e.HoldId);
        Assert.Equal("prod-1", e.ProductId);
        Assert.Equal("cust-1", e.CustomerId);
        Assert.Equal(5, e.Quantity);
        Assert.NotEqual(Guid.Empty, e.EventId);
    }

    [Fact]
    public void Create_ZeroQuantity_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Hold.Create("h1", "prod-1", "cust-1", 0, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Create_NegativeDuration_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Hold.Create("h1", "prod-1", "cust-1", 1, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Release_ActiveHold_EmitsHoldReleasedEvent()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 3, TimeSpan.FromMinutes(15));
        hold.DrainDomainEvents(); // clear creation event

        hold.Release("CustomerCancelled");

        var events = hold.DrainDomainEvents();
        Assert.Single(events);
        var e = Assert.IsType<HoldReleasedEvent>(events[0]);
        Assert.Equal("h1", e.HoldId);
        Assert.Equal("CustomerCancelled", e.Reason);
        Assert.NotEqual(Guid.Empty, e.EventId);
    }

    [Fact]
    public void Release_AlreadyReleased_ThrowsHoldAlreadyReleasedException()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 3, TimeSpan.FromMinutes(15));
        hold.Release("reason");
        hold.DrainDomainEvents();

        Assert.Throws<HoldAlreadyReleasedException>(() => hold.Release("reason-again"));
    }

    [Fact]
    public void Expire_ActiveHold_EmitsHoldExpiredEvent()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromMinutes(15));
        hold.DrainDomainEvents();

        hold.Expire();

        var events = hold.DrainDomainEvents();
        Assert.Single(events);
        var e = Assert.IsType<HoldExpiredEvent>(events[0]);
        Assert.Equal("h1", e.HoldId);
        Assert.NotEqual(Guid.Empty, e.EventId);
    }

    [Fact]
    public void Expire_AlreadyExpired_ThrowsHoldAlreadyExpiredException()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromMinutes(15));
        hold.Expire();
        hold.DrainDomainEvents();

        Assert.Throws<HoldAlreadyExpiredException>(() => hold.Expire());
    }

    [Fact]
    public void Expire_AlreadyReleased_ThrowsHoldAlreadyReleasedException()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromMinutes(15));
        hold.Release("reason");
        hold.DrainDomainEvents();

        Assert.Throws<HoldAlreadyReleasedException>(() => hold.Expire());
    }

    [Fact]
    public void IsExpired_PastExpiresAt_ReturnsTrue()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromSeconds(1));
        var futureTime = DateTimeOffset.UtcNow.AddSeconds(5);

        Assert.True(hold.IsExpired(futureTime));
    }

    [Fact]
    public void IsExpired_BeforeExpiresAt_ReturnsFalse()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromHours(1));

        Assert.False(hold.IsExpired());
    }

    [Fact]
    public void DrainDomainEvents_ClearsEventsAfterDrain()
    {
        var hold = Hold.Create("h1", "prod-1", "cust-1", 2, TimeSpan.FromMinutes(15));

        var firstDrain = hold.DrainDomainEvents();
        var secondDrain = hold.DrainDomainEvents();

        Assert.Single(firstDrain);
        Assert.Empty(secondDrain);
    }

    [Fact]
    public void Create_EventId_IsUniquePerEvent()
    {
        var hold1 = Hold.Create("h1", "p", "c", 1, TimeSpan.FromMinutes(1));
        var hold2 = Hold.Create("h2", "p", "c", 1, TimeSpan.FromMinutes(1));

        var id1 = hold1.DrainDomainEvents()[0].EventId;
        var id2 = hold2.DrainDomainEvents()[0].EventId;

        Assert.NotEqual(id1, id2);
    }
}
