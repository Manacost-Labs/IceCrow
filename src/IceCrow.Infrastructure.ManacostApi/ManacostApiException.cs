namespace IceCrow.Infrastructure.ManacostApi;

public sealed class ManacostApiException : Exception
{
    public ManacostApiException(string message)
        : base(message)
    {
    }

    public ManacostApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
