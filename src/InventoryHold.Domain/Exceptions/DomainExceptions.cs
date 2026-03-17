namespace InventoryHold.Domain.Exceptions;

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class HoldNotFoundException : Exception
{
    public HoldNotFoundException(string holdId)
        : base($"Hold '{holdId}' was not found.") { }
}

public sealed class HoldAlreadyReleasedException : Exception
{
    public HoldAlreadyReleasedException(string holdId)
        : base($"Hold '{holdId}' has already been released.") { }
}

public sealed class HoldAlreadyExpiredException : Exception
{
    public HoldAlreadyExpiredException(string holdId)
        : base($"Hold '{holdId}' has already expired.") { }
}

public sealed class InsufficientInventoryException : Exception
{
    public InsufficientInventoryException(string productId, int requested, int available)
        : base($"Insufficient inventory for product '{productId}'. Requested: {requested}, Available: {available}.") { }
}
