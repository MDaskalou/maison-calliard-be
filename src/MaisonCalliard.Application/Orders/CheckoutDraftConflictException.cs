namespace MaisonCalliard.Application.Orders;

public static class CheckoutUpdateMessages
{
    public const string NotFound = "Order was not found.";
    public const string NotUpdatable = "Only an unpaid card checkout can be updated.";
    public const string NotReusable = "This checkout draft can no longer be reused.";
}

public sealed class CheckoutDraftConflictException : Exception
{
    public CheckoutDraftConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
