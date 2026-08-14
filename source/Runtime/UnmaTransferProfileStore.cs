using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Mafi;
using UNMA.Domain;

namespace UNMA.Runtime;

public sealed class UnmaTransferProfileStore
{
    public const string DefaultProfileFileName = "default.json";

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

    public static string GetDefaultProfilePath(
        string applicationDataRoot = null)
    {
        applicationDataRoot = ResolveDataRoot(
            applicationDataRoot,
            Environment.SpecialFolder.ApplicationData,
            "Roaming");
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(
            applicationDataRoot,
            "Captain of Industry",
            "UNMA",
            "profiles",
            DefaultProfileFileName));
    }

    public static string GetLegacyDefaultProfilePath(
        string localApplicationDataRoot = null)
    {
        localApplicationDataRoot = ResolveDataRoot(
            localApplicationDataRoot,
            Environment.SpecialFolder.LocalApplicationData,
            "Local");
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(
            localApplicationDataRoot,
            "UNMA",
            "profiles",
            DefaultProfileFileName));
    }

    /// <summary>
    /// Resolves the startup file. An explicit configured path wins. Otherwise
    /// the roaming Captain of Industry directory is used and the old local
    /// profile is copied there once, atomically and without deleting it.
    /// </summary>
    public static string ResolveStartupProfilePath(
        string configuredPath,
        out bool migratedLegacyProfile,
        out string warning)
    {
        migratedLegacyProfile = false;
        warning = "";
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(
                    configuredPath.Trim());
                var endsWithSeparator = expanded.EndsWith(
                        System.IO.Path.DirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal) ||
                    expanded.EndsWith(
                        System.IO.Path.AltDirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal);
                var resolved = System.IO.Path.GetFullPath(expanded);
                if (endsWithSeparator || Directory.Exists(resolved) ||
                    !System.IO.Path.HasExtension(resolved))
                {
                    resolved = System.IO.Path.Combine(
                        resolved,
                        DefaultProfileFileName);
                }
                return resolved;
            }
            catch (Exception exception)
            {
                warning = "Configured transfer profile path is invalid; " +
                    "the default path will be used: " + exception.Message;
            }
        }

        return ResolveDefaultProfilePath(
            GetDefaultProfilePath(),
            GetLegacyDefaultProfilePath(),
            out migratedLegacyProfile,
            ref warning);
    }

    internal static string ResolveDefaultProfilePath(
        string destinationPath,
        string legacyPath,
        out bool migratedLegacyProfile,
        ref string warning)
    {
        destinationPath = System.IO.Path.GetFullPath(destinationPath);
        legacyPath = System.IO.Path.GetFullPath(legacyPath);
        migratedLegacyProfile = false;
        if (string.Equals(
                destinationPath,
                legacyPath,
                StringComparison.OrdinalIgnoreCase) ||
            File.Exists(destinationPath) || !File.Exists(legacyPath))
        {
            return destinationPath;
        }

        if (TryMigrateLegacyProfile(
                legacyPath,
                destinationPath,
                out migratedLegacyProfile,
                out var migrationError))
        {
            return destinationPath;
        }

        warning = string.IsNullOrWhiteSpace(warning)
            ? migrationError
            : warning + " " + migrationError;
        // A migration failure must not make the existing profile disappear.
        // Continue using the legacy file for this session; the next launch can
        // try the non-destructive copy again.
        return legacyPath;
    }

    public static bool TryMigrateLegacyProfile(
        string legacyPath,
        string destinationPath,
        out bool migrated,
        out string error)
    {
        migrated = false;
        error = "";
        string temporaryPath = null;
        try
        {
            legacyPath = System.IO.Path.GetFullPath(legacyPath);
            destinationPath = System.IO.Path.GetFullPath(destinationPath);
            if (string.Equals(
                    legacyPath,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase) ||
                File.Exists(destinationPath) || !File.Exists(legacyPath))
            {
                return true;
            }

            var directory = System.IO.Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            temporaryPath = destinationPath + ".migrate-" +
                Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(legacyPath, temporaryPath, false);
            try
            {
                File.Move(temporaryPath, destinationPath);
                migrated = true;
                return true;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another instance completed the same one-time migration.
                TryDelete(temporaryPath);
                return true;
            }
        }
        catch (Exception exception)
        {
            error = "UNMA could not copy the legacy transfer profile from '" +
                legacyPath + "' to '" + destinationPath + "': " +
                exception.Message;
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static string ResolveDataRoot(
        string configuredRoot,
        Environment.SpecialFolder specialFolder,
        string appDataLeaf)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Environment.GetFolderPath(specialFolder)
            : configuredRoot.Trim();
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return System.IO.Path.Combine(
                userProfile,
                "AppData",
                appDataLeaf);
        }

        throw new InvalidOperationException(
            "The Windows application-data directory is unavailable.");
    }

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
