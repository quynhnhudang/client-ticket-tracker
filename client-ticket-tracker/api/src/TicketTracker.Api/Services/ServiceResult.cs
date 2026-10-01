namespace TicketTracker.Api.Services;

public enum ServiceError
{
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>
/// The outcome of a service call that can fail for an expected, business reason.
///
/// Expected failures ("ticket not found", "can't resolve without notes", "someone
/// else changed it") are returned as values, not thrown. The controller then
/// maps them to 404 / 400 / 409. Exceptions are kept for the unexpected (the
/// database is down), which become a 500 via the global exception handler.
/// This keeps HTTP concerns out of the service and makes the failure paths
/// visible in the method signature.
/// </summary>
public sealed class ServiceResult<T>
{
    public T? Value { get; }
    public ServiceError? Error { get; }
    public string? Message { get; }

    private ServiceResult(T? value, ServiceError? error, string? message)
    {
        Value = value;
        Error = error;
        Message = message;
    }

    public static ServiceResult<T> Ok(T value) => new(value, null, null);

    public static ServiceResult<T> Fail(ServiceError error, string message) => new(default, error, message);
}
