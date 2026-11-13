namespace xKsef.CredentialService.Security;

public interface IKeyProtector
{
    byte[] Protect(byte[] key);
    byte[] Unprotect(byte[] protectedKey);
}