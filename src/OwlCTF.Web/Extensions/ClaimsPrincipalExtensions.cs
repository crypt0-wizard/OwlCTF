using System.Security.Claims;

namespace OwlCTF.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("owlctf:user_id"), out var id) ? id : throw new UnauthorizedAccessException();
}
