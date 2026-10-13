using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Endpoints;

public static class FileEndpoints
{
    // גודל מרבי לקובץ בודד
    private const long MaxFileSize = 100L * 1024 * 1024; // 100MB

    public static void MapFileEndpoints(this WebApplication app, string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);

        var group = app.MapGroup("/api/files").RequireAuthorization();

        // ================================================================
        //  שכבת ההגנה: שם התיקייה של הקובץ חייב להיות זהה ל-UserId המחובר
        // ================================================================
        static bool FolderBelongsToUser(string storageKey, Guid userId)
        {
            // StorageKey בנוי כך: "{OwnerId}/{FileId}.סיומת"
            var folderName = storageKey.Split('/').FirstOrDefault();
            return folderName is not null &&
                   Guid.TryParse(folderName, out var folderOwner) &&
                   folderOwner == userId;
        }

        // מזהה המשתמש נלקח תמיד מהעוגייה, לעולם לא ממה שהדפדפן שולח
        static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        // ---------- העלאת קובץ ----------
        group.MapPost("/", async (IFormFile? file, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();

            if (file is null || file.Length == 0)
                return Results.Json(new { message = "לא נבחר קובץ" }, statusCode: StatusCodes.Status400BadRequest);

            if (file.Length > MaxFileSize)
                return Results.Json(new { message = "הקובץ גדול מדי (מקסימום 100MB)" },
                    statusCode: StatusCodes.Status413PayloadTooLarge);

            // תיקייה אישית למשתמש, ששמה הוא ה-UserId שלו
            var userFolder = Path.Combine(storageRoot, userId.Value.ToString());
            Directory.CreateDirectory(userFolder);

            // שם הקובץ בדיסק אקראי, כדי שלא יתנגש ולא יכיל תווים מסוכנים
            var fileId = Guid.NewGuid();
            var extension = Path.GetExtension(file.FileName);
            if (extension.Length > 20 || extension.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
                extension = "";

            var storageKey = $"{userId}/{fileId}{extension}";
            var fullPath = Path.Combine(storageRoot, userId.Value.ToString(), $"{fileId}{extension}");

            await using (var stream = File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            var record = new StoredFile
            {
                Id = fileId,
                OwnerId = userId.Value,
                Name = Path.GetFileName(file.FileName), // השם המקורי, להצגה בלבד
                ContentType = file.ContentType ?? "application/octet-stream",
                Size = file.Length,
                StorageKey = storageKey
            };

            db.Files.Add(record);
            await db.SaveChangesAsync();

            return Results.Ok(new { id = record.Id, name = record.Name, size = record.Size, createdAt = record.CreatedAt });
        }).DisableAntiforgery();

        // ---------- רשימת הקבצים של המשתמש ----------
        group.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var files = await db.Files
                .Where(f => f.OwnerId == userId.Value)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new { id = f.Id, name = f.Name, size = f.Size, contentType = f.ContentType, createdAt = f.CreatedAt })
                .ToListAsync();

            return Results.Ok(files);
        });

        // ---------- הורדת קובץ ----------
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var record = await db.Files.FindAsync(id);
            if (record is null)
                return Results.NotFound(new { message = "הקובץ לא נמצא" });

            // בדיקה ראשונה: הבעלים לפי המסד
            if (record.OwnerId != userId.Value)
                return Results.Json(new { message = "אין לך גישה לקובץ הזה" }, statusCode: StatusCodes.Status403Forbidden);

            // בדיקה שנייה: שם התיקייה בדיסק חייב להתאים ל-UserId
            if (!FolderBelongsToUser(record.StorageKey, userId.Value))
                return Results.Json(new { message = "אין לך גישה לקובץ הזה" }, statusCode: StatusCodes.Status403Forbidden);

            var fullPath = Path.Combine(storageRoot, record.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                return Results.NotFound(new { message = "הקובץ חסר מהאחסון" });

            // attachment: הדפדפן מוריד את הקובץ במקום להריץ אותו
            return Results.File(fullPath, record.ContentType, record.Name);
        });

        // ---------- מחיקת קובץ ----------
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var record = await db.Files.FindAsync(id);
            if (record is null)
                return Results.NotFound(new { message = "הקובץ לא נמצא" });

            if (record.OwnerId != userId.Value || !FolderBelongsToUser(record.StorageKey, userId.Value))
                return Results.Json(new { message = "אין לך גישה לקובץ הזה" }, statusCode: StatusCodes.Status403Forbidden);

            var fullPath = Path.Combine(storageRoot, record.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) File.Delete(fullPath);

            db.Files.Remove(record);
            await db.SaveChangesAsync();

            return Results.Ok(new { message = "הקובץ נמחק" });
        });
    }
}
