using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using KeycloakAuthService.Models;

namespace KeycloakAuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(ILogger<ReportsController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetReport()
    {
        _logger.LogInformation("=== GetReport START ===");

        try
        {
            // Читаем напрямую из сессии
            var tokenResponseJson = HttpContext.Session.GetString("TokenResponse");
            var username = HttpContext.Session.GetString("Username");

            _logger.LogInformation("TokenResponse from Session: {HasToken}", !string.IsNullOrEmpty(tokenResponseJson));
            _logger.LogInformation("Username from Session: {Username}", username);
            _logger.LogInformation("Session ID: {SessionId}", HttpContext.Session.Id);

            if (string.IsNullOrEmpty(tokenResponseJson))
            {
                _logger.LogWarning("No TokenResponse in session");
                return Unauthorized(new { error = "Not authenticated - no session" });
            }

            // Логируем сырой JSON
            _logger.LogInformation("Raw TokenResponse JSON length: {Length}", tokenResponseJson.Length);
            _logger.LogInformation("Raw TokenResponse JSON (first 500 chars): {Json}",
                tokenResponseJson.Length > 500 ? tokenResponseJson.Substring(0, 500) : tokenResponseJson);

            // Десериализуем токен с подробной обработкой ошибок
            TokenResponse tokenResponse = null;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Игнорируем регистр (camelCase/PascalCase)
                };
                tokenResponse = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson, options);

                if (tokenResponse == null)
                {
                    _logger.LogWarning("Deserialize returned null");
                    return Unauthorized(new { error = "Invalid session data - deserialize returned null" });
                }

                _logger.LogInformation("Deserialize successful");
                _logger.LogInformation("AccessToken present: {HasAccessToken}", !string.IsNullOrEmpty(tokenResponse.AccessToken));
                _logger.LogInformation("RefreshToken present: {HasRefreshToken}", !string.IsNullOrEmpty(tokenResponse.RefreshToken));
                _logger.LogInformation("ExpiresIn: {ExpiresIn}", tokenResponse.ExpiresIn);
                _logger.LogInformation("TokenType: {TokenType}", tokenResponse.TokenType);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON deserialization failed");
                _logger.LogError("Problematic JSON: {Json}", tokenResponseJson);
                return StatusCode(500, new { error = "Session data corrupted", details = jsonEx.Message, rawJson = tokenResponseJson });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during deserialization");
                return StatusCode(500, new { error = "Deserialization error", details = ex.Message });
            }

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogWarning("Invalid TokenResponse - AccessToken is null or empty");
                return Unauthorized(new { error = "Invalid session data - no access token" });
            }

            var accessToken = tokenResponse.AccessToken;
            _logger.LogInformation("AccessToken retrieved from session, length: {Length}", accessToken.Length);
            _logger.LogInformation("AccessToken first 50 chars: {Token}", accessToken.Substring(0, Math.Min(50, accessToken.Length)));

            // Декодируем токен для проверки ролей и срока действия
            System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtToken = null;
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

                if (!handler.CanReadToken(accessToken))
                {
                    _logger.LogWarning("Cannot read token - invalid format");
                    return StatusCode(500, new { error = "Invalid token format" });
                }

                jwtToken = handler.ReadJwtToken(accessToken);
                _logger.LogInformation("Token decoded successfully");
                _logger.LogInformation("Token Subject: {Subject}", jwtToken.Subject);
                _logger.LogInformation("Token Issuer: {Issuer}", jwtToken.Issuer);
                _logger.LogInformation("Token Audience: {Audience}", string.Join(", ", jwtToken.Audiences));

                // НОВОЕ: Проверяем срок действия токена
                var validTo = jwtToken.ValidTo;
                var now = DateTime.UtcNow;
                var timeLeft = validTo - now;

                _logger.LogInformation("Token valid from: {ValidFrom} to: {ValidTo}", jwtToken.ValidFrom, validTo);
                _logger.LogInformation("Current time (UTC): {Now}", now);
                _logger.LogInformation("Token expires in: {TimeLeft} seconds", timeLeft.TotalSeconds);

                if (validTo < now)
                {
                    _logger.LogWarning("Token has expired! ValidTo: {ValidTo}, Now: {Now}", validTo, now);
                    // TokenRefreshMiddleware должен был обновить токен, но если нет - сообщаем клиенту
                    return Unauthorized(new { error = "Token expired", needsRefresh = true });
                }

                if (timeLeft.TotalSeconds < 30)
                {
                    _logger.LogInformation("Token is about to expire in {Seconds} seconds, refresh recommended", timeLeft.TotalSeconds);
                    // Добавляем заголовок для фронтенда, что скоро истечет
                    Response.Headers.Append("X-Token-Expiring", timeLeft.TotalSeconds.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading JWT token");
                return StatusCode(500, new { error = "Error processing token", details = ex.Message });
            }

            // Ищем роли в токене
            var roles = new List<string>();

            _logger.LogInformation("Total claims in token: {Count}", jwtToken.Claims.Count());
            foreach (var claim in jwtToken.Claims.Take(10))
            {
                _logger.LogInformation("Claim: Type={Type}, Value={Value}", claim.Type, claim.Value);
            }

            // Проверяем realm_access
            var realmAccessClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "realm_access");
            if (realmAccessClaim != null)
            {
                _logger.LogInformation("realm_access claim found: {Value}", realmAccessClaim.Value);
                try
                {
                    using var doc = JsonDocument.Parse(realmAccessClaim.Value);
                    if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                    {
                        var realmRoles = rolesElement.EnumerateArray().Select(r => r.GetString()).ToList();
                        roles.AddRange(realmRoles);
                        _logger.LogInformation("Roles from realm_access: {Roles}", string.Join(", ", realmRoles));
                    }
                    else
                    {
                        _logger.LogWarning("No 'roles' property in realm_access");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing realm_access");
                }
            }
            else
            {
                _logger.LogWarning("No realm_access claim found");
            }

            // Проверяем resource_access (для клиентских ролей)
            var resourceAccessClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "resource_access");
            if (resourceAccessClaim != null)
            {
                _logger.LogInformation("resource_access claim found");
                try
                {
                    using var doc = JsonDocument.Parse(resourceAccessClaim.Value);
                    foreach (var client in doc.RootElement.EnumerateObject())
                    {
                        if (client.Value.TryGetProperty("roles", out var clientRolesElement))
                        {
                            var clientRoles = clientRolesElement.EnumerateArray().Select(r => r.GetString()).ToList();
                            roles.AddRange(clientRoles);
                            _logger.LogInformation("Roles from client {Client}: {Roles}", client.Name, string.Join(", ", clientRoles));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing resource_access");
                }
            }

            // Проверяем прямые role claims
            var directRoleClaims = jwtToken.Claims.Where(c => c.Type == "roles" || c.Type == "client_roles");
            foreach (var claim in directRoleClaims)
            {
                roles.Add(claim.Value);
                _logger.LogInformation("Direct role claim: {Value}", claim.Value);
            }

            // Убираем дубликаты
            roles = roles.Distinct().ToList();
            _logger.LogInformation("All roles found: {Roles}", string.Join(", ", roles));

            // Проверка на роль prothetic_user
            var hasProtheticRole = roles.Any(r => r.Contains("prothetic_user") || r.Contains("prothetic"));
            _logger.LogInformation("Has prothetic_user role: {HasRole}", hasProtheticRole);

            if (!hasProtheticRole)
            {
                _logger.LogWarning("Access denied - missing prothetic_user role. Available roles: {Roles}", string.Join(", ", roles));
                return StatusCode(403, new
                {
                    error = "Access denied. Prothetic user role required.",
                    availableRoles = roles,
                    user = username
                });
            }

            // Генерация отчета
            _logger.LogInformation("Report generated successfully for user: {Username}", username);
            return Ok(new
            {
                success = true,
                report = "Sensitive report data",
                userRoles = roles,
                user = username,
                timestamp = DateTime.UtcNow,
                message = "Access granted with prothetic_user role",
                // НОВОЕ: Информация о токене для отладки
                tokenInfo = new
                {
                    expiresAt = jwtToken.ValidTo,
                    timeLeftSeconds = (int)(jwtToken.ValidTo - DateTime.UtcNow).TotalSeconds
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetReport");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }
}