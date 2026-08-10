using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using UnityEngine;
using UNMA.Localization;

namespace UNMA.Integration;

/// <summary>
/// Optional, reflection-only bridge to Mori's Keybind Framework.
/// UNMA keeps working with its built-in defaults when the framework is absent
/// or when an older framework does not expose the secondary key slot.
/// </summary>
internal static class KeybindFrameworkBridge
{
    internal const string ToggleWindowId = "UNMA_ToggleWindow";
    internal const string AcknowledgeAllId = "UNMA_AcknowledgeAll";
    internal const string NextUnacknowledgedAlarmId =
        "UNMA_NextUnacknowledgedAlarm";
    internal const string MuteAudioFiveMinutesId =
        "UNMA_MuteAudioFiveMinutes";

    internal const string ToggleWindowDefault = "F8";
    internal const string AcknowledgeAllDefault = "None";
    internal const string NextUnacknowledgedAlarmDefault =
        "LeftShift + F8";
    internal const string MuteAudioFiveMinutesDefault = "None";

    private const string ModId = "UNMA";
    private const string DisplayName = "UNMA";
    private const string FrameworkApiTypeName =
        "KeybindFramework.KeybindFrameworkApi";
    private const string DescriptorSeparator = "~|~";
    private const string CaptureFrameDataKey =
        "MoriPP_KeybindCapturingFrame";

    private static MethodInfo s_getCombo;
    private static MethodInfo s_getComboSecondary;
    private static bool s_initialized;

    /// <summary>
    /// Registers UNMA's bindings when Keybind Framework is present. Call once
    /// from <c>IMod.Initialize</c>, after all mod assemblies have been loaded.
    /// </summary>
    internal static void Register()
    {
        if (s_initialized)
        {
            return;
        }

        s_initialized = true;
        try
        {
            var apiType = FindApiType();
            if (apiType == null)
            {
                return;
            }

            s_getCombo = apiType.GetMethod(
                "GetCombo",
                new[] { typeof(string), typeof(string) });
            s_getComboSecondary = apiType.GetMethod(
                "GetComboSecondary",
                new[] { typeof(string), typeof(string) });
            var registerRaw = apiType.GetMethod(
                "RegisterRaw",
                new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(string[]),
                });
            if (registerRaw == null)
            {
                Log.Warning(
                    "UNMA: Keybind Framework gefunden, aber die erwartete " +
                    "RegisterRaw-API ist nicht verfuegbar.");
                return;
            }

            registerRaw.Invoke(
                null,
                new object[]
                {
                    ModId,
                    DisplayName,
                    CreateRawDescriptors(),
                });
        }
        catch (Exception exception)
        {
            Log.Warning(
                "UNMA: Keybind-Framework-Registrierung fehlgeschlagen; " +
                "verwende Standardbelegung. " +
                exception.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Returns true when either the primary or secondary combo for an action
    /// was pressed. With no framework installed, only the supplied fallback
    /// combo is evaluated.
    /// </summary>
    internal static bool IsPressed(string id, string fallbackDefault)
    {
        if (AppDomain.CurrentDomain.GetData(CaptureFrameDataKey) is int frame &&
            Time.frameCount - frame <= 1)
        {
            return false;
        }

        if (!s_initialized)
        {
            Register();
        }

        if (ComboPressed(ComboFor(id, fallbackDefault)))
        {
            return true;
        }

        return ComboPressed(SecondaryComboFor(id));
    }

    private static Type FindApiType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var apiType = assembly.GetType(FrameworkApiTypeName);
            if (apiType != null)
            {
                return apiType;
            }
        }

        return null;
    }

    private static string[] CreateRawDescriptors()
    {
        var group = UnmaText.Get(
            "keybind.group.operations",
            "Operations");
        var descriptors = new[]
        {
            new[]
            {
                ToggleWindowId,
                UnmaText.Get(
                    "keybind.toggle_window.label",
                    "Open or close UNMA"),
                "Discrete",
                ToggleWindowDefault,
                "",
                group,
                UnmaText.Get(
                    "keybind.toggle_window.tooltip",
                    "Toggles the main UNMA window."),
            },
            new[]
            {
                AcknowledgeAllId,
                UnmaText.Get(
                    "keybind.acknowledge_all.label",
                    "Acknowledge all alarms"),
                "Discrete",
                AcknowledgeAllDefault,
                "",
                group,
                UnmaText.Get(
                    "keybind.acknowledge_all.tooltip",
                    "Acknowledges every currently unacknowledged alarm."),
            },
            new[]
            {
                NextUnacknowledgedAlarmId,
                UnmaText.Get(
                    "keybind.next_unacknowledged.label",
                    "Next unacknowledged alarm"),
                "Discrete",
                NextUnacknowledgedAlarmDefault,
                "",
                group,
                UnmaText.Get(
                    "keybind.next_unacknowledged.tooltip",
                    "Selects the next unacknowledged alarm."),
            },
            new[]
            {
                MuteAudioFiveMinutesId,
                UnmaText.Get(
                    "keybind.mute_audio_five_minutes.label",
                    "Mute alarm audio for 5 minutes"),
                "Discrete",
                MuteAudioFiveMinutesDefault,
                "",
                group,
                UnmaText.Get(
                    "keybind.mute_audio_five_minutes.tooltip",
                    "Silences UNMA alarm audio for five real-time minutes."),
            },
        };

        var raw = new List<string>(descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            // Field 7 is the optional default for the Secondary slot. UNMA
            // leaves it empty so players can choose their own second combo.
            raw.Add(string.Join(
                DescriptorSeparator,
                new[]
                {
                    descriptor[0],
                    descriptor[1],
                    descriptor[2],
                    descriptor[3],
                    descriptor[4],
                    descriptor[5],
                    "",
                    descriptor[6],
                }));
        }

        return raw.ToArray();
    }

    private static string ComboFor(string id, string fallbackDefault)
    {
        try
        {
            if (s_getCombo != null)
            {
                var combo = s_getCombo.Invoke(
                    null,
                    new object[] { ModId, id }) as string;
                if (!string.IsNullOrEmpty(combo))
                {
                    return combo;
                }
            }
        }
        catch
        {
            // A framework failure must never disable UNMA's fallback key.
        }

        return fallbackDefault;
    }

    private static string SecondaryComboFor(string id)
    {
        try
        {
            if (s_getComboSecondary != null)
            {
                var combo = s_getComboSecondary.Invoke(
                    null,
                    new object[] { ModId, id }) as string;
                if (!string.IsNullOrEmpty(combo))
                {
                    return combo;
                }
            }
        }
        catch
        {
            // Framework versions before 2.0.2 do not expose this slot.
        }

        return "None";
    }

    private static bool ComboPressed(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo) ||
            string.Equals(combo.Trim(), "None", StringComparison.Ordinal))
        {
            return false;
        }

        var mainKey = KeyCode.None;
        var modifiers = new List<KeyCode>();
        foreach (var token in combo.Split('+'))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0 ||
                string.Equals(trimmed, "None", StringComparison.Ordinal))
            {
                continue;
            }
            if (!Enum.TryParse(trimmed, out KeyCode key))
            {
                continue;
            }

            if (IsModifierKey(key))
            {
                modifiers.Add(key);
            }
            else
            {
                mainKey = key;
            }
        }

        return mainKey != KeyCode.None &&
               ModifiersHeldExactly(modifiers) &&
               Input.GetKeyDown(mainKey);
    }

    private static bool IsModifierKey(KeyCode key)
    {
        return key == KeyCode.LeftControl ||
               key == KeyCode.RightControl ||
               key == KeyCode.LeftShift ||
               key == KeyCode.RightShift ||
               key == KeyCode.LeftAlt ||
               key == KeyCode.RightAlt ||
               key == KeyCode.AltGr;
    }

    private static bool ModifiersHeldExactly(List<KeyCode> modifiers)
    {
        var wantLeftControl = false;
        var wantRightControl = false;
        var wantLeftShift = false;
        var wantRightShift = false;
        var wantLeftAlt = false;
        var wantRightAlt = false;
        foreach (var modifier in modifiers)
        {
            switch (modifier)
            {
                case KeyCode.AltGr:
                    wantRightAlt = true;
                    break;
                case KeyCode.LeftControl:
                    wantLeftControl = true;
                    break;
                case KeyCode.RightControl:
                    wantRightControl = true;
                    break;
                case KeyCode.LeftShift:
                    wantLeftShift = true;
                    break;
                case KeyCode.RightShift:
                    wantRightShift = true;
                    break;
                case KeyCode.LeftAlt:
                    wantLeftAlt = true;
                    break;
                case KeyCode.RightAlt:
                    wantRightAlt = true;
                    break;
            }
        }

        return LeftControlMatches(wantLeftControl) &&
               wantRightControl == Input.GetKey(KeyCode.RightControl) &&
               wantLeftShift == Input.GetKey(KeyCode.LeftShift) &&
               wantRightShift == Input.GetKey(KeyCode.RightShift) &&
               wantLeftAlt == Input.GetKey(KeyCode.LeftAlt) &&
               wantRightAlt == RightAltDown();
    }

    private static bool LeftControlMatches(bool required)
    {
        var isDown = Input.GetKey(KeyCode.LeftControl);
        if (isDown && RightAltDown())
        {
            // Windows emits a synthetic left-Control press with AltGr. It is
            // therefore a don't-care while right Alt is held.
            return true;
        }

        return required == isDown;
    }

    private static bool RightAltDown()
    {
        if (Input.GetKey(KeyCode.RightAlt))
        {
            return true;
        }

        return AltGrDown() && !Input.GetKey(KeyCode.LeftAlt);
    }

    private static bool AltGrDown()
    {
        try
        {
            return Input.GetKey(KeyCode.AltGr);
        }
        catch
        {
            return false;
        }
    }
}
