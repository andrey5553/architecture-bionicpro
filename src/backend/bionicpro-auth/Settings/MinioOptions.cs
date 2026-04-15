namespace KeycloakAuthService.Settings;
public class MinioOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "reports";
    public string ReportPrefix { get; set; } = "reports";
    public bool UseSSL { get; set; } = false;
}

