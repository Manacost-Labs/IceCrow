using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Deterministic event ids so that re-observing the same Arena fact (after a
/// restart, a retry, or a repeated snapshot) produces the same
/// <see cref="ProfileEvent.EventId"/> and is deduplicated by the outbox and the
/// server. The id is an RFC 9562 version 8 (custom) UUID whose 128 bits are
/// the first 16 bytes of SHA-256 over the UTF-8 canonical string
/// <c>icecrow.profile-sync|&lt;kind&gt;|&lt;runId&gt;|&lt;key parts&gt;</c>,
/// with the version and variant nibbles overwritten.
/// </summary>
public static class ArenaEventIds
{
    private const string Namespace = "icecrow.profile-sync";

    /// <summary>One id per (run, wins, losses, completion) so every score change is a distinct, idempotent event.</summary>
    public static Guid ForRunRecord(Guid runId, int wins, int losses, bool isComplete) =>
        Derive(string.Create(
            CultureInfo.InvariantCulture,
            $"{Namespace}|arena_run|{runId:D}|{wins}|{losses}|{(isComplete ? 1 : 0)}"));

    /// <summary>One id per (run, pick index).</summary>
    public static Guid ForDraftPick(Guid runId, int pickIndex) =>
        Derive(string.Create(CultureInfo.InvariantCulture, $"{Namespace}|arena_draft_pick|{runId:D}|{pickIndex}"));

    private static Guid Derive(string canonical)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical), digest);
        digest[6] = (byte)((digest[6] & 0x0F) | 0x80);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        return new Guid(digest[..16], bigEndian: true);
    }
}
