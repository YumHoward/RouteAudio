using System.Text.Json;
using System.Text.Json.Serialization;
using RouteAudio.Core.Models;

namespace RouteAudio.Core.Config;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public int ReconcileSeconds { get; set; } = 3;

    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>隐藏虚拟音频设备（有路由的虚拟设备仍会显示）。</summary>
    public bool HideVirtualDevices { get; set; } = true;
}

public sealed class UiState
{
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 640;
    public double? Left { get; set; }
    public double? Top { get; set; }
}

public sealed class ConfigDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string ActiveProfileId { get; set; } = "default";

    public List<Profile> Profiles { get; set; } = [Profile.CreateDefault()];

    public Dictionary<string, string> DeviceAliases { get; set; } = [];

    public AppSettings Settings { get; set; } = new();

    public UiState Ui { get; set; } = new();

    [JsonIgnore]
    public Profile ActiveProfile
    {
        get
        {
            var profile = Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);
            if (profile is not null) return profile;
            if (Profiles.Count == 0) Profiles.Add(Profile.CreateDefault());
            ActiveProfileId = Profiles[0].Id;
            return Profiles[0];
        }
    }

    public static ConfigDocument CreateDefault() => new()
    {
        ActiveProfileId = "default",
        Profiles = [Profile.CreateDefault()],
        Settings = new AppSettings(),
    };

    public void Normalize()
    {
        if (Profiles.Count == 0) Profiles.Add(Profile.CreateDefault());
        if (Profiles.All(p => p.Id != ActiveProfileId)) ActiveProfileId = Profiles[0].Id;
        DeviceAliases ??= [];
        Settings ??= new AppSettings();
        Ui ??= new UiState();
    }
}

/// <summary>
/// 配置文件读写：JSON、原子写、损坏回退备份。线程安全（单写者）。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();

    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath;
        Current = Load();
    }

    public string FilePath { get; }

    public ConfigDocument Current { get; private set; }

    public string? LastError { get; private set; }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RouteAudio",
        "config.json");

    public ConfigDocument Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return ConfigDocument.CreateDefault();

            var json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize<ConfigDocument>(json, SerializerOptions);
            if (doc is null) return ConfigDocument.CreateDefault();
            doc.Normalize();
            return doc;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            var backup = FilePath + ".bak";
            try
            {
                if (File.Exists(backup))
                {
                    var json = File.ReadAllText(backup);
                    var doc = JsonSerializer.Deserialize<ConfigDocument>(json, SerializerOptions);
                    if (doc is not null)
                    {
                        doc.Normalize();
                        return doc;
                    }
                }
            }
            catch
            {
                // 备份也损坏，退回默认配置
            }

            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
            }
            catch
            {
                // 忽略
            }

            return ConfigDocument.CreateDefault();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveCore(Current);
        }
    }

    public void Replace(ConfigDocument document)
    {
        lock (_gate)
        {
            document.Normalize();
            Current = document;
            SaveCore(Current);
        }
    }

    private void SaveCore(ConfigDocument document)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(document, SerializerOptions);
            File.WriteAllText(temp, json);

            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
    }
}
