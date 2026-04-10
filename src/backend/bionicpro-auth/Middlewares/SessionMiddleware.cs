using System.Text.Json;
using KeycloakAuthService.Models;

namespace KeycloakAuthService.Middleware;

public class SessionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionMiddleware> _logger;

    public SessionMiddleware(RequestDelegate next, ILogger<SessionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var session = context.Session;
        var tokenResponseJson = session.GetString("TokenResponse");
        var username = session.GetString("Username");

        _logger.LogInformation("SessionMiddleware: Session ID: {SessionId}", session.Id);
        _logger.LogInformation("SessionMiddleware: TokenResponse exists: {HasToken}", !string.IsNullOrEmpty(tokenResponseJson));
        _logger.LogInformation("SessionMiddleware: Username: {Username}", username);

        // Проверяем, нужно ли выполнить ротацию сессии
        // Ротация выполняется при наличии авторизации и по умолчанию включена
        var shouldRotate = ShouldRotateSession(context);

        if (!string.IsNullOrEmpty(tokenResponseJson) && shouldRotate)
        {
            _logger.LogInformation("SessionMiddleware: Performing session rotation for user {Username}", username);

            // Сохраняем данные текущей сессии
            var oldSessionId = session.Id;
            var oldTokenResponseJson = tokenResponseJson;
            var oldUsername = username;

            // Очищаем старую сессию
            session.Clear();

            // Загружаем новую сессию
            await session.LoadAsync();

            // Восстанавливаем данные в новой сессии
            if (!string.IsNullOrEmpty(oldTokenResponseJson))
            {
                session.SetString("TokenResponse", oldTokenResponseJson);
                session.SetString("Username", oldUsername);
            }

            // Сохраняем изменения
            await session.CommitAsync();

            _logger.LogInformation("SessionMiddleware: Session rotated: {OldSessionId} -> {NewSessionId}",
                oldSessionId, session.Id);

            // Обновляем tokenResponseJson для дальнейшего использования
            tokenResponseJson = session.GetString("TokenResponse");
            username = session.GetString("Username");
        }

        if (!string.IsNullOrEmpty(tokenResponseJson))
        {
            try
            {
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(tokenResponseJson);
                if (tokenResponse != null)
                {
                    context.Items["AccessToken"] = tokenResponse.AccessToken;
                    context.Items["RefreshToken"] = tokenResponse.RefreshToken;
                    context.Items["Username"] = username;

                    _logger.LogInformation("SessionMiddleware: Set AccessToken for user {Username}", username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SessionMiddleware: Failed to deserialize TokenResponse");
            }
        }

        await _next(context);
    }

    private bool ShouldRotateSession(HttpContext context)
    {
        // Проверяем, есть ли уже токен в сессии (пользователь авторизован)
        var hasToken = !string.IsNullOrEmpty(context.Session.GetString("TokenResponse"));

        if (!hasToken)
        {
            return false; // Неавторизованным пользователям ротация не нужна
        }

        // Проверяем заголовок от фронтенда (опционально)
        var clientRotation = context.Request.Headers["X-Session-Rotate"].ToString();
        if (clientRotation == "false")
        {
            return false;
        }

        // Проверяем, не была ли уже выполнена ротация в этом запросе
        var rotationPerformed = context.Items.ContainsKey("SessionRotated");
        if (rotationPerformed)
        {
            return false;
        }

        // Отмечаем, что ротация будет выполнена
        context.Items["SessionRotated"] = true;

        // Для демонстрации - ротация при каждом запросе
        // В продакшене можно ротировать раз в N запросов или по времени
        _logger.LogDebug("SessionMiddleware: Session rotation will be performed");
        return true;
    }
}
