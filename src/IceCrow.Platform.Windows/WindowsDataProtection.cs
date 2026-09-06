using System.Security.Cryptography;

namespace IceCrow.Platform.Windows;

/// <summary>
/// Current-user DPAPI protection for small local secrets. The optional
/// entropy binds the ciphertext to a purpose so a blob copied between two
/// IceCrow stores cannot be swapped.
/// </summary>
public sealed class WindowsDataProtection
{
    public const int MaximumPlaintextBytes = 64 * 1024;

    private readonly byte[] _entropy;

    public WindowsDataProtection(ReadOnlySpan<byte> purposeEntropy)
    {
        _entropy = purposeEntropy.ToArray();
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length > MaximumPlaintextBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintext), "Secret exceeds the protection size limit.");
        }

        return ProtectedData.Protect(plaintext.ToArray(), _entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (protectedData.Length > MaximumPlaintextBytes * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(protectedData), "Protected secret exceeds the size limit.");
        }

        return ProtectedData.Unprotect(protectedData.ToArray(), _entropy, DataProtectionScope.CurrentUser);
    }
}
