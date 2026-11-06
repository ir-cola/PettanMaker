using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Boss01 シーン用のベースポストプロセス。
/// URP の Volume をランタイムに構築し、Main Camera のポストプロセスを有効化する。
/// ACES トーンマップ + Bloom + カラーグレーディング + Vignette を常時かけて
/// 戦闘シーンの絵を底上げする。値はすべて Inspector から調整可能。
/// </summary>
[DisallowMultipleComponent]
public class B1_PostProcess : MonoBehaviour
{
    [Header("共有プロファイル (通常時)")]
    [Tooltip("Stage_01 と同じ VolumeProfile (Stage1_PostProcess.asset) を割り当てると、" +
             "下の各パラメータからのランタイム構築を行わず、このプロファイルをそのまま使う。" +
             "null のときは従来どおり Inspector の値から構築する。")]
    [SerializeField] private VolumeProfile sharedProfile;

    [Header("Tonemapping")]
    [Tooltip("ACES トーンマップ。ハイライトの飛びを抑えて一気に映画的な絵になる。")]
    [SerializeField] private bool tonemapping = true;

    [Header("Bloom (発光)")]
    [SerializeField] private bool  bloom = true;
    [Tooltip("この輝度を超えたピクセルが発光する。攻撃エフェクトやテレグラフを光らせる。")]
    [SerializeField] private float bloomThreshold = 0.9f;
    [SerializeField] private float bloomIntensity = 0.8f;
    [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.7f;
    [SerializeField] private Color bloomTint = Color.white;

    [Header("Color Grading (色調整)")]
    [SerializeField] private bool colorGrading = true;
    [Tooltip("ACES トーンマップは中間調が暗くなるため、+0.25 前後で補正して画面の明るさを保つ。")]
    [SerializeField, Range(-2f, 2f)]     private float postExposure = 0.25f;
    [SerializeField, Range(-100f, 100f)] private float contrast = 8f;
    [SerializeField, Range(-100f, 100f)] private float saturation = 10f;
    [SerializeField] private Color colorFilter = Color.white;

    [Header("Shadows / Midtones / Highlights")]
    [Tooltip("影を青紫に寄せてチェシャ猫の妖しい雰囲気を出す。白 = 補正なし。")]
    [SerializeField] private bool  shadowTint = true;
    [SerializeField] private Color shadowColor    = new Color(0.98f, 0.95f, 1.06f, 1f);
    [SerializeField] private Color midtoneColor   = Color.white;
    [SerializeField] private Color highlightColor = Color.white;

    [Header("Vignette (周辺減光)")]
    [SerializeField] private bool vignette = true;
    [Tooltip("強すぎると画面全体が暗く見える。0.2 以下推奨。")]
    [SerializeField, Range(0f, 1f)]     private float vignetteIntensity = 0.18f;
    [SerializeField, Range(0.01f, 1f)]  private float vignetteSmoothness = 0.6f;
    [SerializeField] private Color vignetteColor = new Color(0.05f, 0.03f, 0.08f, 1f);

    [Header("質感 (かけすぎ注意)")]
    [SerializeField] private bool filmGrain = true;
    [SerializeField, Range(0f, 1f)] private float filmGrainIntensity = 0.08f;
    [SerializeField] private bool chromaticAberration = true;
    [SerializeField, Range(0f, 1f)] private float chromaticIntensity = 0.06f;

    [Header("Camera")]
    [Tooltip("SMAA (High)。MSAA と併用してエッジを綺麗にする。文字がにじむ場合は OFF。")]
    [SerializeField] private bool enableSmaa = true;

    private Volume volume;
    private bool   ownsProfile;

    private void Awake()
    {
        EnableCameraPostProcessing();
        volume = BuildVolume();
    }

    private void EnableCameraPostProcessing()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.allowHDR = true;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        data.renderPostProcessing = true;
        if (enableSmaa)
        {
            data.antialiasing        = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
    }

    private Volume BuildVolume()
    {
        // Stage_01 の共有プロファイルが割り当てられていれば、それをそのまま使い回す。
        // sharedProfile はアセット本体なので破棄してはいけない（ownsProfile=false）。
        if (sharedProfile != null)
        {
            var shared = gameObject.AddComponent<Volume>();
            shared.isGlobal      = true;
            shared.priority      = 10f;
            shared.sharedProfile = sharedProfile;
            ownsProfile = false;
            return shared;
        }

        ownsProfile = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        if (tonemapping)
        {
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
        }
        if (bloom)
        {
            var b = profile.Add<Bloom>(true);
            b.threshold.Override(Mathf.Max(0f, bloomThreshold));
            b.intensity.Override(Mathf.Max(0f, bloomIntensity));
            b.scatter.Override(bloomScatter);
            b.tint.Override(bloomTint);
        }
        if (colorGrading)
        {
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(postExposure);
            ca.contrast.Override(contrast);
            ca.saturation.Override(saturation);
            ca.colorFilter.Override(colorFilter);
        }
        if (shadowTint)
        {
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(ToSmhVector(shadowColor));
            smh.midtones.Override(ToSmhVector(midtoneColor));
            smh.highlights.Override(ToSmhVector(highlightColor));
        }
        if (vignette)
        {
            var v = profile.Add<Vignette>(true);
            v.intensity.Override(vignetteIntensity);
            v.smoothness.Override(vignetteSmoothness);
            v.color.Override(vignetteColor);
        }
        if (filmGrain)
        {
            var fg = profile.Add<FilmGrain>(true);
            fg.type.Override(FilmGrainLookup.Thin1);
            fg.intensity.Override(filmGrainIntensity);
        }
        if (chromaticAberration)
        {
            var cab = profile.Add<ChromaticAberration>(true);
            cab.intensity.Override(chromaticIntensity);
        }

        var vol = gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.profile  = profile;
        return vol;
    }

    /// <summary>SMH の Vector4 パラメータへ変換する (xyz = 色、w = オフセット)。</summary>
    private static Vector4 ToSmhVector(Color c)
    {
        return new Vector4(c.r, c.g, c.b, 0f);
    }

    private void OnDestroy()
    {
        // 共有プロファイル使用時はアセットを破棄しない
        // (volume.profile の getter は sharedProfile のクローンを生成してしまうため触らない)。
        if (ownsProfile && volume != null && volume.profile != null)
        {
            Destroy(volume.profile);
        }
    }
}
