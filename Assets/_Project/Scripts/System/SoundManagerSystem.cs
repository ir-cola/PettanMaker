using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// プロジェクト共通の1音分の設定。各キャラクター固有の ScriptableObject から参照する。
/// </summary>
[Serializable]
public class SoundCue
{
    public AudioClip Clip;

    [Range(0f, 1f)]
    public float Volume = 1f;

    [Tooltip("再生ごとにこの範囲からピッチを抽選する。(1, 1) で固定。")]
    public Vector2 PitchRange = Vector2.one;

    [Range(0f, 1f)]
    [Tooltip("0=完全2D、1=完全3D。")]
    public float SpatialBlend = 0.8f;

    [Min(0f)] public float MinDistance = 3f;
    [Min(0.1f)] public float MaxDistance = 40f;

    public float RandomPitch()
    {
        float min = Mathf.Min(PitchRange.x, PitchRange.y);
        float max = Mathf.Max(PitchRange.x, PitchRange.y);
        if (max <= 0f) return 1f;
        return UnityEngine.Random.Range(min, max);
    }
}

/// <summary>
/// 共通再生処理へ渡す任意設定。未指定の値は倍率1・Cue設定そのままになる。
/// </summary>
public sealed class SoundPlayOptions
{
    public AudioMixerGroup OutputMixerGroup;
    public float MasterVolume = 1f;
    public float VolumeMultiplier = 1f;
    public float PitchMultiplier = 1f;
    public bool UseRandomPitch = true;
    public float SpatialBlendMultiplier = 1f;
    public float MinDistanceMultiplier = 1f;
    public float MaxDistanceMultiplier = 1f;
    public AnimationCurve DistanceRolloff;
    public bool Loop;
    [Min(0f)] public float LoopStartSeconds;
    [Min(0f)] public float LoopEndSeconds;
    public string ObjectName = "OneShotSE";
}

/// <summary>
/// 再生中の音を操作するハンドル。呼び出し側からピッチ・音量・停止等を変更できる。
/// </summary>
public sealed class SoundHandle
{
    private SoundPlaybackInstance instance;

    internal SoundHandle(SoundPlaybackInstance instance)
    {
        this.instance = instance;
    }

    public bool IsValid => instance != null && instance.Source != null;
    public bool IsPlaying => IsValid && instance.Source.isPlaying;
    public AudioSource Source => IsValid ? instance.Source : null;

    public float Pitch
    {
        get => IsValid ? instance.Source.pitch : 1f;
        set
        {
            if (IsValid) instance.Source.pitch = Mathf.Clamp(value, 0.1f, 3f);
        }
    }

    public float Volume
    {
        get => IsValid ? instance.Source.volume : 0f;
        set
        {
            if (IsValid) instance.Source.volume = Mathf.Clamp01(value);
        }
    }

    public void SetPosition(Vector3 position)
    {
        if (IsValid) instance.transform.position = position;
    }

    public void AttachTo(Transform target)
    {
        if (!IsValid || target == null) return;
        instance.transform.SetParent(target, false);
        instance.transform.localPosition = Vector3.zero;
    }

    public void Pause()
    {
        if (!IsValid) return;
        instance.SetPaused(true);
        instance.Source.Pause();
    }

    public void Resume()
    {
        if (!IsValid) return;
        instance.SetPaused(false);
        instance.Source.UnPause();
    }

    public void FadeTo(float targetVolume, float duration)
    {
        if (IsValid)
            instance.FadeTo(Mathf.Clamp01(targetVolume), duration, destroyWhenSilent: false);
    }

    public void FadeOut(float duration)
    {
        if (!IsValid) return;
        instance.FadeTo(0f, duration, destroyWhenSilent: true);
        instance = null;
    }

    public void Stop()
    {
        if (!IsValid) return;
        instance.StopAndDestroy();
        instance = null;
    }
}

/// <summary>
/// Boss・Player・UI 等から共通利用するSE再生システム。
/// 再生結果の SoundHandle を保持すれば、再生途中のパラメータも操作できる。
/// </summary>
public static class SoundManagerSystem
{
    public static SoundHandle Play(SoundCue cue, Vector3 position, SoundPlayOptions options = null)
    {
        if (cue?.Clip == null) return null;
        options ??= new SoundPlayOptions();

        var go = new GameObject(string.IsNullOrEmpty(options.ObjectName) ? "OneShotSE" : options.ObjectName);
        go.transform.position = position;

        SoundPlaybackInstance instance = go.AddComponent<SoundPlaybackInstance>();
        AudioSource source = go.AddComponent<AudioSource>();
        ConfigureSource(source, cue, options);
        instance.Initialize(source, options.Loop, options.LoopStartSeconds, options.LoopEndSeconds);
        source.Play();

        return new SoundHandle(instance);
    }

    public static SoundHandle PlayAttached(SoundCue cue, Transform target, SoundPlayOptions options = null)
    {
        Vector3 position = target != null ? target.position : Vector3.zero;
        SoundHandle handle = Play(cue, position, options);
        if (handle != null && target != null) handle.AttachTo(target);
        return handle;
    }

    public static void Stop(ref SoundHandle handle, float fadeDuration = 0f)
    {
        if (fadeDuration > 0f) handle?.FadeOut(fadeDuration);
        else handle?.Stop();
        handle = null;
    }

    private static void ConfigureSource(AudioSource source, SoundCue cue, SoundPlayOptions options)
    {
        source.playOnAwake = false;
        source.clip = cue.Clip;
        source.loop = options.Loop;
        source.volume = Mathf.Clamp01(cue.Volume * options.MasterVolume * Mathf.Max(0f, options.VolumeMultiplier));
        float randomPitch = options.UseRandomPitch ? cue.RandomPitch() : 1f;
        source.pitch = Mathf.Clamp(randomPitch * options.PitchMultiplier, 0.1f, 3f);
        source.spatialBlend = Mathf.Clamp01(cue.SpatialBlend * options.SpatialBlendMultiplier);
        source.minDistance = Mathf.Max(0f, cue.MinDistance * options.MinDistanceMultiplier);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, cue.MaxDistance * options.MaxDistanceMultiplier);
        source.dopplerLevel = 0f;
        source.outputAudioMixerGroup = options.OutputMixerGroup;

        if (options.DistanceRolloff != null && options.DistanceRolloff.length >= 2)
        {
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, options.DistanceRolloff);
        }
        else
        {
            source.rolloffMode = AudioRolloffMode.Logarithmic;
        }
    }
}

/// <summary>SoundManagerSystem が生成した AudioSource の寿命を管理する内部コンポーネント。</summary>
public sealed class SoundPlaybackInstance : MonoBehaviour
{
    public AudioSource Source { get; private set; }

    private bool loop;
    private bool paused;
    private bool checkedFirstFrame;
    private Coroutine fadeCoroutine;
    private int loopStartSample = -1;
    private int loopEndSample = -1;

    public void Initialize(AudioSource source, bool isLoop, float loopStartSeconds, float loopEndSeconds)
    {
        Source = source;
        loop = isLoop;

        if (!loop || Source.clip == null)
        {
            Source.loop = false;
            return;
        }

        float clipLength = Source.clip.length;
        float start = Mathf.Clamp(loopStartSeconds, 0f, clipLength);
        float end = loopEndSeconds > start
            ? Mathf.Clamp(loopEndSeconds, start, clipLength)
            : clipLength;

        // クリップ全体を回す場合はUnity標準ループを使う。
        if (start <= 0.001f && end >= clipLength - 0.001f)
        {
            Source.loop = true;
            return;
        }

        int frequency = Source.clip.frequency;
        loopStartSample = Mathf.Clamp(Mathf.RoundToInt(start * frequency), 0, Source.clip.samples - 1);
        loopEndSample = Mathf.Clamp(Mathf.RoundToInt(end * frequency), loopStartSample + 1, Source.clip.samples);
        Source.loop = false;
    }

    public void SetPaused(bool value) => paused = value;

    public void FadeTo(float targetVolume, float duration, bool destroyWhenSilent)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);

        if (Source == null || duration <= 0f)
        {
            if (Source != null) Source.volume = targetVolume;
            if (destroyWhenSilent && targetVolume <= 0f) StopAndDestroy();
            return;
        }

        fadeCoroutine = StartCoroutine(FadeVolume(targetVolume, duration, destroyWhenSilent));
    }

    public void StopAndDestroy()
    {
        if (Source != null) Source.Stop();
        Destroy(gameObject);
    }

    private IEnumerator FadeVolume(float targetVolume, float duration, bool destroyWhenSilent)
    {
        float startVolume = Source.volume;
        float elapsed = 0f;

        while (Source != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            Source.volume = Mathf.Lerp(startVolume, targetVolume, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        fadeCoroutine = null;
        if (Source != null) Source.volume = targetVolume;
        if (destroyWhenSilent && targetVolume <= 0f) StopAndDestroy();
    }

    private void LateUpdate()
    {
        if (loop && loopEndSample > loopStartSample && Source != null && Source.timeSamples >= loopEndSample)
        {
            int length = loopEndSample - loopStartSample;
            int overflow = Source.timeSamples - loopEndSample;
            Source.timeSamples = loopStartSample + overflow % length;
            if (!Source.isPlaying && !paused) Source.Play();
        }

        if (!checkedFirstFrame)
        {
            checkedFirstFrame = true;
            return;
        }

        if (!loop && !paused && (Source == null || !Source.isPlaying))
            Destroy(gameObject);
    }
}
