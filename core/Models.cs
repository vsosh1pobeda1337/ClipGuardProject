using System;

namespace ClipGuard.Core;

public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public sealed class HotkeyDefinition
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Alt;
    public string Key { get; set; } = "C";
}

public sealed class AppSettings
{
    public HotkeyDefinition CopyHotkey { get; set; } = new() { Modifiers = HotkeyModifiers.Alt, Key = "N" };
    public HotkeyDefinition PasteHotkey { get; set; } = new() { Modifiers = HotkeyModifiers.Alt, Key = "M" };
    public PasswordRecord? Password { get; set; }
}

public sealed class PasswordRecord
{
    public string SaltBase64 { get; set; } = string.Empty;
    public string HashBase64 { get; set; } = string.Empty;
    public int Iterations { get; set; } = 150_000;
}

public sealed class EncryptedBuffer
{
    public string CiphertextBase64 { get; set; } = string.Empty;
    public string NonceBase64 { get; set; } = string.Empty;
    public string TagBase64 { get; set; } = string.Empty;
    public string SaltBase64 { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClipboardPayload
{
    public ClipboardPayload(string content, DateTimeOffset updatedAt)
    {
        Content = content;
        UpdatedAt = updatedAt;
    }

    public string Content { get; }
    public DateTimeOffset UpdatedAt { get; }
}

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Operation { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
