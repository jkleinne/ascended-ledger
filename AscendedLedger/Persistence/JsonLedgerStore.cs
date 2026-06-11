using System;
using System.IO;

using Dalamud.Plugin.Services;

namespace AscendedLedger.Persistence;

/// <summary>
/// File-backed ledger store: one ledger.json per plugin config directory,
/// written atomically (temp file + rename) so a crash mid-write can never
/// corrupt the previous good state. Unusable files are backed up, never
/// overwritten, so user data survives even our own bugs.
/// </summary>
internal sealed class JsonLedgerStore : ILedgerStore {
    /// <summary>File name of the persisted contract document.</summary>
    public const string LedgerFileName = "ledger.json";

    /// <summary>Parse guard: refuse files above this size before reading them into memory.</summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    private const string TempFileName = "ledger.json.tmp";
    private const string BackupTimestampFormat = "yyyyMMddHHmmss";

    private readonly string directory;
    private readonly IPluginLog log;

    internal JsonLedgerStore(string directory, IPluginLog log) {
        this.directory = directory;
        this.log = log;
    }

    private string LedgerPath => Path.Combine(directory, LedgerFileName);

    /// <inheritdoc/>
    public LedgerStoreLoadOutcome Load() {
        var path = LedgerPath;
        if (!File.Exists(path)) {
            return new LedgerStoreLoadOutcome(new Ledger(), LedgerLoadError.None, null);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > MaxFileBytes) {
            var backupPath = BackUpUnusableFile(path, $"file size {fileInfo.Length} exceeds cap {MaxFileBytes}");
            return new LedgerStoreLoadOutcome(new Ledger(), LedgerLoadError.StructuralViolation, backupPath);
        }

        string json;
        try {
            json = File.ReadAllText(path);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException($"Cannot read ledger at {path}; refusing to start with a fresh ledger that would overwrite it.", exception);
        }

        var result = LedgerSerializer.Deserialize(json);
        if (result.Error == LedgerLoadError.None) {
            return new LedgerStoreLoadOutcome(result.Ledger!, LedgerLoadError.None, null);
        }

        var backup = BackUpUnusableFile(path, result.Detail ?? result.Error.ToString());
        return new LedgerStoreLoadOutcome(new Ledger(), result.Error, backup);
    }

    /// <inheritdoc/>
    public void Save(Ledger ledger) {
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, TempFileName);
        try {
            File.WriteAllText(tempPath, LedgerSerializer.Serialize(ledger));
            File.Move(tempPath, LedgerPath, overwrite: true);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            try {
                File.Delete(tempPath);
            } catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException) {
                log.Warning("Could not remove stale temp file at {TempPath}.", tempPath);
            }

            throw new InvalidOperationException($"Saving ledger to {LedgerPath} failed.", exception);
        }
    }

    private string BackUpUnusableFile(string path, string reason) {
        var backupPath = $"{path}.bak-{DateTime.UtcNow.ToString(BackupTimestampFormat, System.Globalization.CultureInfo.InvariantCulture)}";
        try {
            File.Move(path, backupPath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException($"Ledger at {path} is unusable ({reason}) and could not be backed up; refusing to continue.", exception);
        }

        log.Warning("Ledger at {Path} was unusable ({Reason}); backed up to {BackupPath} and starting fresh.", path, reason, backupPath);
        return backupPath;
    }
}
