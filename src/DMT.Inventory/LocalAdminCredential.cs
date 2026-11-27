using System.Security.Cryptography;

namespace DMT.Inventory;

public sealed record LocalAdminCredential(string Username, string Salt, string Hash, int Iterations = 210_000)
{
    public const int MinimumPasswordLength = 8;
    public static LocalAdminCredential Create(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || password.Length < MinimumPasswordLength || password.Length > 1024) throw new ArgumentException("invalid_admin_credentials");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return new(username.Trim(), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }
    public bool Verify(string username, string password)
    {
        if (password.Length > 1024 || Iterations is < 100_000 or > 1_000_000) return false;
        try
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(Salt), Iterations, HashAlgorithmName.SHA256, 32);
            var valid = CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(Hash));
            return valid && string.Equals(username.Trim(), Username, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException) { return false; }
    }
}
