using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Unity.Audio;
using UnityEngine;
using UNMA.Localization;
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

    private static readonly BuiltInSoundOptionDefinition[] s_builtinOptions =
    {
        new("auto", "sounds.builtin.auto", "Automatic by severity"),
        new("none", "sounds.builtin.none", "No sound"),
        new("bell", "sounds.builtin.bell", "Mechanical bell"),
        new("horn", "sounds.builtin.horn", "Industrial horn"),
        new("siren", "sounds.builtin.siren", "E57 motor siren · 2 s up / 2 s down"),
        new("sine", "sounds.builtin.oscillator.sine", "Oscillator · Sine"),
        new("square", "sounds.builtin.oscillator.square", "Oscillator · Square"),
        new("saw", "sounds.builtin.oscillator.sawtooth", "Oscillator · Sawtooth"),
        new("triangle", "sounds.builtin.oscillator.triangle", "Oscillator · Triangle"),
        new("pulse", "sounds.builtin.oscillator.pulse", "Oscillator · Pulse"),
    };

    private readonly Dictionary<string, AudioClip> m_clips =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_failedCustomSounds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SoundOption> m_soundOptions = new();

    private AudioSource m_source;
    private string m_soundsDirectory = "";
    private string m_requestedSoundId = "";
    private string m_requestedAlarmKey = "";
    private long m_requestedAlarmSequence;
    private string m_playingSoundId = "";
    private string m_playingAlarmKey = "";
    private long m_playingAlarmSequence;
    private bool m_playingDefaultFallback;
    private AlarmSeverity m_playingFallbackSeverity;
    private string m_loadingSoundId = "";
    private Coroutine m_loadingCoroutine;
    private bool m_isMuted;

    public string SoundsDirectory => m_soundsDirectory;
    public bool IsMuted => m_isMuted;
    public string PlayingAlarmKey =>
        IsAlarmOccurrencePlaying ? m_playingAlarmKey : "";
    public long PlayingAlarmSequence =>
        IsAlarmOccurrencePlaying ? m_playingAlarmSequence : 0L;

    private bool IsAlarmOccurrencePlaying =>
        !m_isMuted &&
        m_source != null &&
        m_source.isPlaying &&
        !string.IsNullOrWhiteSpace(m_playingAlarmKey) &&
        m_playingAlarmSequence > 0;

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

    /// <summary>
    /// Hard master gate for every UNMA sound, including sound previews. The
    /// selected alarm resumes on the next update after the gate is released.
    /// </summary>
    public void SetMuted(bool muted)
    {
        if (m_isMuted == muted)
        {
            return;
        }
        m_isMuted = muted;
        if (muted)
        {
            StopAlarm();
        }
    }

    public bool IsPlayingAlarm(AlarmView alarm)
    {
        return IsAlarmOccurrencePlaying &&
               AlarmAudioPlaybackPolicy.IsSameOccurrence(
                   alarm,
                   m_playingAlarmKey,
                   m_playingAlarmSequence);
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
        foreach (var option in s_builtinOptions)
        {
            m_soundOptions.Add(new SoundOption(
                option.Id,
                UnmaText.Get(option.LabelKey, option.LabelFallback)));
        }
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
                    UnmaText.Get("auto.3a1cbc4478b0") + fileName));
            }
        }
        catch (Exception exception)
        {
            Log.Warning(
                UnmaText.Get("auto.567bb01fcccb") +
                exception.Message);
        }
    }

    public void UpdateAlarm(AlarmView alarm, int volumePercent)
    {
        if (m_isMuted)
        {
            StopAlarm();
            return;
        }
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
        m_requestedAlarmKey = alarm.Key ?? "";
        m_requestedAlarmSequence = alarm.Sequence;
        if (string.Equals(
                m_playingSoundId,
                soundId,
                StringComparison.OrdinalIgnoreCase) &&
            (!m_playingDefaultFallback ||
             m_playingFallbackSeverity == alarm.Severity) &&
            m_source.isPlaying)
        {
            SetPlayingAlarmIdentityFromRequest();
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
            PlayDefaultFallback(soundId, alarm.Severity);
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
            PlayDefaultFallback(soundId, alarm.Severity);
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
        m_requestedAlarmKey = "";
        m_requestedAlarmSequence = 0L;
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
        m_playingAlarmKey = "";
        m_playingAlarmSequence = 0L;
        m_playingDefaultFallback = false;
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
                                UnmaText.Get("auto.9cff4c7a069f"));
                        }
                        else
                        {
                            clip.name = UnmaText.Get("auto.9efeab6faae0") + fileName;
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
        SetPlayingAlarmIdentityFromRequest();
        m_playingDefaultFallback = false;
    }

    private void SetPlayingAlarmIdentityFromRequest()
    {
        m_playingAlarmKey = m_requestedAlarmKey;
        m_playingAlarmSequence = m_requestedAlarmSequence;
    }

    private void PlayDefaultFallback(
        string requestedSoundId,
        AlarmSeverity severity)
    {
        var fallbackId = ResolveDefaultSoundId(severity);
        if (m_clips.TryGetValue(fallbackId, out var fallbackClip))
        {
            // Keep the requested ID as the playback key. This prevents a
            // missing custom file from restarting the fallback every frame.
            Play(requestedSoundId, fallbackClip);
            m_playingDefaultFallback = true;
            m_playingFallbackSeverity = severity;
            return;
        }
        StopAlarm();
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

        return ResolveDefaultSoundId(alarm.Severity);
    }

    private static string ResolveDefaultSoundId(AlarmSeverity severity)
    {
        return severity switch
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
        AddClip("horn", 4.4f, HornSample);
        AddClip("siren", MechanicalSirenSynth.Generate(SampleRate));
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

        AddClip(id, samples);
    }

    private void AddClip(string id, float[] samples)
    {
        if (samples == null || samples.Length == 0)
        {
            throw new ArgumentException(
                UnmaText.Get("auto.f9c182691284"),
                nameof(samples));
        }

        var clip = AudioClip.Create(
            UnmaText.Get("auto.9efeab6faae0") + id,
            samples.Length,
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
                UnmaText.Get("auto.162e492a1cc9") + id);
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
        const float activeSeconds = 3.2f;
        if (time >= activeSeconds)
        {
            return 0f;
        }

        var attackUnit = Mathf.Clamp01(time / 0.14f);
        var releaseUnit = Mathf.Clamp01(
            (activeSeconds - time) / 0.18f);
        var attack = attackUnit * attackUnit * (3f - 2f * attackUnit);
        var release = releaseUnit * releaseUnit * (3f - 2f * releaseUnit);
        var envelope = attack * release;

        const float fundamental = 112f;
        const float wobbleRate = 1.15f;
        const float wobbleDepth = 0.0045f;
        var phaseCycles = fundamental * time +
            fundamental * wobbleDepth /
            (2f * Mathf.PI * wobbleRate) *
            (1f - Mathf.Cos(2f * Mathf.PI * wobbleRate * time));
        var phase = 2f * Mathf.PI * phaseCycles;
        var pressure = 0.97f +
            0.03f * Mathf.Sin(2f * Mathf.PI * 0.68f * time + 0.4f);
        var signal =
            0.54f * Mathf.Sin(phase) +
            0.25f * Mathf.Sin(2f * phase - 0.10f) +
            0.13f * Mathf.Sin(3f * phase - 0.28f) +
            0.065f * Mathf.Sin(4f * phase - 0.48f) +
            0.025f * Mathf.Sin(5f * phase - 0.70f);
        return envelope * pressure * signal;
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

    private sealed class BuiltInSoundOptionDefinition
    {
        public string Id { get; }
        public string LabelKey { get; }
        public string LabelFallback { get; }

        public BuiltInSoundOptionDefinition(
            string id,
            string labelKey,
            string labelFallback)
        {
            Id = id;
            LabelKey = labelKey;
            LabelFallback = labelFallback;
        }
    }
}
