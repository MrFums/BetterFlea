using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace BetterFlea.Web;

// The settings page's own admin account. The first person to open the page makes it; after that, changing
// anything needs a login. Forgot the password: delete data/admin.json and restart the server.
internal static class AdminLogin
{
    private const string AccountFile = "admin.json";
    private const string Cookie = "bf_admin";
    private const int Iterations = 100_000;
    private static readonly TimeSpan SessionLength = TimeSpan.FromDays(7);
    private static readonly object Gate = new();
    private static Account? _account;
    private static bool _read;

    // Logins are kept in the file too, so a server restart doesn't log anyone out. Only a hash of each
    // cookie is saved, so someone who copies admin.json still can't log in with it.
    public record Account(string User, string Salt, string Hash)
    {
        public Dictionary<string, DateTime> Sessions { get; set; } = new();
    }

    private static Account? Current
    {
        get
        {
            lock (Gate)
            {
                if (!_read)
                {
                    _account = DataFiles.Read<Account>(AccountFile);
                    _read = true;
                }

                return _account;
            }
        }
    }

    public static bool HasAccount => Current is not null;

    public static string? User => Current?.User;

    public static bool IsAdmin(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Cookie, out var token) || token is null) return false;
        lock (Gate)
        {
            return Current is { } account && account.Sessions.TryGetValue(Key(token), out var expires) && expires > DateTime.UtcNow;
        }
    }

    // null = done, otherwise why not.
    public static string? Create(HttpContext context, string user, string password)
    {
        user = user.Trim();
        if (user.Length is < 1 or > 32) return "Pick a username (up to 32 characters).";
        if (password.Length < 6) return "The password needs at least 6 characters.";
        lock (Gate)
        {
            if (Current is not null) return "There's already an admin account.";
            var salt = RandomNumberGenerator.GetBytes(16);
            _account = new Account(user, Convert.ToBase64String(salt), Convert.ToBase64String(Hash(password, salt)));
        }

        StartSession(context);
        return null;
    }

    public static async Task<bool> LogIn(HttpContext context, string user, string password)
    {
        var account = Current;
        var ok = account is not null && string.Equals(account.User, user.Trim(), StringComparison.OrdinalIgnoreCase)
            && CryptographicOperations.FixedTimeEquals(Hash(password, Convert.FromBase64String(account.Salt)), Convert.FromBase64String(account.Hash));
        if (!ok)
        {
            await Task.Delay(800);
            return false;
        }

        StartSession(context);
        return true;
    }

    public static void LogOut(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(Cookie, out var token) && token is not null)
        {
            lock (Gate)
            {
                if (Current is { } account && account.Sessions.Remove(Key(token)))
                {
                    DataFiles.Write(AccountFile, account);
                }
            }
        }

        context.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/betterflea" });
    }

    private static void StartSession(HttpContext context)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (Gate)
        {
            var account = Current!;
            foreach (var old in account.Sessions.Where(entry => entry.Value <= DateTime.UtcNow).ToList())
            {
                account.Sessions.Remove(old.Key);
            }

            account.Sessions[Key(token)] = DateTime.UtcNow + SessionLength;
            DataFiles.Write(AccountFile, account);
        }

        context.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            Path = "/betterflea", HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, MaxAge = SessionLength
        });
    }

    private static string Key(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}
