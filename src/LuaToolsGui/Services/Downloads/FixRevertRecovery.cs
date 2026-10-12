using System.IO;
using System.Text.Json;
using LuaToolsGui.Models;

namespace LuaToolsGui.Services.Downloads;

/// <summary>Persistent recovery copies made before force revert; never automatically deleted.</summary>
internal sealed class FixRevertRecovery
{
    public string DirectoryPath { get; }
    private readonly Dictionary<string, string?> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly RecoveryRecord _record;

    private sealed record RecoveryFile(string RelativePath, string BackupPath, string Hash, string Action);
    private sealed class RecoveryRecord(string fixId)
    {
        public string FixId { get; } = fixId;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "preparing";
        public List<RecoveryFile> Files { get; } = [];
    }

    private FixRevertRecovery(string directory, string fixId)
    {
        DirectoryPath = directory;
        _record = new RecoveryRecord(fixId);
    }

    public static FixRevertRecovery Prepare(string installDir, string fixId, DenuvoFixRecord record)
    {
        // Validate every entry and required original backup before touching any game file.
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in record.Files)
        {
            string dest = ManifestJobFactory.ResolveInside(installDir, entry.RelativePath)
                ?? throw new IOException(entry.RelativePath);
            if (!paths.Add(dest) || Directory.Exists(dest)) throw new IOException(entry.RelativePath);
            if (entry.Action == "modified")
            {
                string backup = ManifestJobFactory.ResolveInside(
                    Path.Combine(installDir, ManifestJobFactory.FixRecordDir), entry.BackupPath ?? "")
                    ?? throw new IOException(entry.RelativePath);
                if (FileHash.Sha256(backup) is not { } hash ||
                    (!string.IsNullOrEmpty(entry.HashBefore) &&
                     !string.Equals(hash, entry.HashBefore, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException(backup);
            }
            else if (entry.Action != "added") throw new IOException(entry.RelativePath);
        }

        string name = $"{ManifestJobFactory.SafeFixKey(fixId)}_{DateTime.Now:yyyy-MM-dd_HHmmss}_{Guid.NewGuid():N}";
        string directory = Path.Combine(installDir, ".luatools-recovery", name);
        var recovery = new FixRevertRecovery(directory, fixId);
        Directory.CreateDirectory(directory);
        recovery.Save();

        foreach (var entry in record.Files)
        {
            string dest = Path.GetFullPath(Path.Combine(installDir, entry.RelativePath));
            if (!File.Exists(dest))
            {
                recovery._hashes[dest] = null;
                continue;
            }

            string relative = Path.Combine("files", entry.RelativePath);
            string copy = ManifestJobFactory.ResolveInside(directory, relative)
                ?? throw new IOException(entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            // Hold the source against writes while copying so the saved hash describes those bytes.
            using (var source = File.Open(dest, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = File.Open(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            string hash = FileHash.Sha256(copy) ?? throw new IOException(copy);
            recovery._hashes[dest] = hash;
            recovery._record.Files.Add(new RecoveryFile(entry.RelativePath, relative, hash,
                entry.Action == "modified" ? "restore" : "delete"));
            recovery.Save();
        }

        recovery.SetStatus("prepared");
        return recovery;
    }

    /// <summary>Stop if a file changed again after its recovery copy was made.</summary>
    public bool MatchesCurrent(string path) => _hashes.TryGetValue(path, out string? hash) &&
        (hash is null ? !File.Exists(path) : string.Equals(FileHash.Sha256(path), hash, StringComparison.OrdinalIgnoreCase));

    public void SetStatus(string status)
    {
        _record.Status = status;
        Save();
    }

    private void Save() => File.WriteAllText(Path.Combine(DirectoryPath, "recovery.json"),
        JsonSerializer.Serialize(_record, new JsonSerializerOptions
            { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}
