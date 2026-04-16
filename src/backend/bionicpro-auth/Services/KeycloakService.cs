using System.Text;
using System.Text.Json;
using KeycloakAuthService.Models;

namespace KeycloakAuthService.Services;

public class KeycloakService : IKeycloakService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakService> _logger;

    public KeycloakService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<KeycloakService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TokenResponse> GetTokenAsync(string username, string password)
    {
        _logger.LogInformation("=== GetTokenAsync START ===");
        _logger.LogInformation("Username: {Username}", username);

        var tokenUrl = $"{_configuration["Keycloak:BaseUrl"]}/realms/{_configuration["Keycloak:Realm"]}{_configuration["Keycloak:TokenEndpoint"]}";
        _logger.LogInformation("Token URL: {TokenUrl}", tokenUrl);
        _logger.LogInformation("ClientId: {ClientId}", _configuration["Keycloak:ClientId"]);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _configuration["Keycloak:ClientId"]!),
            new KeyValuePair<string, string>("client_secret", _configuration["Keycloak:ClientSecret"]!),
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        });

        _logger.LogInformation("Sending request to Keycloak...");
        var response = await _httpClient.PostAsync(tokenUrl, content);

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("Response Status Code: {StatusCode}", response.StatusCode);
        _logger.LogInformation("Response Body (full): {ResponseBody}", responseBody);

        if (!string.IsNullOrEmpty(responseBody))
        {
            _logger.LogInformation("Response Body length: {Length}", responseBody.Length);
            if (responseBody.Length > 100)
            {
                _logger.LogInformation("Response Body (first 200 chars): {Preview}", responseBody.Substring(0, Math.Min(200, responseBody.Length)));
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak token error. Status: {StatusCode}, Error: {Error}", response.StatusCode, responseBody);
            throw new UnauthorizedAccessException($"Invalid credentials. Keycloak response: {responseBody}");
        }

        _logger.LogInformation("Response successful, deserializing...");

        // Настройка для десериализации camelCase
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        TokenResponse? tokenResponse = null;
        try
        {
            tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseBody, options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON Deserialization failed");
            _logger.LogError("Failed to parse JSON: {ResponseBody}", responseBody);
            throw new Exception($"Failed to parse token response: {ex.Message}");
        }

        if (tokenResponse == null)
        {
            _logger.LogError("Deserialization returned null");
            throw new Exception("Failed to parse token response - result is null");
        }

        _logger.LogInformation("=== Deserialized TokenResponse ===");
        _logger.LogInformation("AccessToken: {HasValue} (length: {Length})",
            !string.IsNullOrEmpty(tokenResponse.AccessToken),
            tokenResponse.AccessToken?.Length ?? 0);
        _logger.LogInformation("RefreshToken: {HasValue} (length: {Length})",
            !string.IsNullOrEmpty(tokenResponse.RefreshToken),
            tokenResponse.RefreshToken?.Length ?? 0);
        _logger.LogInformation("ExpiresIn: {ExpiresIn}", tokenResponse.ExpiresIn);
        _logger.LogInformation("RefreshExpiresIn: {RefreshExpiresIn}", tokenResponse.RefreshExpiresIn);
        _logger.LogInformation("TokenType: {TokenType}", tokenResponse.TokenType);
        _logger.LogInformation("SessionState: {SessionState}", tokenResponse.SessionState);

        if (!string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            _logger.LogInformation("AccessToken preview: {Preview}",
                tokenResponse.AccessToken.Substring(0, Math.Min(50, tokenResponse.AccessToken.Length)));
        }

        _logger.LogInformation("=== GetTokenAsync END ===");
        return tokenResponse;
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken)
    {
        _logger.LogInformation("=== RefreshTokenAsync START ===");
        _logger.LogInformation("RefreshToken length: {Length}", refreshToken?.Length ?? 0);

        var tokenUrl = $"{_configuration["Keycloak:BaseUrl"]}/realms/{_configuration["Keycloak:Realm"]}{_configuration["Keycloak:TokenEndpoint"]}";
        _logger.LogInformation("Token URL: {TokenUrl}", tokenUrl);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _configuration["Keycloak:ClientId"]!),
            new KeyValuePair<string, string>("client_secret", _configuration["Keycloak:ClientSecret"]!),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await _httpClient.PostAsync(tokenUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Response Status Code: {StatusCode}", response.StatusCode);
        _logger.LogInformation("Response Body (first 500 chars): {Preview}",
            responseBody.Substring(0, Math.Min(500, responseBody.Length)));

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak refresh error: {Error}", responseBody);
            throw new UnauthorizedAccessException("Invalid refresh token");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseBody, options);

        if (tokenResponse != null)
        {
            _logger.LogInformation("Refresh successful. New AccessToken length: {Length}",
                tokenResponse.AccessToken?.Length ?? 0);
        }

        _logger.LogInformation("=== RefreshTokenAsync END ===");
        return tokenResponse ?? throw new Exception("Failed to parse refresh response");
    }

    public async Task<bool> LogoutAsync(string refreshToken)
    {
        _logger.LogInformation("=== LogoutAsync START ===");

        var logoutUrl = $"{_configuration["Keycloak:BaseUrl"]}/realms/{_configuration["Keycloak:Realm"]}{_configuration["Keycloak:LogoutEndpoint"]}";
        _logger.LogInformation("Logout URL: {LogoutUrl}", logoutUrl);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _configuration["Keycloak:ClientId"]!),
            new KeyValuePair<string, string>("client_secret", _configuration["Keycloak:ClientSecret"]!),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await _httpClient.PostAsync(logoutUrl, content);
        _logger.LogInformation("Logout response status: {StatusCode}", response.StatusCode);

        _logger.LogInformation("=== LogoutAsync END ===");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        _logger.LogInformation("=== ValidateTokenAsync START ===");

        try
        {
            var userInfoUrl = $"{_configuration["Keycloak:BaseUrl"]}/realms/{_configuration["Keycloak:Realm"]}{_configuration["Keycloak:UserInfoEndpoint"]}";
            _logger.LogInformation("UserInfo URL: {UserInfoUrl}", userInfoUrl);

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.GetAsync(userInfoUrl);
            _logger.LogInformation("Validate response status: {StatusCode}", response.StatusCode);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return false;
        }
    }

    public async Task<UserInfoResponse> GetUserInfoAsync(string accessToken)
    {
        _logger.LogInformation("=== GetUserInfoAsync START ===");

        var userInfoUrl = $"{_configuration["Keycloak:BaseUrl"]}/realms/{_configuration["Keycloak:Realm"]}{_configuration["Keycloak:UserInfoEndpoint"]}";
        _logger.LogInformation("UserInfo URL: {UserInfoUrl}", userInfoUrl);

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.GetAsync(userInfoUrl);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Response Status: {StatusCode}", response.StatusCode);
        _logger.LogInformation("Response Body: {ResponseBody}", responseBody);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get user info");
            throw new UnauthorizedAccessException("Invalid token");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var userInfo = JsonSerializer.Deserialize<UserInfoResponse>(responseBody, options);

        if (userInfo != null)
        {
            _logger.LogInformation("UserInfo: Sub={Sub}, Username={PreferredUsername}, Email={Email}",
                userInfo.Sub, userInfo.PreferredUsername, userInfo.Email);
        }

        _logger.LogInformation("=== GetUserInfoAsync END ===");
        return userInfo ?? throw new Exception("Failed to parse user info");
    }
}