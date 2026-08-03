namespace Memogram.Configs;

public class WebConfig : IValidableConfig
{
    public static string SectionName => "Web";

    public required string Address { get; set; } = "http://0.0.0.0";

    public required int Port { get; set; } = 8080;

    public double HealthCheckTimeoutSeconds { get; set; } = 10;

    public void Validate()
    {
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException("WebConfig:Port must be between 1 and 65535");

        if (HealthCheckTimeoutSeconds <= 0)
            throw new InvalidOperationException("WebConfig:CheckTimeoutSeconds must be greater than 0");
    }
}
