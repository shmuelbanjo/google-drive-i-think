using Microsoft.EntityFrameworkCore;

namespace Server.Data;

// טבלת המשתמשים במסד
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";

    // null כשהמשתמש נרשם רק דרך Google
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }

    public string? PhotoUrl { get; set; }
    public bool EmailVerified { get; set; }
    public bool Newsletter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

// טבלת הקבצים. הקובץ עצמו יושב על הדיסק, וכאן נשמר המידע עליו
public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // הבעלים. זה גם שם התיקייה שבה הקובץ נשמר בדיסק
    public Guid OwnerId { get; set; }

    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }

    // הנתיב היחסי בתוך תיקיית האחסון, בצורה "{OwnerId}/{Id}.סיומת"
    public string StorageKey { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasIndex(u => u.Email).IsUnique();
            user.HasIndex(u => u.GoogleId).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.FullName).HasMaxLength(200);
        });

        modelBuilder.Entity<StoredFile>(file =>
        {
            file.ToTable("files");
            file.HasIndex(f => f.OwnerId);
            file.Property(f => f.Name).HasMaxLength(400);
            file.Property(f => f.ContentType).HasMaxLength(200);
            file.Property(f => f.StorageKey).HasMaxLength(500);

            // מחיקת משתמש מוחקת גם את רשומות הקבצים שלו
            file.HasOne<User>()
                .WithMany()
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
