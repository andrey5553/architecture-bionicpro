using KeycloakAuthService.Models;

namespace KeycloakAuthService.Services;

public interface IKeycloakService
{
    Task<TokenResponse> GetTokenAsync(string username, string password);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken);
    Task<bool> LogoutAsync(string refreshToken);
    Task<bool> ValidateTokenAsync(string accessToken);
    Task<UserInfoResponse> GetUserInfoAsync(string accessToken);
}

public class UserInfoResponse
{
    public string Sub { get; set; } = string.Empty;
    public string PreferredUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string Name { get; set; } = string.Empty;
}