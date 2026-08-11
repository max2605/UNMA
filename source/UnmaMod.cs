using System;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.Products;
using Mafi.Core.Maintenance;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Logging;
using Mafi.Unity;
using Mafi.Unity.Audio;
using Mafi.Unity.Camera;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit;
using UnityEngine;
using UNMA.Localization;
using UNMA.Extensions;
using UNMA.Integration;
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
        UnmaText.Initialize(manifest.RootDirectoryPath);
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
        EntityVanillaNotificationCatalog.Configure(protosDb);
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        KeybindFrameworkBridge.Register();

        var gameId = resolver.Resolve<IGameIdProvider>().GameId;
        var store = new UnmaStateStore(
            Manifest.RootDirectoryPath,
            gameId);
        m_runtime = new UnmaRuntime(
            resolver.Resolve<INotificationsManager>(),
            resolver.Resolve<IEntitiesManager>(),
            resolver.Resolve<TransportsManager>(),
            resolver.Resolve<IWorkersManager>(),
            resolver.Resolve<SettlementsManager>(),
            resolver.Resolve<PopsHealthManager>(),
            resolver.Resolve<IProductsManager>(),
            resolver.Resolve<MaintenanceManager>(),
            resolver.Resolve<ICalendar>(),
            resolver.Resolve<ISimLoopEvents>(),
            store,
            ReadSettings(),
            DiscoverActiveExternalProviders());
        m_runtime.Initialize();

        m_overlay = UnmaOverlayController.Create(
            m_runtime,
            resolver.Resolve<InspectorsManager>(),
            resolver.Resolve<CameraController>(),
            resolver.Resolve<IUnityInputMgr>(),
            resolver.Resolve<AudioDb>(),
            resolver.Resolve<UiRoot>(),
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
        m_overlay?.DisposeUi();
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
        };
    }

    private ExternalProviderDescriptor[] DiscoverActiveExternalProviders()
    {
        try
        {
            return ModsLoader.LoadedAndFailedMods
                .AsEnumerable()
                .Where(item => item.LoadError.IsNone)
                .Select(item => item.Manifest)
                .Where(manifest => manifest != null)
                .Where(manifest => !IsBuiltInProvider(manifest.Id))
                .GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(manifest => new ExternalProviderDescriptor(
                    manifest.Id,
                    manifest.RootDirectoryPath))
                .ToArray();
        }
        catch (Exception exception)
        {
            Log.Warning(
                "UNMA: aktive Mod-Wurzeln konnten nicht vollständig " +
                "ermittelt werden: " + exception.Message);
            return new[]
            {
                new ExternalProviderDescriptor(
                    Manifest.Id,
                    Manifest.RootDirectoryPath),
            };
        }
    }

    private static bool IsBuiltInProvider(string id) =>
        string.Equals(id, "COI-Core", StringComparison.Ordinal) ||
        string.Equals(id, "COI-CoreData", StringComparison.Ordinal) ||
        string.Equals(id, "COI-CoreUnity", StringComparison.Ordinal);
}
