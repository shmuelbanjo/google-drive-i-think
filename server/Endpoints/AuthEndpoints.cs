using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Endpoints;

public record RegisterRequest(string FullName, string Email, string Password, bool Newsletter);
public record LoginRequest(string Email, string Password, bool RememberMe);
public record ForgotPasswordRequest(string Email);

public static class AuthEndpoints
{
    public const string ExternalScheme = "External";
    public const string PictureClaim = "urn:google:picture";

    private static readonly PasswordHasher<User> Hasher = new();

    public static void MapAuthEndpoints(this WebApplication app, string[] clientOrigins)
    {
        var clientOrigin = clientOrigins[0];
        var group = app.MapGroup("/api/auth");

        // ---------- הרשמה ----------
        group.MapPost("/register", async (RegisterRequest req, AppDbContext db, HttpContext http) =>
        {
            var email = NormalizeEmail(req.Email);
            var fullName = (req.FullName ?? "").Trim();

            if (fullName.Length == 0)
                return Error("יש להזין שם מלא");
            if (!IsValidEmail(email))
                return Error("כתובת אימייל לא תקינה");
            if ((req.Password ?? "").Length < 8)
                return Error("הסיסמה חייבת להכיל לפחות 8 תווים");
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Error("כתובת האימייל כבר רשומה במערכת", StatusCodes.Status409Conflict);

            var user = new User { Email = email, FullName = fullName, Newsletter = req.Newsletter, LastLoginAt = DateTime.UtcNow };
            user.PasswordHash = Hasher.HashPassword(user, req.Password!);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            await SignInAsync(http, user, persistent: false);
            return Results.Ok(ToDto(user));
        });

        // ---------- התחברות ----------
        group.MapPost("/login", async (LoginRequest req, AppDbContext db, HttpContext http) =>
        {
            var email = NormalizeEmail(req.Email);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            // אותה הודעה בכל מקרה, כדי לא לחשוף אילו אימיילים רשומים
            if (user?.PasswordHash is null ||
                Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
                return Error("אימייל או סיסמה שגויים", StatusCodes.Status401Unauthorized);

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await SignInAsync(http, user, req.RememberMe);
            return Results.Ok(ToDto(user));
        });

        // ---------- התנתקות ----------
        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "התנתקת בהצלחה" });
        });

        // ---------- מי מחובר ----------
        group.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, HttpContext http) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                return Results.Unauthorized();

            var user = await db.Users.FindAsync(id);
            if (user is null)
            {
                // המשתמש נמחק מהמסד אבל ה-cookie עוד קיים
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Unauthorized();
            }
            return Results.Ok(ToDto(user));
        }).RequireAuthorization();

        // ---------- שכחתי סיסמה ----------
        group.MapPost("/forgot-password", (ForgotPasswordRequest req, ILogger<User> logger) =>
        {
            // TODO: שליחת מייל אמיתי דורשת שירות מיילים (SMTP / SendGrid וכו')
            logger.LogWarning("Password reset requested for {Email}, but email sending is not configured yet", NormalizeEmail(req.Email));
            return Results.Ok(new { message = "אם האימייל רשום, יישלח אליו קישור לאיפוס" });
        });

        // ---------- התחברות עם Google ----------
        group.MapGet("/google", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync("Google") is null)
                return Error("התחברות עם Google עדיין לא הוגדרה בשרת", StatusCodes.Status501NotImplemented);

            var safeReturn = SafeReturnUrl(returnUrl, clientOrigins);
            var props = new AuthenticationProperties
            {
                RedirectUri = "/api/auth/google/callback?returnUrl=" + Uri.EscapeDataString(safeReturn)
            };
            return Results.Challenge(props, ["Google"]);
        });

        group.MapGet("/google/callback", async (string? returnUrl, AppDbContext db, HttpContext http) =>
        {
            var safeReturn = SafeReturnUrl(returnUrl, clientOrigins);
            var external = await http.AuthenticateAsync(ExternalScheme);
            await http.SignOutAsync(ExternalScheme);

            var googleId = external.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = NormalizeEmail(external.Principal?.FindFirstValue(ClaimTypes.Email));
            if (!external.Succeeded || googleId is null || !IsValidEmail(email))
                return Results.Redirect(clientOrigin + "/index.html?error=google");

            // משתמש קיים לפי Google או לפי אימייל, אחרת משתמש חדש
            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId)
                    ?? await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user is null)
            {
                user = new User { Email = email };
                db.Users.Add(user);
            }

            user.GoogleId = googleId;
            user.EmailVerified = true; // גוגל כבר אימתה את האימייל
            if (string.IsNullOrWhiteSpace(user.FullName))
                user.FullName = external.Principal!.FindFirstValue(ClaimTypes.Name) ?? "";
            user.PhotoUrl = external.Principal!.FindFirstValue(PictureClaim) ?? user.PhotoUrl;
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await SignInAsync(http, user, persistent: true);
            return Results.Redirect(safeReturn);
        });
    }

    private static Task SignInAsync(HttpContext http, User user, bool persistent)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = persistent });
    }

    private static object ToDto(User u) => new
    {
        id = u.Id,
        email = u.Email,
        fullName = u.FullName,
        photoUrl = u.PhotoUrl,
        emailVerified = u.EmailVerified
    };

    private static IResult Error(string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new { message }, statusCode: status);

    private static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 320 && System.Net.Mail.MailAddress.TryCreate(email, out _);

    // מונע הפניה לאתרים זרים: רק כתובות של ה-client מותרות
    private static string SafeReturnUrl(string? returnUrl, string[] clientOrigins) =>
        returnUrl is not null && clientOrigins.Any(o => returnUrl.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase))
            ? returnUrl
            : clientOrigins[0] + "/dashboard.html";
}
