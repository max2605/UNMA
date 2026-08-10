using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Mafi;
using UNMA.Domain;

namespace UNMA.Runtime;

public sealed class UnmaStateStore
{
    [DataContract]
    private sealed class SchemaVersionProbe
    {
        [DataMember(Name = "SchemaVersion", Order = 1)]
        public int SchemaVersion = 0;
    }

    private readonly string m_path;

    public string Path => m_path;
    public bool IsWriteBlocked { get; private set; }
    public string WriteBlockReason { get; private set; } = "";

    public UnmaStateStore(string modRoot, long gameId)
    {
        m_path = System.IO.Path.Combine(
            modRoot,
            "unma-world-" +
            gameId.ToString("X16", CultureInfo.InvariantCulture) +
            ".json");
    }

    public UnmaConfiguration Load()
    {
        if (!File.Exists(m_path))
        {
            return UnmaConfiguration.CreateDefault();
        }

        try
        {
            var storedSchemaVersion = ReadStoredSchemaVersion();
            if (storedSchemaVersion >
                UnmaConfiguration.CurrentSchemaVersion)
            {
                BlockWritesForFutureSchema(storedSchemaVersion);
                Log.Warning("UNMA: " + WriteBlockReason);
                return UnmaConfiguration.CreateDefault();
            }

            using var stream = File.OpenRead(m_path);
            var serializer = new DataContractJsonSerializer(
                typeof(UnmaConfiguration));
            var configuration =
                serializer.ReadObject(stream) as UnmaConfiguration ??
                UnmaConfiguration.CreateDefault();
            configuration.Normalize();
            return configuration;
        }
        catch (Exception exception)
        {
            Log.Warning(
                "UNMA: Konfiguration konnte nicht geladen werden: " +
                exception.Message);
            TryBackupBrokenFile();
            return UnmaConfiguration.CreateDefault();
        }
    }

    public bool Save(UnmaConfiguration configuration, out string error)
    {
        error = "";
        var temporaryPath = m_path + ".tmp";

        if (IsWriteBlocked)
        {
            error = WriteBlockReason;
            return false;
        }
        if (configuration == null)
        {
            error = "UNMA configuration is missing.";
            return false;
        }
        if (configuration.SchemaVersion >
            UnmaConfiguration.CurrentSchemaVersion)
        {
            BlockWritesForFutureSchema(configuration.SchemaVersion);
            error = WriteBlockReason;
            Log.Warning("UNMA: " + error);
            return false;
        }
        if (TryReadFutureSchemaVersion(out var storedSchemaVersion))
        {
            BlockWritesForFutureSchema(storedSchemaVersion);
            error = WriteBlockReason;
            Log.Warning("UNMA: " + error);
            return false;
        }

        try
        {
            configuration.Normalize();
            using (var stream = File.Create(temporaryPath))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(UnmaConfiguration));
                serializer.WriteObject(stream, configuration);
                stream.Flush();
            }

            if (File.Exists(m_path))
            {
                var backupPath = m_path + ".bak";
                File.Replace(temporaryPath, m_path, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, m_path);
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Log.Warning(
                "UNMA: Konfiguration konnte nicht gespeichert werden: " +
                exception.Message);
            TryDelete(temporaryPath);
            return false;
        }
    }

    private int ReadStoredSchemaVersion()
    {
        using var stream = File.OpenRead(m_path);
        var serializer = new DataContractJsonSerializer(
            typeof(SchemaVersionProbe));
        var probe = serializer.ReadObject(stream) as SchemaVersionProbe;
        return probe?.SchemaVersion ?? 0;
    }

    private bool TryReadFutureSchemaVersion(out int schemaVersion)
    {
        schemaVersion = 0;
        if (!File.Exists(m_path))
        {
            return false;
        }
        try
        {
            schemaVersion = ReadStoredSchemaVersion();
            return schemaVersion > UnmaConfiguration.CurrentSchemaVersion;
        }
        catch
        {
            // Preserve the existing broken-file recovery path. A malformed
            // current file is still replaced atomically and retained as both
            // .bak and, when loaded, .broken-*; only a positively identified
            // future schema makes this store read-only.
            schemaVersion = 0;
            return false;
        }
    }

    private void BlockWritesForFutureSchema(int schemaVersion)
    {
        IsWriteBlocked = true;
        WriteBlockReason =
            "UNMA configuration schema " + schemaVersion +
            " is newer than supported schema " +
            UnmaConfiguration.CurrentSchemaVersion +
            ". The original file was left unchanged and saving is disabled " +
            "for this session.";
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            var backupPath = m_path + ".broken-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(m_path, backupPath, true);
        }
        catch
        {
            // Loading still continues with safe defaults.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
