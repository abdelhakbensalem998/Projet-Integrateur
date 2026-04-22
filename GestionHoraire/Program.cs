using System;
using GestionHoraire.Data;
using GestionHoraire.Services;
using Microsoft.EntityFrameworkCore;

// PostgreSQL : permet d'utiliser DateTime.Now (local) au lieu de forcer DateTime.UtcNow
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = ".GestionHoraire.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ✅ DI Services
builder.Services.AddScoped<PlanningService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<TwoFactorService>();

var app = builder.Build();

// Connexion à la base de données existante
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();
}

// Pipeline HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// ✅ Session doit être après UseRouting et avant MapControllerRoute
app.UseSession();

app.Use(async (context, next) =>
{
    if (RequiresAuthenticatedSession(context.Request.Path) &&
        context.Session.GetInt32("UserId") == null)
    {
        if (AcceptsHtml(context))
            context.Response.Redirect("/Login/Index?expired=1");
        else
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static bool RequiresAuthenticatedSession(PathString path)
{
    var value = path.Value ?? "/";

    if (value == "/")
        return false;

    return !value.StartsWith("/Home", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("/Login", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("/css", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("/js", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);
}

static bool AcceptsHtml(HttpContext context)
{
    var accept = context.Request.Headers.Accept.ToString();
    return string.IsNullOrWhiteSpace(accept) ||
        accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
        accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);
}
