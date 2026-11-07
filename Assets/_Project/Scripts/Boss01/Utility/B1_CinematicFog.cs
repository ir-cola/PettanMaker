using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// カメラ演出中だけ有効化する実行時フォグ。
/// B1_CameraSceneSettings.FogSettings の値を URP の HeightFogVolume に流し込み、
/// 高優先度の Global Volume として重ねてブレンドする。演出終了時は
/// restoreFogOnFinish に従って元のシーンフォグへ戻すか、そのまま残す。
///
/// 実際の描画は既存の PostProcessFeature が VolumeManager のスタックから
/// HeightFogVolume を読み取って行う。ここはその値を一時的に上書きするだけ。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_CinematicFog : MonoBehaviour
{
    private Volume volume;
    private VolumeProfile profile;
    private bool restoreOnFinish;

    /// <summary>設定から演出用フォグを生成する(weight は 0 から開始)。</summary>
    public static B1_CinematicFog Create(B1_CameraSceneSettings.FogSettings s)
    {
        if (s == null) return null;
        var go = new GameObject("B1_CinematicFog");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト。シーンに保存しない
        var self = go.AddComponent<B1_CinematicFog>();
        self.Init(s);
        return self;
    }

    private void Init(B1_CameraSceneSettings.FogSettings s)
    {
        restoreOnFinish = s.restoreFogOnFinish;

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10000f; // 既存の Global Volume より確実に上へ
        volume.weight = 0f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "B1_CinematicFog (Runtime)";
        volume.sharedProfile = profile;

        var fog = profile.Add<HeightFogVolume>(true);
        fog.fogColor.Override(s.fogColor);
        fog.heightBase.Override(s.heightBase);
        fog.heightFalloff.Override(Mathf.Clamp(s.heightFalloff, 0.001f, 1f));
        fog.heightStrength.Override(Mathf.Clamp(s.heightStrength, 0f, 3f));
        fog.distanceStart.Override(Mathf.Max(0f, s.distanceStart));
        fog.distanceEnd.Override(Mathf.Max(0f, s.distanceEnd));
        fog.distanceStrength.Override(Mathf.Clamp(s.distanceStrength, 0f, 2f));
    }

    /// <summary>0〜1 でシーンフォグへのブレンド量を設定する。</summary>
    public void SetWeight(float w)
    {
        if (volume != null) volume.weight = Mathf.Clamp01(w);
    }

    /// <summary>
    /// 演出終了処理。restoreFogOnFinish=true なら破棄して元のフォグへ戻し、
    /// false なら weight=1 のまま残す(演出後もこのフォグを維持したいケース)。
    /// </summary>
    public void Finish()
    {
        if (restoreOnFinish) Dispose();
        else SetWeight(1f);
    }

    /// <summary>即座に破棄して元のシーンフォグへ戻す。</summary>
    public void Dispose()
    {
        if (profile != null) { B1_CinematicUtil.SafeDestroy(profile); profile = null; }
        if (this != null && gameObject != null) B1_CinematicUtil.SafeDestroy(gameObject);
    }

    private void OnDestroy()
    {
        if (profile != null) { B1_CinematicUtil.SafeDestroy(profile); profile = null; }
    }
}
