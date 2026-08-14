namespace IceCrow.Infrastructure.ManacostApi;

public sealed record ManacostApiOptions
{
    public static Uri ProductionBaseAddress { get; } = new("https://api.kolodahearthstone.com/");

    public Uri BaseAddress { get; init; } = ProductionBaseAddress;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaximumResponseBytes { get; init; } = 8 * 1024 * 1024;

    public int MaximumPages { get; init; } = 100;

    public int PageSize { get; init; } = 200;

    internal void Validate()
    {
        if (BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The Manacost API base address must use HTTPS.", nameof(BaseAddress));
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        }

        if (MaximumResponseBytes is < 1024 or > 64 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumResponseBytes));
        }

        if (MaximumPages is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPages));
        }

        if (PageSize is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize));
        }
    }
}
