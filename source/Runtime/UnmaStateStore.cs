using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using Mafi;
using UNMA.Domain;

namespace UNMA.Runtime;

public sealed class UnmaStateStore
{
    private readonly string m_path;

    public string Path => m_path;

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
