namespace TeamsGeneratorWebAPI.Capacity;

public sealed class CapacityException : Exception
{
    public CapacityException(string code, string message, int statusCode = 429,
        int? limit = null, int? current = null, int? retryAfterSeconds = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Limit = limit;
        Current = current;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public int? Limit { get; }
    public int? Current { get; }
    public int? RetryAfterSeconds { get; }
}
