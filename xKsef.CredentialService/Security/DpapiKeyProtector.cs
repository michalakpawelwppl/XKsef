using System.Security.Cryptography;

namespace xKsef.CredentialService.Security;

public sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] key)
    {
        return ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedKey)
    {
        return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
    }
}