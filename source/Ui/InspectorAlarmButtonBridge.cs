using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mafi;
using Mafi.Collections;
using Mafi.Localization;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;

namespace UNMA.Ui;

/// <summary>
/// Adds a UNMA alarm button to every inspector instance created by the game.
/// The bridge deliberately treats the game's private inspector list as an
/// optional API so a future game update cannot prevent UNMA from starting.
/// </summary>
public sealed class InspectorAlarmButtonBridge : IDisposable
{
    private sealed class InspectorReferenceComparer :
        IEqualityComparer<IEntityInspector>
    {
        public static readonly InspectorReferenceComparer Instance = new();

        public bool Equals(
            IEntityInspector left,
            IEntityInspector right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(IEntityInspector inspector)
        {
            return RuntimeHelpers.GetHashCode(inspector);
        }
    }

    private const string BellIconPath =
        "Assets/Unity/UserInterface/General/Bell128.png";
    private const float ScanIntervalSeconds = 0.2f;

    private readonly InspectorsManager m_inspectorsManager;
    private readonly Action<IEntityInspector> m_onButtonClicked;
    private readonly FieldInfo m_inspectorInstancesField;
    private readonly Dictionary<IEntityInspector, ButtonIcon> m_buttons =
        new(InspectorReferenceComparer.Instance);
    private readonly HashSet<Type> m_incompatibleInspectorTypes = new();

    private float m_nextScanTime;
    private bool m_isDisposed;
    private bool m_isCompatible;
    private bool m_updateFailureLogged;

    public InspectorAlarmButtonBridge(
        InspectorsManager inspectorsManager,
        Action<IEntityInspector> onButtonClicked)
    {
        m_inspectorsManager = inspectorsManager;
        m_onButtonClicked = onButtonClicked;
        m_inspectorInstancesField = FindInspectorInstancesField();
        m_isCompatible = m_inspectorsManager != null &&
                         m_onButtonClicked != null &&
                         m_inspectorInstancesField != null;

        if (!m_isCompatible)
        {
            Log.Warning(
                "UNMA: Inspector-Alarmknopf deaktiviert; " +
                "die Inspector-API ist nicht kompatibel.");
        }
    }

    /// <summary>
    /// Scans periodically for inspector instances that were created on demand.
    /// Must be called from Unity's main thread.
    /// </summary>
    public void Update()
    {
        if (m_isDisposed || !m_isCompatible)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (now < m_nextScanTime)
        {
            return;
        }
        m_nextScanTime = now + ScanIntervalSeconds;

        try
        {
            var rawInstances = m_inspectorInstancesField.GetValue(
                m_inspectorsManager);
            if (rawInstances is not LystStruct<IEntityInspector> instances)
            {
                DisableAfterFailure(
                    "Inspector-Liste hat einen unerwarteten Typ.",
                    null);
                return;
            }

            for (var index = 0; index < instances.Count; index++)
            {
                TryAddButton(instances[index]);
            }
        }
        catch (Exception exception)
        {
            DisableAfterFailure(
                "Inspector-Liste konnte nicht gelesen werden.",
                exception);
        }
    }

    /// <summary>
    /// Removes all injected UI components while keeping the bridge reusable.
    /// </summary>
    public void RemoveFromHierarchy()
    {
        foreach (var button in m_buttons.Values)
        {
            try
            {
                button.RemoveFromHierarchy();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "UNMA: Inspector-Alarmknopf konnte nicht entfernt " +
                    "werden: " + exception.Message);
            }
        }
        m_buttons.Clear();
    }

    public void Dispose()
    {
        if (m_isDisposed)
        {
            return;
        }

        RemoveFromHierarchy();
        m_isDisposed = true;
    }

    private void TryAddButton(IEntityInspector inspector)
    {
        if (inspector == null ||
            m_buttons.ContainsKey(inspector) ||
            m_incompatibleInspectorTypes.Contains(inspector.GetType()))
        {
            return;
        }

        try
        {
            var topRightButtonsField = inspector.GetType().GetField(
                "TopRightButtons",
                BindingFlags.Instance | BindingFlags.Public);
            if (topRightButtonsField?.GetValue(inspector) is not
                Row topRightButtons)
            {
                MarkInspectorTypeIncompatible(
                    inspector.GetType(),
                    "TopRightButtons fehlt.",
                    null);
                return;
            }

            var capturedInspector = inspector;
            var button = new ButtonIcon(
                    Button.Primary,
                    BellIconPath,
                    () => m_onButtonClicked(capturedInspector))
                .Tooltip(new LocStrFormatted(
                    "UNMA: Objekt hinzufügen und Meldeschlitz auswählen"));
            var headerPanelField = inspector.GetType().GetField(
                "HeaderPanel",
                BindingFlags.Instance | BindingFlags.Public);
            var headerIsVisible =
                headerPanelField?.GetValue(inspector) is not Panel headerPanel ||
                headerPanel.IsVisible();
            if (headerIsVisible)
            {
                topRightButtons.Add(
                    button.CompactForInspectorHeader(toggle: false));
            }
            else if (inspector is Window window)
            {
                window.AttachComponentToTitleLeft(button.Compact());
            }
            else
            {
                MarkInspectorTypeIncompatible(
                    inspector.GetType(),
                    "Inspector-Kopfzeile ist ausgeblendet.",
                    null);
                button.RemoveFromHierarchy();
                return;
            }
            m_buttons.Add(inspector, button);
        }
        catch (Exception exception)
        {
            MarkInspectorTypeIncompatible(
                inspector.GetType(),
                "Alarmknopf konnte nicht eingefügt werden.",
                exception);
        }
    }

    private void MarkInspectorTypeIncompatible(
        Type inspectorType,
        string message,
        Exception exception)
    {
        if (!m_incompatibleInspectorTypes.Add(inspectorType))
        {
            return;
        }

        Log.Warning(
            "UNMA: Kein Inspector-Alarmknopf für " +
            inspectorType.FullName + ": " + message +
            FormatException(exception));
    }

    private void DisableAfterFailure(
        string message,
        Exception exception)
    {
        m_isCompatible = false;
        if (m_updateFailureLogged)
        {
            return;
        }

        m_updateFailureLogged = true;
        Log.Warning(
            "UNMA: Inspector-Alarmknopf deaktiviert: " + message +
            FormatException(exception));
    }

    private static FieldInfo FindInspectorInstancesField()
    {
        var expectedType = typeof(LystStruct<IEntityInspector>);
        FieldInfo result = null;

        foreach (var field in typeof(InspectorsManager).GetFields(
                     BindingFlags.Instance |
                     BindingFlags.Public |
                     BindingFlags.NonPublic))
        {
            if (field.FieldType != expectedType)
            {
                continue;
            }

            if (result != null)
            {
                Log.Warning(
                    "UNMA: Mehrere kompatible Inspector-Listen gefunden.");
                return null;
            }
            result = field;
        }

        return result;
    }

    private static string FormatException(Exception exception)
    {
        return exception == null
            ? ""
            : " " + exception.GetType().Name + ": " + exception.Message;
    }
}
