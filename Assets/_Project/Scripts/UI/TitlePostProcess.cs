/**===========================================================================

 File   : タイトル用ポストプロセス [TitlePostProcess.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30
 -----------------------------------------------------------------------------

   タイトル画面にだけ、控えめな Bloom・Vignette・カラーグレーディングを
   かけるためのクラス。URP の Volume をランタイムに構築し、Main Camera の
   ポストプロセスを有効化する。Canvas は Screen Space - Camera 前提
   (オーバーレイUIはカメラのポストプロセス対象外のため)。

 ===========================================================================**/
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class TitlePostProcess : MonoBehaviour {

    [Header("Bloom (発光)")]
    [SerializeField] bool  _bloom = true;
    [SerializeField] float _bloomThreshold = 1.0f;
    [SerializeField] float _bloomIntensity = 0.55f;
    [SerializeField, Range(0f, 1f)] float _bloomScatter = 0.68f;
    [SerializeField] Color _bloomTint = Color.white;

    [Header("Vignette (周辺減光)")]
    [SerializeField] bool  _vignette = true;
    [SerializeField, Range(0f, 1f)] float _vignetteIntensity = 0.24f;
    [SerializeField, Range(0.01f, 1f)] float _vignetteSmoothness = 0.6f;
    [SerializeField] Color _vignetteColor = new Color(0.05f, 0.03f, 0.08f, 1f);

    [Header("Color Grading (色調整)")]
    [SerializeField] bool  _colorGrading = true;
    [SerializeField, Range(-2f, 2f)] float _postExposure = 0.0f;
    [SerializeField, Range(-100f, 100f)] float _contrast = 7f;
    [SerializeField, Range(-100f, 100f)] float _saturation = 8f;
    [SerializeField] Color _colorFilter = new Color(1.0f, 0.98f, 0.94f, 1f);

    [Header("Tonemapping")]
    [SerializeField] bool _tonemapping = false; // ON で Neutral トーンマップ

    [Header("Camera")]
    [SerializeField] bool _enableFxaa = false;  // 文字がにじむ場合は OFF

    Volume _volume;

    void Awake() {
        EnableCameraPostProcessing();
        _volume = BuildVolume();
    }

    void EnableCameraPostProcessing() {
        var cam = Camera.main;
        if (cam == null) return;
        cam.allowHDR = true;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) {
            data.renderPostProcessing = true;
            data.antialiasing = _enableFxaa ? AntialiasingMode.FastApproximateAntialiasing
                                            : AntialiasingMode.None;
        }
    }

    Volume BuildVolume() {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        if (_bloom) {
            var b = profile.Add<Bloom>(true);
            b.threshold.Override(Mathf.Max(0f, _bloomThreshold));
            b.intensity.Override(Mathf.Max(0f, _bloomIntensity));
            b.scatter.Override(_bloomScatter);
            b.tint.Override(_bloomTint);
        }
        if (_vignette) {
            var v = profile.Add<Vignette>(true);
            v.intensity.Override(_vignetteIntensity);
            v.smoothness.Override(_vignetteSmoothness);
            v.color.Override(_vignetteColor);
        }
        if (_colorGrading) {
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(_postExposure);
            ca.contrast.Override(_contrast);
            ca.saturation.Override(_saturation);
            ca.colorFilter.Override(_colorFilter);
        }
        if (_tonemapping) {
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
        }

        var vol = gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.profile  = profile;
        return vol;
    }

    void OnDestroy() {
        if (_volume != null && _volume.profile != null) {
            Destroy(_volume.profile);
        }
    }
}
