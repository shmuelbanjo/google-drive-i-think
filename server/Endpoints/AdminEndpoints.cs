using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app, string[] adminEmails, string loginPage)
    {
        bool IsAdmin(ClaimsPrincipal user)
        {
            var email = user.FindFirstValue(ClaimTypes.Email);
            return email is not null &&
                   adminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
        }

        // ---------- דף האדמין עצמו: http://localhost:5000/admin ----------
        app.MapGet("/admin", (ClaimsPrincipal user, IWebHostEnvironment env) =>
        {
            // לא מחובר בכלל - שליחה למסך ההתחברות
            if (user.Identity?.IsAuthenticated != true)
                return Results.Redirect(loginPage);

            // מחובר אבל לא אדמין - חסום
            if (!IsAdmin(user))
                return Results.Content(
                    "<html lang='he'><body style=\"font-family:system-ui;text-align:center;padding:80px;direction:rtl\">" +
                    "<h1>403</h1><p>אין לך הרשאת גישה לאזור הזה.</p></body></html>",
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.File(Path.Combine(env.ContentRootPath, "wwwroot", "admin.html"), "text/html");
        });

        // ---------- ה-API של האדמין ----------
        var group = app.MapGroup("/api/admin")
            .AddEndpointFilter(async (context, next) =>
            {
                var user = context.HttpContext.User;
                if (user.Identity?.IsAuthenticated != true)
                    return Results.Unauthorized();
                if (!IsAdmin(user))
                    return Results.Json(new { message = "אין הרשאת אדמין" }, statusCode: StatusCodes.Status403Forbidden);
                return await next(context);
            });

        // סיכום מספרי לכרטיסיות שבראש הדף
        group.MapGet("/stats", async (AppDbContext db) =>
        {
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            return Results.Ok(new
            {
                totalUsers = await db.Users.CountAsync(),
                verifiedUsers = await db.Users.CountAsync(u => u.EmailVerified),
                googleUsers = await db.Users.CountAsync(u => u.GoogleId != null),
                newThisWeek = await db.Users.CountAsync(u => u.CreatedAt >= weekAgo)
            });
        });

        // רשימת כל המשתמשים
        group.MapGet("/users", async (AppDbContext db) =>
        {
            var users = await db.Users
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    fullName = u.FullName,
                    emailVerified = u.EmailVerified,
                    newsletter = u.Newsletter,
                    provider = u.GoogleId != null ? "Google" : "סיסמה",
                    createdAt = u.CreatedAt,
                    lastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            return Results.Ok(users);
        });

        // מחיקת משתמש
        group.MapDelete("/users/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            // הגנה: אסור למחוק את עצמך ולהישאר בחוץ
            if (principal.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
                return Results.Json(new { message = "אי אפשר למחוק את המשתמש שאיתו אתה מחובר" },
                    statusCode: StatusCodes.Status400BadRequest);

            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound(new { message = "המשתמש לא נמצא" });

            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "המשתמש נמחק" });
        });
    }
}
