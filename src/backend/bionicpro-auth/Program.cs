using KeycloakAuthService.Middleware;
using KeycloakAuthService.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

//// Data Protection с постоянным хранилищем
//builder.Services.AddDataProtection()
//    //.PersistKeysToFileSystem(new DirectoryInfo("/root/.aspnet/DataProtection-Keys"))
//    .SetApplicationName("KeycloakAuthService");
//    //.SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Data Protection с постоянным хранилищем
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/root/.aspnet/DataProtection-Keys"))
    .SetApplicationName("KeycloakAuthService")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add HTTP client
builder.Services.AddHttpClient<IKeycloakService, KeycloakService>();

// Add session with memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(
        builder.Configuration.GetValue<int>("Session:TimeoutMinutes", 60));
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = builder.Configuration["Session:CookieName"] ?? "KeycloakSession";
    options.Cookie.SameSite = SameSiteMode.Lax;  // 
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;  
});

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000") // React app URL
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Middleware pipeline
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseSession();
app.UseMiddleware<TokenRefreshMiddleware>();
app.UseMiddleware<SessionMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();