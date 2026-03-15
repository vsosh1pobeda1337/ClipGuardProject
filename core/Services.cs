using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ClipGuard.Core;

public sealed class ClipboardVault : IClipboardVault
{
    private readonly IClipboardStore _store;
    private readonly IEncryptionService _crypto;
    private readonly ILogWriter _log;

    public ClipboardVault(IClipboardStore store, IEncryptionService crypto, ILogWriter log)
    {
        _store = store;
        _crypto = crypto;
        _log = log;
    }

    public async Task SaveAsync(string plaintext, string password, CancellationToken cancellationToken = default)
    {
        var encrypted = _crypto.Encrypt(plaintext, password);
        await _store.SaveAsync(encrypted, cancellationToken);

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = "ctrl c",
            Content = LogContentCodec.Encode(plaintext, password, _crypto)
        };

        await _log.AppendAsync(entry, cancellationToken);
    }

    public async Task<ClipboardPayload?> LoadAsync(string password, CancellationToken cancellationToken = default)
    {
        var encrypted = await _store.LoadAsync(cancellationToken);
        if (encrypted is null)
        {
            return null;
        }

        var plaintext = _crypto.Decrypt(encrypted, password);

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = "ctrl v",
            Content = LogContentCodec.Encode(plaintext, password, _crypto)
        };

        await _log.AppendAsync(entry, cancellationToken);

        return new ClipboardPayload(plaintext, encrypted.UpdatedAt);
    }

    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        var encrypted = await _store.LoadAsync(cancellationToken);
        return encrypted is not null && !string.IsNullOrWhiteSpace(encrypted.CiphertextBase64);
    }
}

internal static class LogContentCodec
{
    private const string Prefix = "enc:";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Encode(string plaintext, string password, IEncryptionService crypto)
    {
        var encrypted = crypto.Encrypt(plaintext, password);
        var json = JsonSerializer.Serialize(encrypted, Options);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return Prefix + payload;
    }

    public static bool TryDecode(string content, string password, IEncryptionService crypto, out string plaintext, out bool wasEncrypted)
    {
        plaintext = string.Empty;
        wasEncrypted = !string.IsNullOrWhiteSpace(content) && content.StartsWith(Prefix, StringComparison.Ordinal);
        if (!wasEncrypted)
        {
            plaintext = content ?? string.Empty;
            return true;
        }

        var payload = content[Prefix.Length..];
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var encrypted = JsonSerializer.Deserialize<EncryptedBuffer>(json, Options);
            if (encrypted is null)
            {
                return false;
            }

            plaintext = crypto.Decrypt(encrypted, password);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class AppDataPaths
{
    public string RootPath { get; }
    public string ClipboardPath => Path.Combine(RootPath, "secure-clipboard.json");
    public string SettingsPath => Path.Combine(RootPath, "settings.json");
    public string LogPath => Path.Combine(RootPath, "activity-log.jsonl");

    public AppDataPaths(string? rootOverride = null)
    {
        RootPath = rootOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipGuard");
        Directory.CreateDirectory(RootPath);
    }
}

public sealed class FileClipboardStore : IClipboardStore
{
    private readonly AppDataPaths _paths;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileClipboardStore(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task SaveAsync(EncryptedBuffer buffer, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(buffer, _options);
        await File.WriteAllTextAsync(_paths.ClipboardPath, json, cancellationToken);
    }

    public async Task<EncryptedBuffer?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ClipboardPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_paths.ClipboardPath, cancellationToken);
        return JsonSerializer.Deserialize<EncryptedBuffer>(json, _options);
    }
}

public sealed class JsonLogWriter : ILogWriter
{
    private readonly AppDataPaths _paths;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public JsonLogWriter(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task AppendAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        var line = JsonSerializer.Serialize(entry, _options);
        await File.AppendAllTextAsync(_paths.LogPath, line + Environment.NewLine, cancellationToken);
    }
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly AppDataPaths _paths;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSettingsStore(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsPath))
        {
            return new AppSettings();
        }

        var json = await File.ReadAllTextAsync(_paths.SettingsPath, cancellationToken);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _options);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, _options);
        await File.WriteAllTextAsync(_paths.SettingsPath, json, cancellationToken);
    }
}

public sealed class ForegroundAppService
{
    public ForegroundAppInfo CaptureActive()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return new ForegroundAppInfo(IntPtr.Zero, "Unknown");
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        try
        {
            var process = Process.GetProcessById((int)processId);
            return new ForegroundAppInfo(handle, process.ProcessName);
        }
        catch
        {
            return new ForegroundAppInfo(handle, "Unknown");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

public sealed class ForegroundAppInfo
{
    public ForegroundAppInfo(IntPtr handle, string name)
    {
        Handle = handle;
        Name = name;
    }

    public IntPtr Handle { get; }
    public string Name { get; }
}

public sealed class AppServices
{
    private readonly ISettingsStore _settingsStore;

    public AppServices(IEncryptionService encryption, IPasswordService passwords)
    {
        Paths = new AppDataPaths();
        _settingsStore = new JsonSettingsStore(Paths);
        Encryption = encryption;
        Passwords = passwords;
        Vault = new ClipboardVault(new FileClipboardStore(Paths), Encryption, new JsonLogWriter(Paths));
    }

    public AppDataPaths Paths { get; }
    public AppSettings Settings { get; private set; } = new();
    public IClipboardVault Vault { get; }
    public IEncryptionService Encryption { get; }
    public IPasswordService Passwords { get; }

    public async Task InitializeAsync()
    {
        Settings = await _settingsStore.LoadAsync();
    }

    public async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(Settings);
    }

    public async Task<bool> ReencryptLogAsync(string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Paths.LogPath))
        {
            return true;
        }

        var raw = await File.ReadAllTextAsync(Paths.LogPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return true;
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var updated = new List<string>(lines.Length);
        var failed = false;

        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line, options);
                if (entry is null)
                {
                    updated.Add(line);
                    continue;
                }

                if (LogContentCodec.TryDecode(entry.Content, oldPassword, Encryption, out var plaintext, out var wasEncrypted))
                {
                    entry.Content = LogContentCodec.Encode(plaintext, newPassword, Encryption);
                    updated.Add(JsonSerializer.Serialize(entry, options));
                }
                else
                {
                    failed |= wasEncrypted;
                    updated.Add(line);
                }
            }
            catch
            {
                updated.Add(line);
            }
        }

        if (failed)
        {
            return false;
        }

        var result = string.Join(Environment.NewLine, updated) + Environment.NewLine;
        await File.WriteAllTextAsync(Paths.LogPath, result, cancellationToken);
        return true;
    }
}
