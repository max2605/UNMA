using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Mafi;
using UNMA.Domain;

namespace UNMA.Runtime;

public sealed class UnmaTransferProfileStore
{
    [DataContract]
    private sealed class ProfileSchemaVersionProbe
    {
        [DataMember(Name = "ProfileSchemaVersion", Order = 1)]
        public int ProfileSchemaVersion = 0;
    }

    private readonly string m_path;

    public string Path => m_path;
    public bool IsWriteBlocked { get; private set; }
    public string WriteBlockReason { get; private set; } = "";

    // The runtime owns path selection. This keeps the store usable with a
    // global profile path as well as explicit test or future per-profile paths.
    public UnmaTransferProfileStore(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            throw new ArgumentException(
                "A transfer profile path is required.",
                nameof(profilePath));
        }
        m_path = System.IO.Path.GetFullPath(profilePath);
    }

    public UnmaTransferProfile Load(out string error)
    {
        error = "";
        if (!File.Exists(m_path))
        {
            return null;
        }

        try
        {
            var storedSchemaVersion = ReadStoredSchemaVersion();
            if (storedSchemaVersion >
                UnmaTransferProfile.CurrentProfileSchemaVersion)
            {
                BlockWritesForFutureSchema(storedSchemaVersion);
                error = WriteBlockReason;
                Log.Warning("UNMA: " + error);
                return null;
            }

            using var stream = File.OpenRead(m_path);
            var serializer = new DataContractJsonSerializer(
                typeof(UnmaTransferProfile));
            var profile = serializer.ReadObject(stream) as UnmaTransferProfile;
            if (profile == null)
            {
                throw new SerializationException(
                    "The transfer profile did not contain a profile object.");
            }
            profile.Normalize();
            return profile;
        }
        catch (Exception exception)
        {
            error = "UNMA transfer profile could not be loaded: " +
                    exception.Message;
            Log.Warning("UNMA: " + error);
            TryBackupBrokenFile();
            return null;
        }
    }

    public bool TryLoad(
        out UnmaTransferProfile profile,
        out string error)
    {
        profile = Load(out error);
        return profile != null;
    }

    public bool SaveIfMissing(
        UnmaTransferProfile profile,
        out bool alreadyExists,
        out string error)
    {
        alreadyExists = false;
        error = "";
        var temporaryPath = m_path + ".create-" +
            Guid.NewGuid().ToString("N") + ".tmp";

        if (IsWriteBlocked)
        {
            error = WriteBlockReason;
            return false;
        }
        if (profile == null)
        {
            error = "UNMA transfer profile is missing.";
            return false;
        }
        if (profile.ProfileSchemaVersion >
            UnmaTransferProfile.CurrentProfileSchemaVersion)
        {
            BlockWritesForFutureSchema(profile.ProfileSchemaVersion);
            error = WriteBlockReason;
            Log.Warning("UNMA: " + error);
            return false;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(m_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(m_path))
            {
                alreadyExists = true;
                return false;
            }

            var safeProfile = ConfigurationTransferPolicy.CloneProfile(profile);
            safeProfile.Normalize();
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(UnmaTransferProfile));
                serializer.WriteObject(stream, safeProfile);
                stream.Flush(true);
            }

            try
            {
                // File.Move does not overwrite an existing destination. This
                // makes first-profile creation atomic across UNMA instances.
                File.Move(temporaryPath, m_path);
                return true;
            }
            catch (IOException) when (File.Exists(m_path))
            {
                alreadyExists = true;
                TryDelete(temporaryPath);
                return false;
            }
        }
        catch (Exception exception)
        {
            error = "UNMA transfer profile could not be created: " +
                    exception.Message;
            Log.Warning("UNMA: " + error);
            TryDelete(temporaryPath);
            return false;
        }
    }

    public bool Save(UnmaTransferProfile profile, out string error)
    {
        error = "";
        var temporaryPath = m_path + ".tmp";

        if (IsWriteBlocked)
        {
            error = WriteBlockReason;
            return false;
        }
        if (profile == null)
        {
            error = "UNMA transfer profile is missing.";
            return false;
        }
        if (profile.ProfileSchemaVersion >
            UnmaTransferProfile.CurrentProfileSchemaVersion)
        {
            BlockWritesForFutureSchema(profile.ProfileSchemaVersion);
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
            var safeProfile = ConfigurationTransferPolicy.CloneProfile(profile);
            safeProfile.Normalize();
            var directory = System.IO.Path.GetDirectoryName(m_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(UnmaTransferProfile));
                serializer.WriteObject(stream, safeProfile);
                stream.Flush(true);
            }

            if (File.Exists(m_path))
            {
                File.Replace(
                    temporaryPath,
                    m_path,
                    m_path + ".bak",
                    true);
            }
            else
            {
                File.Move(temporaryPath, m_path);
            }
            return true;
        }
        catch (Exception exception)
        {
            error = "UNMA transfer profile could not be saved: " +
                    exception.Message;
            Log.Warning("UNMA: " + error);
            TryDelete(temporaryPath);
            return false;
        }
    }

    private int ReadStoredSchemaVersion()
    {
        using var stream = File.OpenRead(m_path);
        var serializer = new DataContractJsonSerializer(
            typeof(ProfileSchemaVersionProbe));
        var probe = serializer.ReadObject(stream) as ProfileSchemaVersionProbe;
        return probe?.ProfileSchemaVersion ?? 0;
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
            return schemaVersion >
                   UnmaTransferProfile.CurrentProfileSchemaVersion;
        }
        catch
        {
            // A malformed current file may still be replaced atomically. The
            // previous bytes then remain available as the .bak file.
            schemaVersion = 0;
            return false;
        }
    }

    private void BlockWritesForFutureSchema(int schemaVersion)
    {
        IsWriteBlocked = true;
        WriteBlockReason =
            "UNMA transfer profile schema " + schemaVersion +
            " is newer than supported schema " +
            UnmaTransferProfile.CurrentProfileSchemaVersion +
            ". The original file was left unchanged and saving is disabled " +
            "for this session.";
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            var backupPath = m_path + ".broken-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            File.Copy(m_path, backupPath, true);
        }
        catch
        {
            // Loading still returns a safe failure result.
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
