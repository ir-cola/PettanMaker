/**===========================================================================

 File   : UI揺れ演出クラス [UISway.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30
 -----------------------------------------------------------------------------

   RectTransform をゆらゆらと揺らす演出。
   回転(Z)・上下バウンド・左右スウェイ・呼吸スケールを合成し、
   タイトルのキャラクターなどに自然でやわらかい揺れを与える。
   揺れは基準値からの相対なので、レイアウトを壊さず重ねられる。

 ===========================================================================**/
using UnityEngine;

/**===========================================================================
  UI をゆらゆら揺らす演出クラス
===========================================================================**/
[DisallowMultipleComponent]
public class UISway : MonoBehaviour {

    [Header("回転スウェイ (Z)")]
    [Tooltip("左右に傾く最大角度(度)")]
    [SerializeField] float _rotateAngle = 4.0f;
    [Tooltip("回転の周期(秒)")]
    [SerializeField] float _rotatePeriod = 3.2f;

    [Header("上下バウンド")]
    [Tooltip("上下に揺れる振幅(ピクセル)")]
    [SerializeField] float _bobAmplitude = 16.0f;
    [Tooltip("上下バウンドの周期(秒)")]
    [SerializeField] float _bobPeriod = 2.6f;

    [Header("左右スウェイ")]
    [Tooltip("左右に揺れる振幅(ピクセル)")]
    [SerializeField] float _swayAmplitude = 9.0f;
    [Tooltip("左右スウェイの周期(秒)")]
    [SerializeField] float _swayPeriod = 4.4f;

    [Header("呼吸スケール")]
    [Tooltip("拡大縮小の振幅(0で無効。0.02 = ±2%)")]
    [SerializeField] float _breathAmplitude = 0.015f;
    [Tooltip("呼吸の周期(秒)")]
    [SerializeField] float _breathPeriod = 3.0f;

    [Header("回転の支点")]
    [Tooltip("回転中心のオフセット(RectTransform ローカル単位)。Y を負にすると足元を支点に振れる")]
    [SerializeField] Vector2 _pivotOffset = Vector2.zero;

    [Header("その他")]
    [Tooltip("ポーズの影響を受けない時間で動かす")]
    [SerializeField] bool _useUnscaledTime = true;
    [Tooltip("開始位相をランダムにして単調さを避ける")]
    [SerializeField] bool _randomizePhase = true;

    const float TAU = Mathf.PI * 2.0f;

    RectTransform _rect;
    Vector2 _baseAnchoredPos;
    Quaternion _baseRotation;
    Vector3 _baseScale;
    float _phase;

    void Awake() {
        _rect = transform as RectTransform;
    }

    void OnEnable() {
        if (_rect == null) _rect = transform as RectTransform;
        if (_rect == null) return;

        // 基準値を記録(揺れはこの値からの相対オフセット)
        _baseAnchoredPos = _rect.anchoredPosition;
        _baseRotation    = _rect.localRotation;
        _baseScale       = _rect.localScale;
        _phase = _randomizePhase ? Random.value * TAU : 0.0f;
    }

    void OnDisable() {
        // 揺れの影響を残さないよう基準値へ戻す
        if (_rect == null) return;
        _rect.anchoredPosition = _baseAnchoredPos;
        _rect.localRotation    = _baseRotation;
        _rect.localScale       = _baseScale;
    }

    void Update() {
        if (_rect == null) return;

        float t = (_useUnscaledTime ? Time.unscaledTime : Time.time) + _phase;

        // --- 回転スウェイ ---
        float angle = 0.0f;
        if (_rotatePeriod > 0.0001f && _rotateAngle != 0.0f) {
            angle = Mathf.Sin(t * (TAU / _rotatePeriod)) * _rotateAngle;
        }
        _rect.localRotation = _baseRotation * Quaternion.Euler(0.0f, 0.0f, angle);

        // --- 位置(上下バウンド + 左右スウェイ + 支点補正) ---
        Vector2 pos = _baseAnchoredPos;
        if (_bobPeriod > 0.0001f) {
            pos.y += Mathf.Sin(t * (TAU / _bobPeriod)) * _bobAmplitude;
        }
        if (_swayPeriod > 0.0001f) {
            pos.x += Mathf.Sin(t * (TAU / _swayPeriod)) * _swayAmplitude;
        }
        // 支点オフセットがある場合、その点を中心に回って見えるよう位置を補正
        // (親 Canvas は無回転・無スケールのためこの近似で十分)
        if (_pivotOffset != Vector2.zero && angle != 0.0f) {
            float rad = angle * Mathf.Deg2Rad;
            float s = Mathf.Sin(rad);
            float c = Mathf.Cos(rad);
            Vector2 v = _pivotOffset;
            Vector2 rotated = new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
            pos += v - rotated;
        }
        _rect.anchoredPosition = pos;

        // --- 呼吸スケール(符号を保持して反転キャラにも対応) ---
        if (_breathPeriod > 0.0001f && _breathAmplitude != 0.0f) {
            float scl = 1.0f + Mathf.Sin(t * (TAU / _breathPeriod)) * _breathAmplitude;
            _rect.localScale = new Vector3(_baseScale.x * scl, _baseScale.y * scl, _baseScale.z);
        }
    }
}
