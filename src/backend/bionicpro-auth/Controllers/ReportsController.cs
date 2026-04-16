using Amazon.S3;
using Amazon.S3.Model;
using ClickHouse.Client.ADO.Parameters;
using KeycloakAuthService.Models;
using KeycloakAuthService.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace KeycloakAuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportsController> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly MinioOptions _minioOptions;
    private readonly CdnOptions _cdnOptions;

    public ReportsController(
        IConfiguration configuration,
        ILogger<ReportsController> logger,
        IAmazonS3 s3Client,
        IOptions<MinioOptions> minioOptions,
        IOptions<CdnOptions> cdnOptions)
    {
        _configuration = configuration;
        _logger = logger;
        _s3Client = s3Client;
        _minioOptions = minioOptions.Value;
        _cdnOptions = cdnOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetReport([FromQuery] bool forceRefresh = false)
    {
        _logger.LogInformation("=== GetReport START === ForceRefresh: {ForceRefresh}", forceRefresh);

        _logger.LogInformation("Minio configured: Endpoint={Endpoint}, Bucket={Bucket}",
            _minioOptions.Endpoint, _minioOptions.BucketName);
        _logger.LogInformation("S3 client is null? {IsNull}", _s3Client == null);

        try
        {
            // 1. Аутентификация и получение данных пользователя
            var (authResult, username, accessToken) = await AuthenticateAndGetUser();
            if (authResult != null) return authResult;

            // 2. Проверка роли prothetic_user
            var roleCheckResult = await CheckUserRole(accessToken, username);
            if (roleCheckResult != null) return roleCheckResult;

            // 3. Генерация ключа отчета в S3
            var reportKey = GenerateReportKey(username);
            _logger.LogInformation("Report S3 Key: {ReportKey}", reportKey);

            // 4. Проверка наличия отчета в S3 (если не force refresh)
            if (!forceRefresh)
            {
                var s3CheckResult = await CheckAndReturnFromS3(reportKey);
                if (s3CheckResult != null) return s3CheckResult;
            }
            else
            {
                _logger.LogInformation("Force refresh enabled, skipping S3 cache check");

                // При force refresh - инвалидируем кеш
                await InvalidateCache(reportKey);
            }

            // 5. Генерация нового отчета из ClickHouse
            _logger.LogInformation("Generating new report from ClickHouse for user: {Username}", username);
            var reportData = await GenerateReportFromClickHouse(username);

            // 6. Сохранение отчета в S3
            await SaveReportToS3(reportKey, reportData, username);

            // 7. Получение CDN ссылки
            var cdnUrl = GetCdnUrl(reportKey);

            // 8. Возврат результата с CDN ссылкой
            return Ok(new
            {
                success = true,
                user = username,
                report = reportData,
                cdn_url = cdnUrl,
                cached = false,
                generated_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddHours(1)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetReport");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }
    

    #region Private Methods

    private async Task<(IActionResult? authResult, string? username, string? accessToken)> AuthenticateAndGetUser()
    {
        var tokenResponseJson = HttpContext.Session.GetString("TokenResponse");
        var username = HttpContext.Session.GetString("Username");

        _logger.LogInformation("TokenResponse from Session: {HasToken}", !string.IsNullOrEmpty(tokenResponseJson));
        _logger.LogInformation("Username from Session: {Username}", username);

        if (string.IsNullOrEmpty(tokenResponseJson))
        {
            _logger.LogWarning("No TokenResponse in session");
            return (Unauthorized(new { error = "Not authenticated - no session" }), null, null);
        }

        TokenResponse? tokenResponse = null;
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            tokenResponse = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson, options);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return (Unauthorized(new { error = "Invalid session data - no access token" }), null, null);
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON deserialization failed");
            return (StatusCode(500, new { error = "Session data corrupted", details = jsonEx.Message }), null, null);
        }

        return (null, username, tokenResponse.AccessToken);
    }

    private async Task<IActionResult?> CheckUserRole(string accessToken, string? username)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

        if (!handler.CanReadToken(accessToken))
        {
            _logger.LogWarning("Cannot read token - invalid format");
            return StatusCode(500, new { error = "Invalid token format" });
        }

        var jwtToken = handler.ReadJwtToken(accessToken);

        // Проверка срока действия токена
        var validTo = jwtToken.ValidTo;
        var now = DateTime.UtcNow;

        if (validTo < now)
        {
            _logger.LogWarning("Token has expired!");
            return Unauthorized(new { error = "Token expired", needsRefresh = true });
        }

        // Поиск ролей
        var roles = new List<string>();

        var realmAccessClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "realm_access");
        if (realmAccessClaim != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccessClaim.Value);
                if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                {
                    roles.AddRange(rolesElement.EnumerateArray().Select(r => r.GetString()));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing realm_access");
            }
        }

        var resourceAccessClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "resource_access");
        if (resourceAccessClaim != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(resourceAccessClaim.Value);
                foreach (var client in doc.RootElement.EnumerateObject())
                {
                    if (client.Value.TryGetProperty("roles", out var clientRolesElement))
                    {
                        roles.AddRange(clientRolesElement.EnumerateArray().Select(r => r.GetString()));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing resource_access");
            }
        }

        roles = roles.Distinct().ToList();
        var hasProtheticRole = roles.Any(r => r.Contains("prothetic_user") || r.Contains("prothetic"));

        if (!hasProtheticRole)
        {
            _logger.LogWarning("Access denied - missing prothetic_user role");
            return StatusCode(403, new
            {
                error = "Access denied. Prothetic user role required.",
                availableRoles = roles,
                user = username
            });
        }

        return null;
    }

    private async Task<IActionResult?> CheckAndReturnFromS3(string reportKey)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _minioOptions.BucketName,
                Key = reportKey
            };

            var metadata = await _s3Client.GetObjectMetadataAsync(request);

            _logger.LogInformation("Report found in S3, last modified: {LastModified}", metadata.LastModified);

            var cdnUrl = GetCdnUrl(reportKey);

            // Добавляем заголовки кеширования
            Response.Headers.Append("X-Cache-Status", "HIT");
            Response.Headers.Append("X-S3-Last-Modified", metadata.LastModified?.ToString("R"));

            return Ok(new
            {
                success = true,
                cached = true,
                cdn_url = cdnUrl,
                report_metadata = new
                {
                    last_modified = metadata.LastModified,
                    size_bytes = metadata.ContentLength,
                    etag = metadata.ETag
                }
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Report not found in S3, will generate new one");
            return null; // Отчет не найден, нужно генерировать
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking S3 for report {ReportKey}", reportKey);
            return null; // При ошибке S3 - генерируем новый отчет
        }
    }

    private async Task SaveReportToS3(string reportKey, object reportData, string username)
    {
        try
        {
            var reportJson = JsonSerializer.Serialize(reportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var putRequest = new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = _minioOptions.BucketName,
                Key = reportKey,
                ContentBody = reportJson,
                ContentType = "application/json",
            };

            // Добавление метаданных через свойство Metadata
            putRequest.Metadata.Add("x-amz-meta-user", username);
            putRequest.Metadata.Add("x-amz-meta-generated-at", DateTime.UtcNow.ToString("O"));
            putRequest.Metadata.Add("x-amz-meta-version", "1.0");

            var response = await _s3Client.PutObjectAsync(putRequest);
            _logger.LogInformation("Report saved to S3: {Key}, ETag: {ETag}", reportKey, response.ETag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save report to S3 for user {Username}", username);
            throw;
        }
    }

    private async Task InvalidateCache(string reportKey)
    {
        try
        {
            // Удаляем из S3
            var deleteRequest = new Amazon.S3.Model.DeleteObjectRequest
            {
                BucketName = _minioOptions.BucketName,
                Key = reportKey
            };

            await _s3Client.DeleteObjectAsync(deleteRequest);
            _logger.LogInformation("Cache invalidated for report: {ReportKey}", reportKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache for {ReportKey}", reportKey);
            throw;
        }
    }

    private string GenerateReportKey(string username)
    {
        // Структура: reports/{username}/{date}.json
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return $"{_minioOptions.ReportPrefix}/{username}/{date}.json";
    }

    private string GetCdnUrl(string reportKey)
    {
        // Убираем префикс "reports/" для CDN URL
        var cdnPath = reportKey.Replace($"{_minioOptions.ReportPrefix}/", "");
        return $"{_cdnOptions.BaseUrl}/{cdnPath}";
    }

    private async Task<object> GenerateReportFromClickHouse(string username)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("ClickHouse");

            using var connection = new ClickHouse.Client.ADO.ClickHouseConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
        SELECT 
            report_date,
            avg_battery_level,
            total_steps,
            max_muscle_voltage,
            errors_count
        FROM report_user_daily_mart
        WHERE keycloak_username = {username:String} 
        AND report_date >= today() - 7
        ORDER BY report_date DESC 
        LIMIT 30";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new ClickHouseDbParameter
            {
                ParameterName = "username",
                Value = username
            });

            // Создаём строго типизированный список вместо List<object>
            var results = new List<DailyReportData>();

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new DailyReportData
                {
                    ReportDate = reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                    AvgBatteryLevel = reader.GetFloat(1),
                    TotalSteps = Convert.ToInt32(reader.GetValue(2)),
                    MaxMuscleVoltage = reader.GetFloat(3),
                    ErrorsCount = Convert.ToInt32(reader.GetValue(4))
                });
            }

            _logger.LogInformation("Found {Count} report records for user {Username}", results.Count, username);

            // Проверяем, есть ли данные
            if (!results.Any())
            {
                return new
                {
                    daily_stats = new List<object>(),
                    total_days = 0,
                    avg_daily_steps = 0,
                    summary = new
                    {
                        total_steps = 0,
                        avg_battery = 0,
                        total_errors = 0
                    }
                };
            }

            // Теперь работаем со строго типизированными данными - никаких dynamic
            return new
            {
                daily_stats = results.Select(r => new
                {
                    report_date = r.ReportDate,
                    avg_battery_level = r.AvgBatteryLevel,
                    total_steps = r.TotalSteps,
                    max_muscle_voltage = r.MaxMuscleVoltage,
                    errors_count = r.ErrorsCount
                }),
                total_days = results.Count,
                avg_daily_steps = results.Average(r => r.TotalSteps),  // Больше никаких dynamic
                summary = new
                {
                    total_steps = results.Sum(r => r.TotalSteps),
                    avg_battery = results.Average(r => r.AvgBatteryLevel),
                    total_errors = results.Sum(r => r.ErrorsCount)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying ClickHouse for user {Username}", username);
            throw;
        }
    }
 
    #endregion
}

public class DailyReportData
{
    public string ReportDate { get; set; } = string.Empty;
    public float AvgBatteryLevel { get; set; }
    public int TotalSteps { get; set; }
    public float MaxMuscleVoltage { get; set; }
    public int ErrorsCount { get; set; }
}
