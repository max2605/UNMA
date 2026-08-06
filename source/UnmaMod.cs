using System;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Logging;
using Mafi.Unity.Audio;
using Mafi.Unity.Ui;
using UnityEngine;
using UNMA.Runtime;
using UNMA.Ui;

namespace UNMA;

public sealed class UnmaMod : IMod
{
    private UnmaRuntime m_runtime;
    private UnmaOverlayController m_overlay;

    public ModManifest Manifest { get; }

    public bool IsUiOnly => true;

    [Obsolete("Use JsonConfig instead.")]
    public Option<IConfig> ModConfig => Option<IConfig>.None;

    public ModJsonConfig JsonConfig { get; }

    public UnmaMod(ModManifest manifest)
    {
        Manifest = manifest;
        JsonConfig = new ModJsonConfig(this);
        Log.Info("UNMA: constructed");
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
    }

    public void RegisterDependencies(
        DependencyResolverBuilder depBuilder,
        ProtosDb protosDb,
        bool gameWasLoaded)
    {
        EntityMetricCatalog.ConfigureProducts(protosDb);
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        var gameId = resolver.Resolve<IGameIdProvider>().GameId;
        var store = new UnmaStateStore(
            Manifest.RootDirectoryPath,
            gameId);
        m_runtime = new UnmaRuntime(
            resolver.Resolve<INotificationsManager>(),
            resolver.Resolve<IEntitiesManager>(),
            resolver.Resolve<IWorkersManager>(),
            resolver.Resolve<SettlementsManager>(),
            resolver.Resolve<PopsHealthManager>(),
            resolver.Resolve<ISimLoopEvents>(),
            store,
            ReadSettings());
        m_runtime.Initialize();

        m_overlay = UnmaOverlayController.Create(
            m_runtime,
            resolver.Resolve<InspectorsManager>(),
            resolver.Resolve<AudioDb>(),
            Manifest.RootDirectoryPath);
        JsonConfig.OnValueChanged += OnConfigValueChanged;

        Log.Info(
            $"UNMA: initialized; loadedSave={gameWasLoaded}, " +
            $"gameId={gameId}, " +
            $"panels={m_runtime.Configuration.Panels.Count}, " +
            $"rules={m_runtime.Configuration.Rules.Count}");
    }

    public void MigrateJsonConfig(
        VersionSlim savedVersion,
        Dict<string, object> savedValues)
    {
    }

    public void Dispose()
    {
        JsonConfig.OnValueChanged -= OnConfigValueChanged;
        m_runtime?.Dispose();
        m_runtime = null;

        if (m_overlay != null)
        {
            m_overlay.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(m_overlay.gameObject);
            m_overlay = null;
        }
    }

    private void OnConfigValueChanged(string _)
    {
        m_overlay?.ApplySettings(ReadSettings());
    }

    private UnmaSettings ReadSettings()
    {
        var warning = Math.Max(
            1,
            Math.Min(
                100,
                JsonConfig.GetInt("healthWarningPercent", 65)));
        var critical = Math.Max(
            1,
            Math.Min(
                warning,
                JsonConfig.GetInt("healthCriticalPercent", 45)));
        var emergency = Math.Max(
            1,
            Math.Min(
                critical,
                JsonConfig.GetInt("healthEmergencyPercent", 25)));
        return new UnmaSettings
        {
            ShowOnGameStart = JsonConfig.GetBool("showOnGameStart", true),
            EnableAudio = JsonConfig.GetBool("enableAudio", true),
            AudioVolumePercent = JsonConfig.GetInt(
                "audioVolumePercent",
                65),
            PollIntervalMs = JsonConfig.GetInt("pollIntervalMs", 500),
            EnableSystemAlarms = JsonConfig.GetBool(
                "enableSystemAlarms",
                true),
            HealthWarningPercent = warning,
            HealthCriticalPercent = critical,
            HealthEmergencyPercent = emergency,
        };
    }
}
