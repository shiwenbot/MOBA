using Fantasy.Entitas;

namespace Fantasy;

/// <summary>
/// Demonstration-grade authentication session. The token is repeatable until expiry;
/// Battle reads this record but never issues or revokes credentials.
/// </summary>
public sealed class LoginSession : Entity
{
    public string Token { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public long ExpiresAtMs { get; set; }
    public long IssuedAtMs { get; set; }
}
