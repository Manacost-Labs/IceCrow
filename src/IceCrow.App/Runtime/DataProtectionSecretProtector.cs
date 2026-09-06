using System.Text;
using IceCrow.Platform.Windows;
using IceCrow.ProfileSync;

namespace IceCrow.App.Runtime;

/// <summary>Adapts the Windows DPAPI boundary to the profile-sync protector contract.</summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("IceCrow.ProfileSync.DeviceCredential.v1");
    private readonly WindowsDataProtection _protection = new(Purpose);

    public byte[] Protect(ReadOnlySpan<byte> plaintext) => _protection.Protect(plaintext);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => _protection.Unprotect(protectedData);
}
