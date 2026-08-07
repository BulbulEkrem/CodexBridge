using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBridge.Host.Push;

/// <summary>Telefonun push kimlik bilgisi. Token platforma göre APNs cihaz token'ı ya da FCM registration token'ı.</summary>
public sealed record DeviceRegistration
{
    [JsonPropertyName("token")] public required string Token { get; init; }
    [JsonPropertyName("platform")] public required PushPlatform Platform { get; init; }
    /// <summary>Kullanıcıya gösterilebilir cihaz adı (ör. "iPhone 15"). İsteğe bağlı.</summary>
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("registeredAt")] public DateTimeOffset RegisteredAt { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PushPlatform>))]
public enum PushPlatform { Apns, Fcm }

/// <summary>Kayıtlı telefon cihazları deposu. Push, telefon asla doğrudan sağlayıcıya gitmesin diye host'tan itilir.</summary>
public interface IDeviceRegistry
{
    Task<IReadOnlyList<DeviceRegistration>> ListAsync(CancellationToken ct = default);
    /// <summary>Cihazı ekler; aynı token varsa günceller (idempotent).</summary>
    Task AddAsync(DeviceRegistration device, CancellationToken ct = default);
    /// <summary>Token'a göre siler. Silindiyse true.</summary>
    Task<bool> RemoveAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Dosya tabanlı cihaz deposu (JSON). Push token'ları hassastır → dosya yalnızca kullanıcı
/// profilinde tutulur, snapshot'a/loga sızmaz. Eşzamanlı erişim tek kilitle serileştirilir.
/// </summary>
public sealed class JsonFileDeviceRegistry : IDeviceRegistry
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public JsonFileDeviceRegistry(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>Varsayılan konum: %LOCALAPPDATA%\CodexBridge\devices.json.</summary>
    public static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexBridge", "devices.json");

    public async Task<IReadOnlyList<DeviceRegistration>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await LoadUnlockedAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(DeviceRegistration device, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var list = (await LoadUnlockedAsync(ct)).ToList();
            list.RemoveAll(d => string.Equals(d.Token, device.Token, StringComparison.Ordinal));
            list.Add(device);
            await SaveUnlockedAsync(list, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string token, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var list = (await LoadUnlockedAsync(ct)).ToList();
            int removed = list.RemoveAll(d => string.Equals(d.Token, token, StringComparison.Ordinal));
            if (removed > 0) await SaveUnlockedAsync(list, ct);
            return removed > 0;
        }
        finally { _gate.Release(); }
    }

    private async Task<List<DeviceRegistration>> LoadUnlockedAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return [];
        await using var fs = File.OpenRead(_path);
        var list = await JsonSerializer.DeserializeAsync<List<DeviceRegistration>>(fs, Json, ct);
        return list ?? [];
    }

    private async Task SaveUnlockedAsync(List<DeviceRegistration> list, CancellationToken ct)
    {
        // Atomik yazım: geçici dosyaya yaz, sonra taşı (yarı yazılmış dosya kalmasın).
        var tmp = _path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, list, Json, ct);
        File.Move(tmp, _path, overwrite: true);
    }
}
