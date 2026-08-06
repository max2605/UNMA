using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Unity.Audio;
using UnityEngine;
using UnityEngine.Networking;
using UNMA.Domain;

namespace UNMA.Audio;

public sealed class SoundOption
{
    public string Id { get; }
    public string Label { get; }

    public SoundOption(string id, string label)
    {
        Id = id;
        Label = label;
    }
}

public sealed class UnmaAudioController : MonoBehaviour
{
    private const int SampleRate = 44100;

    private static readonly SoundOption[] s_builtinOptions =
    {
        new("auto", "Automatisch nach Stufe"),
        new("none", "Kein Ton"),
        new("bell", "Klingel"),
        new("horn", "Industriehorn"),
        new("siren", "E51-Auf/Ab-Sirene"),
        new("sine", "Oszillator · Sinus"),
        new("square", "Oszillator · Rechteck"),
        new("saw", "Oszillator · Sägezahn"),
        new("triangle", "Oszillator · Dreieck"),
        new("pulse", "Oszillator · Impuls"),
    };

    private readonly Dictionary<string, AudioClip> m_clips =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_failedCustomSounds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SoundOption> m_soundOptions = new();

    private AudioSource m_source;
    private string m_soundsDirectory = "";
    private string m_requestedSoundId = "";
    private string m_playingSoundId = "";
    private string m_loadingSoundId = "";
    private Coroutine m_loadingCoroutine;

    public string SoundsDirectory => m_soundsDirectory;

    public void Configure(string modRoot, AudioDb audioDb)
    {
        m_soundsDirectory = Path.Combine(modRoot, "Sounds");
        Directory.CreateDirectory(m_soundsDirectory);
        m_source = gameObject.AddComponent<AudioSource>();
        m_source.playOnAwake = false;
        m_source.loop = true;
        m_source.spatialBlend = 0f;
        m_source.ignoreListenerPause = true;
        if (audioDb != null)
        {
            m_source.outputAudioMixerGroup =
                audioDb.GetChannel(AudioChannel.UserInterface);
        }

        CreateBuiltInClips();
        RefreshSoundOptions();
    }

    public IReadOnlyList<SoundOption> GetSoundOptions()
    {
        return m_soundOptions;
    }

    public void RefreshSoundOptions()
    {
        if (m_loadingCoroutine != null)
        {
            StopCoroutine(m_loadingCoroutine);
            m_loadingCoroutine = null;
            m_loadingSoundId = "";
        }

        if (m_playingSoundId.StartsWith(
                "file:",
                StringComparison.OrdinalIgnoreCase))
        {
            StopPlayback();
        }

        foreach (var soundId in m_clips.Keys
                     .Where(id => id.StartsWith(
                         "file:",
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            var clip = m_clips[soundId];
            m_clips.Remove(soundId);
            if (clip != null)
            {
                Destroy(clip);
            }
        }

        m_soundOptions.Clear();
        m_soundOptions.AddRange(s_builtinOptions);
        m_failedCustomSounds.Clear();
        if (!Directory.Exists(m_soundsDirectory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(m_soundsDirectory)
                         .Where(path =>
                             string.Equals(
                                 Path.GetExtension(path),
                                 ".wav",
                                 StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(
                                 Path.GetExtension(path),
                                 ".ogg",
                                 StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(path);
                m_soundOptions.Add(new SoundOption(
                    "file:" + fileName,
                    "Datei · " + fileName));
            }
        }
        catch (Exception exception)
        {
            Log.Warning(
                "UNMA: Sound-Verzeichnis konnte nicht gelesen werden: " +
                exception.Message);
        }
    }

    public void UpdateAlarm(AlarmView alarm, int volumePercent)
    {
        m_source.volume = Mathf.Clamp01(volumePercent / 100f);
        if (alarm == null)
        {
            StopAlarm();
            return;
        }

        var soundId = ResolveSoundId(alarm);
        if (soundId == "none")
        {
            StopAlarm();
            return;
        }

        m_requestedSoundId = soundId;
        if (string.Equals(
                m_playingSoundId,
                soundId,
                StringComparison.OrdinalIgnoreCase) &&
            m_source.isPlaying)
        {
            return;
        }

        if (m_clips.TryGetValue(soundId, out var clip))
        {
            Play(soundId, clip);
            return;
        }

        if (!soundId.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            m_failedCustomSounds.Contains(soundId))
        {
            StopAlarm();
            return;
        }

        if (m_loadingCoroutine != null)
        {
            if (string.Equals(
                    m_loadingSoundId,
                    soundId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            StopCoroutine(m_loadingCoroutine);
            m_loadingCoroutine = null;
            m_loadingSoundId = "";
        }

        if (!TryResolveCustomSoundPath(soundId, out var fullPath))
        {
            m_failedCustomSounds.Add(soundId);
            StopAlarm();
            return;
        }

        StopPlayback();
        m_loadingSoundId = soundId;
        m_loadingCoroutine = StartCoroutine(
            LoadCustomSound(soundId, fullPath));
    }

    public void StopAlarm()
    {
        m_requestedSoundId = "";
        if (m_loadingCoroutine != null)
        {
            StopCoroutine(m_loadingCoroutine);
            m_loadingCoroutine = null;
        }
        m_loadingSoundId = "";
        StopPlayback();
    }

    private void StopPlayback()
    {
        m_playingSoundId = "";
        if (m_source != null)
        {
            m_source.Stop();
            m_source.clip = null;
        }
    }

    private bool TryResolveCustomSoundPath(
        string soundId,
        out string fullPath)
    {
        var fileName = soundId.Substring("file:".Length);
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(
                m_soundsDirectory,
                fileName));
            var expectedRoot = Path.GetFullPath(m_soundsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return fullPath.StartsWith(
                       expectedRoot,
                       StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(fullPath);
        }
        catch
        {
            fullPath = "";
            return false;
        }
    }

    private IEnumerator LoadCustomSound(string soundId, string fullPath)
    {
        try
        {
            var fileName = Path.GetFileName(fullPath);
            var extension = Path.GetExtension(fullPath);
            var audioType = string.Equals(
                extension,
                ".ogg",
                StringComparison.OrdinalIgnoreCase)
                ? AudioType.OGGVORBIS
                : AudioType.WAV;
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            Exception setupException = null;
            try
            {
                request = UnityWebRequestMultimedia.GetAudioClip(
                    new Uri(fullPath).AbsoluteUri,
                    audioType);
                operation = request.SendWebRequest();
            }
            catch (Exception exception)
            {
                setupException = exception;
            }

            if (setupException != null)
            {
                RegisterCustomSoundFailure(
                    soundId,
                    fileName,
                    setupException.Message);
                request?.Dispose();
                yield break;
            }

            using (request)
            {
                yield return operation;

                try
                {
                    if (request.result !=
                        UnityWebRequest.Result.Success)
                    {
                        RegisterCustomSoundFailure(
                            soundId,
                            fileName,
                            request.error);
                    }
                    else
                    {
                        var clip = DownloadHandlerAudioClip.GetContent(request);
                        if (clip == null)
                        {
                            RegisterCustomSoundFailure(
                                soundId,
                                fileName,
                                "kein AudioClip erzeugt");
                        }
                        else
                        {
                            clip.name = "UNMA " + fileName;
                            m_clips[soundId] = clip;
                            if (string.Equals(
                                    m_requestedSoundId,
                                    soundId,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                Play(soundId, clip);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    RegisterCustomSoundFailure(
                        soundId,
                        fileName,
                        exception.Message);
                }
            }
        }
        finally
        {
            CompleteLoading(soundId);
        }
    }

    private void RegisterCustomSoundFailure(
        string soundId,
        string fileName,
        string error)
    {
        m_failedCustomSounds.Add(soundId);
        Log.Warning(
            $"UNMA: Audiodatei '{fileName}' konnte nicht geladen " +
            $"werden: {error}");
    }

    private void CompleteLoading(string soundId)
    {
        if (!string.Equals(
                m_loadingSoundId,
                soundId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        m_loadingCoroutine = null;
        m_loadingSoundId = "";
    }

    private void Play(string soundId, AudioClip clip)
    {
        m_source.Stop();
        m_source.clip = clip;
        m_source.loop = true;
        m_source.Play();
        m_playingSoundId = soundId;
    }

    private static string ResolveSoundId(AlarmView alarm)
    {
        if (!string.IsNullOrWhiteSpace(alarm.SoundId) &&
            !string.Equals(
                alarm.SoundId,
                "auto",
                StringComparison.OrdinalIgnoreCase))
        {
            return alarm.SoundId;
        }

        return alarm.Severity switch
        {
            AlarmSeverity.Emergency => "siren",
            AlarmSeverity.Critical => "horn",
            AlarmSeverity.Warning => "bell",
            _ => "sine",
        };
    }

    private void CreateBuiltInClips()
    {
        AddClip("bell", 2.4f, BellSample);
        AddClip("horn", 3.2f, HornSample);
        AddClip("siren", 8f, SirenSample);
        AddClip("sine", 1.4f,
            (time, _) => GatedOscillator(time, 720f, Waveform.Sine));
        AddClip("square", 1.4f,
            (time, _) => GatedOscillator(time, 620f, Waveform.Square));
        AddClip("saw", 1.4f,
            (time, _) => GatedOscillator(time, 540f, Waveform.Saw));
        AddClip("triangle", 1.4f,
            (time, _) => GatedOscillator(time, 680f, Waveform.Triangle));
        AddClip("pulse", 1.4f,
            (time, _) => GatedOscillator(time, 760f, Waveform.Pulse));
    }

    private void AddClip(
        string id,
        float durationSeconds,
        Func<float, int, float> sampler)
    {
        var sampleCount = Mathf.CeilToInt(durationSeconds * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var time = index / (float)SampleRate;
            samples[index] = Mathf.Clamp(
                sampler(time, index),
                -0.92f,
                0.92f);
        }

        var clip = AudioClip.Create(
            "UNMA " + id,
            sampleCount,
            1,
            SampleRate,
            false);
        // Unity 6.3 exposes an additional ReadOnlySpan overload whose
        // netstandard 2.1 signature cannot be consumed directly by a net48
        // mod compiler. Resolve the long-standing float[] overload explicitly.
        var setData = typeof(AudioClip).GetMethod(
            "SetData",
            new[] { typeof(float[]), typeof(int) });
        if (setData == null || !(bool)setData.Invoke(
                clip,
                new object[] { samples, 0 }))
        {
            Destroy(clip);
            throw new InvalidOperationException(
                "Unity rejected generated UNMA audio data for " + id);
        }
        m_clips[id] = clip;
    }

    private static float BellSample(float time, int _)
    {
        var local = time % 0.78f;
        if (local > 0.52f)
        {
            return 0f;
        }
        var envelope = Mathf.Exp(-7.5f * local) *
                       SmoothEdge(local, 0.015f, 0.5f);
        return envelope *
               (0.42f * Mathf.Sin(2f * Mathf.PI * 880f * local) +
                0.25f * Mathf.Sin(2f * Mathf.PI * 1320f * local) +
                0.12f * Mathf.Sin(2f * Mathf.PI * 1760f * local));
    }

    private static float HornSample(float time, int _)
    {
        var active = time < 2.55f;
        if (!active)
        {
            return 0f;
        }
        var envelope = SmoothEdge(time, 0.18f, 2.5f);
        var wobble = 1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 2.3f * time);
        var baseFrequency = 154f * wobble;
        return envelope *
               (0.43f * Mathf.Sin(2f * Mathf.PI * baseFrequency * time) +
                0.27f * Mathf.Sin(2f * Mathf.PI * baseFrequency * 2f * time) +
                0.13f * Mathf.Sin(2f * Mathf.PI * baseFrequency * 3f * time));
    }

    private static float SirenSample(float time, int _)
    {
        const float cycleSeconds = 8f;
        var phase = (time % cycleSeconds) / cycleSeconds;
        var sweep = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phase);
        var frequency = Mathf.Lerp(390f, 910f, sweep);
        var integratedPhase = 2f * Mathf.PI *
            (390f * time +
             260f * time -
             260f * cycleSeconds /
             (2f * Mathf.PI) *
             Mathf.Sin(2f * Mathf.PI * time / cycleSeconds));
        var signal =
            0.52f * Mathf.Sin(integratedPhase) +
            0.17f * Mathf.Sin(2f * integratedPhase) +
            0.08f * Mathf.Sin(3f * integratedPhase);
        return signal * (0.82f + 0.18f * frequency / 910f);
    }

    private static float GatedOscillator(
        float time,
        float frequency,
        Waveform waveform)
    {
        var local = time % 0.7f;
        if (local >= 0.45f)
        {
            return 0f;
        }
        var phase = time * frequency;
        var unit = phase - Mathf.Floor(phase);
        var value = waveform switch
        {
            Waveform.Sine => Mathf.Sin(2f * Mathf.PI * unit),
            Waveform.Square => unit < 0.5f ? 1f : -1f,
            Waveform.Saw => 2f * unit - 1f,
            Waveform.Triangle => 1f - 4f * Mathf.Abs(unit - 0.5f),
            Waveform.Pulse => unit < 0.18f ? 1f : -0.25f,
            _ => 0f,
        };
        return 0.32f * value * SmoothEdge(local, 0.012f, 0.44f);
    }

    private static float SmoothEdge(float time, float attackEnd, float end)
    {
        var attack = Mathf.Clamp01(time / Math.Max(0.001f, attackEnd));
        var release = Mathf.Clamp01((end - time) / 0.035f);
        return attack * release;
    }

    private void OnDestroy()
    {
        StopAlarm();
        foreach (var clip in m_clips.Values)
        {
            if (clip != null)
            {
                Destroy(clip);
            }
        }
        m_clips.Clear();
    }

    private enum Waveform
    {
        Sine,
        Square,
        Saw,
        Triangle,
        Pulse,
    }
}
