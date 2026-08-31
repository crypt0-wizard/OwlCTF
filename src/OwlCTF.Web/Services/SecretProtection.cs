using Microsoft.AspNetCore.DataProtection;

namespace OwlCTF.Services;

public sealed class JoinCodeProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("OwlCTF.TeamJoinCodes.v1");
    public string Protect(string code) => _protector.Protect(code);
    public string? Unprotect(string? protectedCode)
    {
        if (string.IsNullOrWhiteSpace(protectedCode)) return null;
        try { return _protector.Unprotect(protectedCode); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}

public sealed class FirstBloodWebhookProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("OwlCTF.FirstBloodWebhook.v1");
    public string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : protector.Protect(value);
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try { return protector.Unprotect(protectedValue); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
