/**===========================================================================

 File   : BGM 管理 [BGMManager.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/4
 -----------------------------------------------------------------------------
 シーンとロード画面に応じて BGM をクロスフェード再生する常駐マネージャ。
 シーンへの配置は不要 (起動時に自動生成して DontDestroyOnLoad)。

   ・Title シーン                         → title
   ・StageSelect シーン                   → stageSelect
   ・ロード画面表示中 (SceneLoader.IsActive) → loading (シーンより優先)
   ・Stage_* シーン (インゲーム)          → stage (Stage_1_Load)
   ・上記以外のシーン                     → 無音へフェード

 クリップ割り当て・音量は Assets/_Project/Resources/BGMAssets.asset で調整。
 ボス戦 BGM などへの切り替えは BGMManager.SetOverride(clip) / ClearOverride()。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BGMManager : MonoBehaviour {

    private static BGMManager instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() {
        if (instance != null) return;

        BGMAssets assets = Resources.Load<BGMAssets>("BGMAssets");
        if (assets == null) {
            Debug.LogWarning("[BGMManager] Resources/BGMAssets.asset が見つかりません。BGM は再生されません。");
            return;
        }

        var go = new GameObject("BGMManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BGMManager>();
        instance.assets = assets;
        instance.InitSources();
    }

    /// <summary>シーン既定より優先して流す BGM (ボス戦など)。null 指定は ClearOverride と同じ。</summary>
    public static void SetOverride(AudioClip clip) {
        if (clip == null) {
            ClearOverride();
            return;
        }

        if (instance != null) {
            instance.overrideClip = clip;
            instance.ResetVolumeModulation();
        }
    }

    public static void ClearOverride() {
        if (instance != null) {
            instance.overrideClip = null;
            instance.ResetVolumeModulation();
        }
    }

    /// <summary>BGM の基準音量倍率を滑らかに変更する。攻撃の溜めなど、少し長い変化に使う。</summary>
    public static void SetVolumeScale(float scale, float fadeTime = 0.2f) {
        if (instance != null) instance.SetVolumeScaleInternal(scale, fadeTime);
    }

    /// <summary>BGM の基準音量倍率を 1 に戻す。</summary>
    public static void ResetVolumeScale(float fadeTime = 0.2f) {
        SetVolumeScale(1f, fadeTime);
    }

    /// <summary>着弾などに合わせて一度だけ BGM を沈め、元の基準音量へ戻す。</summary>
    public static void PulseVolume(float scale, float attackTime, float holdTime, float releaseTime) {
        if (instance != null) instance.PulseVolumeInternal(scale, attackTime, holdTime, releaseTime);
    }

    private BGMAssets assets;
    private AudioSource sourceA;
    private AudioSource sourceB;
    private AudioSource active;
    private AudioClip currentClip;
    private AudioClip overrideClip;
    private float fade = 1f;                 // 0→1 でクロスフェード進行
    private float volumeScale = 1f;
    private float volumeScaleStart = 1f;
    private float volumeScaleTarget = 1f;
    private float volumeScaleElapsed;
    private float volumeScaleDuration;
    private float pulseScale = 1f;
    private float pulseTargetScale = 1f;
    private Coroutine volumePulse;

    private void InitSources() {
        sourceA = CreateSource();
        sourceB = CreateSource();
        active = sourceA;
        SwitchTo(SelectClip(), immediate: true);
    }

    private AudioSource CreateSource() {
        var s = gameObject.AddComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;                 // 2D 再生
        s.volume = 0f;
        return s;
    }

    private void Update() {
        AudioClip want = SelectClip();
        if (want != currentClip) SwitchTo(want, immediate: false);

        UpdateVolumeScale();

        // クロスフェード進行 (ロード中も止まらないよう unscaled)
        fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / Mathf.Max(0.05f, assets.fadeTime));
        AudioSource back = (active == sourceA) ? sourceB : sourceA;
        float volume = assets.volume * volumeScale * pulseScale;
        active.volume = volume * fade;
        back.volume = volume * (1f - fade);
        if (fade >= 1f && back.isPlaying) back.Stop();
    }

    private void SetVolumeScaleInternal(float scale, float fadeTime) {
        volumeScaleStart = volumeScale;
        volumeScaleTarget = Mathf.Clamp01(scale);
        volumeScaleElapsed = 0f;
        volumeScaleDuration = Mathf.Max(0f, fadeTime);

        if (volumeScaleDuration <= 0f) volumeScale = volumeScaleTarget;
    }

    private void UpdateVolumeScale() {
        if (Mathf.Approximately(volumeScale, volumeScaleTarget)) return;

        volumeScaleElapsed += Time.unscaledDeltaTime;
        float t = volumeScaleDuration > 0f
            ? Mathf.Clamp01(volumeScaleElapsed / volumeScaleDuration)
            : 1f;
        volumeScale = Mathf.Lerp(volumeScaleStart, volumeScaleTarget, Mathf.SmoothStep(0f, 1f, t));
    }

    private void PulseVolumeInternal(float scale, float attackTime, float holdTime, float releaseTime) {
        float requestedScale = Mathf.Clamp01(scale);
        if (volumePulse != null && requestedScale > pulseTargetScale) return;

        if (volumePulse != null) StopCoroutine(volumePulse);
        pulseTargetScale = requestedScale;
        volumePulse = StartCoroutine(VolumePulseRoutine(
            requestedScale,
            Mathf.Max(0f, attackTime),
            Mathf.Max(0f, holdTime),
            Mathf.Max(0f, releaseTime)));
    }

    private IEnumerator VolumePulseRoutine(float scale, float attackTime, float holdTime, float releaseTime) {
        yield return FadePulseTo(scale, attackTime);

        float elapsed = 0f;
        while (elapsed < holdTime) {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return FadePulseTo(1f, releaseTime);
        pulseScale = 1f;
        pulseTargetScale = 1f;
        volumePulse = null;
    }

    private IEnumerator FadePulseTo(float target, float duration) {
        float start = pulseScale;
        if (duration <= 0f) {
            pulseScale = target;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            pulseScale = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        pulseScale = target;
    }

    private void ResetVolumeModulation() {
        if (volumePulse != null) StopCoroutine(volumePulse);
        volumePulse = null;
        volumeScale = 1f;
        volumeScaleStart = 1f;
        volumeScaleTarget = 1f;
        volumeScaleElapsed = 0f;
        volumeScaleDuration = 0f;
        pulseScale = 1f;
        pulseTargetScale = 1f;
    }

    // 今流すべき BGM。ロード画面が最優先、次に上書き指定、最後にシーン名。
    private AudioClip SelectClip() {
        if (SceneLoader.IsActive) return assets.loading;
        if (overrideClip != null) return overrideClip;

        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Title") return assets.title;
        if (scene == "StageSelect") return assets.stageSelect;
        if (scene.StartsWith("Stage")) return assets.stage;   // Stage_1 等 (StageSelect は上で処理済み)
        return null;                                          // 未対応シーンは無音へ
    }

    private void SwitchTo(AudioClip clip, bool immediate) {
        currentClip = clip;

        AudioSource prev = active;
        AudioSource next = (active == sourceA) ? sourceB : sourceA;
        next.clip = clip;
        next.volume = immediate ? assets.volume : 0f;
        if (clip != null) next.Play();
        else next.Stop();

        active = next;
        fade = immediate ? 1f : 0f;
        if (immediate) prev.Stop();
    }
}
