using System.Threading;
using System.Threading.Tasks;

namespace ClipGuard.Core;

public interface IClipboardStore
{
    Task SaveAsync(EncryptedBuffer buffer, CancellationToken cancellationToken = default);
    Task<EncryptedBuffer?> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IEncryptionService
{
    EncryptedBuffer Encrypt(string plaintext, string password);
    string Decrypt(EncryptedBuffer encrypted, string password);
}

public interface ILogWriter
{
    Task AppendAsync(LogEntry entry, CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IPasswordService
{
    PasswordRecord CreateRecord(string password);
    bool Verify(string password, PasswordRecord record);
}

public interface IClipboardVault
{
    Task SaveAsync(string plaintext, string password, CancellationToken cancellationToken = default);
    Task<ClipboardPayload?> LoadAsync(string password, CancellationToken cancellationToken = default);
    Task<bool> HasDataAsync(CancellationToken cancellationToken = default);
}
