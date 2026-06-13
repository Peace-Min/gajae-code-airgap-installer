using System.Reflection;
using System.Text.Json;

namespace GajaeCode.AirgapInstaller;

internal sealed record DeploymentSettings(string GatewayBaseUrl, string ModelId, string ProviderId)
{
    public static DeploymentSettings Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GajaeCode.DeploymentSettings");
        if (stream is null)
        {
            return new DeploymentSettings(
                "http://127.0.0.1:8080/anthropic",
                "model-id",
                "internal-anthropic");
        }

        var settings = JsonSerializer.Deserialize<DeploymentSettings>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("내장 배포 설정을 읽을 수 없습니다.");
        if (!Uri.TryCreate(settings.GatewayBaseUrl, UriKind.Absolute, out var gateway) ||
            gateway.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("내장 게이트웨이 주소가 유효하지 않습니다.");
        }
        if (string.IsNullOrWhiteSpace(settings.ModelId) || string.IsNullOrWhiteSpace(settings.ProviderId))
        {
            throw new InvalidOperationException("내장 모델 또는 provider 설정이 비어 있습니다.");
        }
        return settings with { GatewayBaseUrl = settings.GatewayBaseUrl.TrimEnd('/') };
    }
}
