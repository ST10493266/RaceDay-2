namespace RaceDay.Api.Services;

/// <summary>Hashes and verifies passwords with BCrypt (salted, slow by design). Plain passwords are never stored.</summary>
public static class PasswordService
{
    private const int WorkFactor = 11;

    // Used to keep login timing similar whether or not the email exists.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password", WorkFactor);

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string? hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash ?? DummyHash) && hash is not null;
        }
        catch (Exception)
        {
            // Malformed hash in the database: treat as a failed login rather than a 500.
            return false;
        }
    }
}
