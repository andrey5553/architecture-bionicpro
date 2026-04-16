using System.Text.Json;
using KeycloakAuthService.Models;
using KeycloakAuthService.Services;

namespace KeycloakAuthService.Middleware;

public class TokenRefreshMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenRefreshMiddleware> _logger;

    public TokenRefreshMiddleware(RequestDelegate next, ILogger<TokenRefreshMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IKeycloakService keycloakService)
    {
        var tokenResponseJson = context.Session.GetString("TokenResponse");

        if (!string.IsNullOrEmpty(tokenResponseJson))
        {
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson);

            if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                // Проверяем, не истек ли access_token
                var isValid = await keycloakService.ValidateTokenAsync(tokenResponse.AccessToken);

                if (!isValid)
                {
                    _logger.LogInformation("Access token expired, refreshing...");

                    try
                    {
                        // Обновляем через refresh_token
                        var newTokens = await keycloakService.RefreshTokenAsync(tokenResponse.RefreshToken);

                        // Сохраняем новые токены
                        context.Session.SetString("TokenResponse", JsonSerializer.Serialize(newTokens));
                        await context.Session.CommitAsync();

                        _logger.LogInformation("Token refreshed successfully");

                        // Обновляем Items для текущего запроса
                        context.Items["AccessToken"] = newTokens.AccessToken;
                        context.Items["RefreshToken"] = newTokens.RefreshToken;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Token refresh failed");
                        context.Session.Clear();
                    }
                }
                else
                {
                    context.Items["AccessToken"] = tokenResponse.AccessToken;
                    context.Items["RefreshToken"] = tokenResponse.RefreshToken;
                }
            }
        }

        await _next(context);
    }
}
