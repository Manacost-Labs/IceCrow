namespace IceCrow.ProfileSync.Transport;

/// <summary>
/// Reads an HTTP response body with a hard byte cap regardless of
/// <c>Content-Length</c> (chunked responses carry none), so a hostile or
/// broken origin cannot make the client buffer an unbounded body.
/// </summary>
internal static class BoundedResponse
{
    public const int MaximumBytes = 256 * 1024;

    /// <summary>Returns the body, or null when it exceeds <see cref="MaximumBytes"/>.</summary>
    public static async Task<byte[]?> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength is > MaximumBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaximumBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > MaximumBytes ? null : buffer[..total];
    }
}
