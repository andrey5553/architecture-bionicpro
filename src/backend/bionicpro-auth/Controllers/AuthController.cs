using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using KeycloakAuthService.Models;
using KeycloakAuthService.Services;

namespace KeycloakAuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IKeycloakService _keycloakService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IKeycloakService keycloakService, ILogger<AuthController> logger)
    {
        _keycloakService = keycloakService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] TokenRequest request)
    {
        try
        {
            var tokens = await _keycloakService.GetTokenAsync(request.Username, request.Password);

            // Сериализуем и логируем
            var tokenJson = JsonSerializer.Serialize(tokens);
            _logger.LogInformation("Token JSON length: {Length}", tokenJson.Length);
            _logger.LogInformation("Token JSON (first 200 chars): {Json}", tokenJson.Substring(0, Math.Min(200, tokenJson.Length)));

            HttpContext.Session.SetString("TokenResponse", tokenJson);
            HttpContext.Session.SetString("Username", request.Username);
            await HttpContext.Session.CommitAsync();

            _logger.LogInformation("Login successful. Session ID: {SessionId}, User: {Username}",
                HttpContext.Session.Id, request.Username);

            // Проверяем, что сохранилось
            var savedJson = HttpContext.Session.GetString("TokenResponse");
            _logger.LogInformation("Verified saved JSON length: {Length}", savedJson?.Length ?? 0);

            return Ok(new
            {
                accessToken = tokens.AccessToken,
                expiresIn = tokens.ExpiresIn,
                tokenType = tokens.TokenType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return Unauthorized(new { error = "Invalid credentials" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            var tokenResponseJson = HttpContext.Session.GetString("TokenResponse");
            if (string.IsNullOrEmpty(tokenResponseJson))
            {
                return Unauthorized(new { error = "No session found" });
            }

            var tokens = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson);
            if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken))
            {
                return Unauthorized(new { error = "Invalid session" });
            }

            var newTokens = await _keycloakService.RefreshTokenAsync(tokens.RefreshToken);

            // Обновляем сессию
            HttpContext.Session.SetString("TokenResponse", JsonSerializer.Serialize(newTokens));

            return Ok(new
            {
                accessToken = newTokens.AccessToken,
                expiresIn = newTokens.ExpiresIn,
                tokenType = newTokens.TokenType
            });
        }
        catch (UnauthorizedAccessException)
        {
            HttpContext.Session.Clear();
            return Unauthorized(new { error = "Refresh failed, please login again" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh error");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var tokenResponseJson = HttpContext.Session.GetString("TokenResponse");
            if (!string.IsNullOrEmpty(tokenResponseJson))
            {
                var tokens = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson);
                if (tokens != null && !string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    await _keycloakService.LogoutAsync(tokens.RefreshToken);
                }
            }

            HttpContext.Session.Clear();
            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout error");
            HttpContext.Session.Clear();
            return Ok(new { message = "Logged out" });
        }
    }

    [HttpGet("userinfo")]
    public async Task<IActionResult> GetUserInfo()
    {
        try
        {
            var accessToken = HttpContext.Items["AccessToken"] as string;
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized(new { error = "No session found" });
            }

            var userInfo = await _keycloakService.GetUserInfoAsync(accessToken);
            return Ok(userInfo);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid session" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetUserInfo error");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("validate")]
    public async Task<IActionResult> Validate()
    {
        var accessToken = HttpContext.Items["AccessToken"] as string;
        if (string.IsNullOrEmpty(accessToken))
        {
            return Unauthorized(new { authenticated = false });
        }

        var isValid = await _keycloakService.ValidateTokenAsync(accessToken);
        return Ok(new { authenticated = isValid });
    }
}