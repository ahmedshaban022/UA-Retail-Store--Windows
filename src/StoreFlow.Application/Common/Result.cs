namespace StoreFlow.Application.Common;

/// <summary>
/// A failure, expressed as a stable machine-readable code plus a message that is safe to show a
/// user. Codes let the UI react (translate, route to a specific message) without parsing text.
/// </summary>
/// <param name="Code">A stable code such as <c>auth.invalid_credentials</c>.</param>
/// <param name="Message">A human-readable, actionable message.</param>
public sealed record Error(string Code, string Message)
{
    /// <summary>The absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Code);

    public static Error Create(string code, string message) => new(code, message);
}

/// <summary>The outcome of an operation that produces no value.</summary>
public sealed class Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result Failure(string code, string message) => new(false, new Error(code, message));
}

/// <summary>The outcome of an operation that produces a value on success.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, Error error, T? value)
    {
        IsSuccess = isSuccess;
        Error = error;
        _value = value;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    /// <summary>The produced value. Throws when the result is a failure.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result is a failure ({Error.Code}); it has no value.");

    public static Result<T> Success(T value) => new(true, Error.None, value);

    public static Result<T> Failure(Error error) => new(false, error, default);

    public static Result<T> Failure(string code, string message) => new(false, new Error(code, message), default);

    /// <summary>Projects the value of a successful result, leaving failures untouched.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return IsSuccess ? Result<TOut>.Success(projection(_value!)) : Result<TOut>.Failure(Error);
    }
}
