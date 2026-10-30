using MicaRomManager.Core.Data;

namespace MicaRomManager.Core.Ai;

/// <summary>AI 接入配置；Key 加密后存 settings 表，或由环境变量覆盖。</summary>
public sealed class AiOptions
{
    public const string BaseUrlKey = "ai.base_url";
    public const string ApiKeyKey = "ai.api_key";
    public const string ModelKey = "ai.model";
    public const string TimeoutKey = "ai.timeout_seconds";
    public const string BatchLimitKey = "ai.batch_limit";

    public const int DefaultTimeoutSeconds = 60;
    public const int DefaultBatchLimit = 50;
    private const string EncryptedPrefix = "enc:";

    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
    public int BatchLimit { get; init; } = DefaultBatchLimit;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);

    public static AiOptions Load(SettingsRepository settings, ISecretProtector protector)
    {
        var timeout = int.TryParse(settings.Get(TimeoutKey), out var seconds)
            ? Math.Clamp(seconds, 10, 600)
            : DefaultTimeoutSeconds;
        var limit = int.TryParse(settings.Get(BatchLimitKey), out var count)
            ? Math.Clamp(count, 1, 200)
            : DefaultBatchLimit;

        var key = Environment.GetEnvironmentVariable("MICA_AI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) key = ReadKey(settings, protector);

        return new AiOptions
        {
            BaseUrl = settings.Get(BaseUrlKey)?.Trim() ?? "",
            ApiKey = key?.Trim() ?? "",
            Model = settings.Get(ModelKey)?.Trim() ?? "",
            TimeoutSeconds = timeout,
            BatchLimit = limit,
        };
    }

    public static void SaveApiKey(SettingsRepository settings, ISecretProtector protector, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            settings.Set(ApiKeyKey, null);
            return;
        }

        settings.Set(ApiKeyKey, EncryptedPrefix + protector.Protect(apiKey.Trim()));
    }

    public static string MaskedKey(SettingsRepository settings, ISecretProtector protector)
    {
        var key = ReadKey(settings, protector);
        if (string.IsNullOrWhiteSpace(key)) return "";
        return key.Length <= 8 ? "••••" : $"{key[..4]}••••{key[^4..]}";
    }

    private static string? ReadKey(SettingsRepository settings, ISecretProtector protector)
    {
        var stored = settings.Get(ApiKeyKey);
        if (string.IsNullOrWhiteSpace(stored)) return "";

        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return stored;

        try
        {
            return protector.Unprotect(stored[EncryptedPrefix.Length..]);
        }
        catch
        {
            return "";
        }
    }
}
