using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// הכתובות של ה-client (הדפים ב-HTML). רק מהן מותר לשלוח בקשות עם cookie
var clientOrigins = builder.Configuration.GetSection("ClientOrigins").Get<string[]>() is { Length: > 0 } origins
    ? origins
    : ["http://localhost:5500"];

// מי רשאי להיכנס לאזור האדמין (לפי כתובת אימייל)
var adminEmails = builder.Configuration.GetSection("Admin:Emails").Get<string[]>() ?? [];

// ---------- מסד נתונים: PostgreSQL ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ---------- CORS: מאפשר ל-client לדבר עם השרת ----------
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // בפיתוח: כל פורט מקומי מותר (Live Server, Rider, וכו')
            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host == "localhost" || uri.Host == "127.0.0.1"));
        }
        else
        {
            // בייצור: רק הכתובות שמוגדרות במפורש
            policy.WithOrigins(clientOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }));

// ---------- התחברות: cookie + Google ----------
var auth = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "drive_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // זה API: במקום הפניה לדף התחברות מחזירים 401 / 403
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    })
    // cookie זמני שמחזיק את התשובה מגוגל עד שיוצרים את המשתמש אצלנו
    .AddCookie(AuthEndpoints.ExternalScheme);

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    auth.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = AuthEndpoints.ExternalScheme;
        options.CallbackPath = "/signin-google";
        options.ClaimActions.MapJsonKey(AuthEndpoints.PictureClaim, "picture");
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// יצירת מסד הנתונים והטבלאות בהפעלה הראשונה
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(
            "לא ניתן להתחבר ל-PostgreSQL. בדוק את הסיסמה ב-appsettings.Development.json. שגיאה: {Message}",
            ex.Message);
        throw;
    }
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Server is running");
app.MapAuthEndpoints(clientOrigins);
app.MapAdminEndpoints(adminEmails, clientOrigins[0] + "/index.html");

// תיקיית האחסון של הקבצים, יחסית לתיקיית השרת
var storageRoot = Path.Combine(app.Environment.ContentRootPath,
    builder.Configuration["Storage:Root"] ?? "storage");
app.MapFileEndpoints(storageRoot);

app.Run();
