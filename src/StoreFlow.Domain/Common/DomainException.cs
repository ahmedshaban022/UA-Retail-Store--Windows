namespace StoreFlow.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated. Application-layer handlers translate this into a
/// failed <c>Result</c>; it never escapes to the UI.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Stable machine-readable code, e.g. <c>stock.insufficient</c>.</summary>
    public string Code { get; }

    public DomainException()
        : this("domain.error", "A domain rule was violated.")
    {
    }

    public DomainException(string message)
        : this("domain.error", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain.error";
    }

    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
