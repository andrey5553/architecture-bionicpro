using Amazon.S3;
using KeycloakAuthService.Middleware;
using KeycloakAuthService.Services;
using KeycloakAuthService.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

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

builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("Minio"));
builder.Services.Configure<CdnOptions>(builder.Configuration.GetSection("CDN"));

// Регистрация S3 клиента
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = options.Endpoint,
        ForcePathStyle = true, // Важно для Minio!
        UseHttp = !options.UseSSL
    };

    return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
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