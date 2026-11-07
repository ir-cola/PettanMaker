/**===========================================================================

 File   : ホバーぷるぷる揺れ [SplatoonHoverJiggle.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/9
 -----------------------------------------------------------------------------

 UI要素にカーソルを重ねた「最初の1〜2秒だけ」スプラトゥーンのメニュー風に
 チャカチャカと小刻みに回転し、その後は止まる。乗せ直す（一度離れて再び
 重ねる）と、また最初から揺れる。
   ・回転のみを操作（スケールは触らない＝「着用中」ハイライト1.08倍と非干渉）
   ・基準姿勢は生成時のローカル回転（セルの傾きに追従）
 raycastTarget を持つ Graphic と同じ GameObject に付けること。

===========================================================================**/
using UnityEngine;
using UnityEngine.EventSystems;

public class SplatoonHoverJiggle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {

    [Tooltip("重ねた瞬間から揺れ続ける時間（秒）")]
    [SerializeField] private float burstDuration = 1.4f;
    [Tooltip("揺れの振れ幅（度）")]
    [SerializeField] private float amplitudeDeg = 9f;
    [Tooltip("揺れの速さ")]
    [SerializeField] private float frequency = 26f;
    [Tooltip("揺れの減衰の強さ（大きいほど早く小さくなる）")]
    [SerializeField] private float decayRate = 3.0f;
    [Tooltip("止まる・離れたとき基準へ戻る速さ")]
    [SerializeField] private float returnSpeed = 16f;

    /// <summary>カーソルを重ねた瞬間に呼ぶ任意コールバック（説明文の切り替え等）。null 可。</summary>
    public System.Action OnHover;
    /// <summary>カーソルを離した瞬間に呼ぶ任意コールバック（説明文を着用中へ戻す等）。null 可。</summary>
    public System.Action OnHoverExit;

    private Quaternion _baseRot;
    private bool  _playing;
    private float _t;

    void Start() {
        _baseRot = transform.localRotation;   // 生成時の傾きを基準に
    }

    // 乗せた瞬間に最初から再生（＋ホバー通知）
    public void OnPointerEnter(PointerEventData e) {
        _playing = true;
        _t = 0f;
        OnHover?.Invoke();                 // カーソルを重ねた時点で説明を切り替える
    }

    public void OnPointerExit(PointerEventData e) {
        _playing = false;
        OnHoverExit?.Invoke();             // カーソルを離したら説明を着用中へ戻す
    }

    void Update() {
        if (_playing) {
            _t += Time.unscaledDeltaTime;
            if (_t >= burstDuration) {                 // 1〜2秒で止める
                _playing = false;
                transform.localRotation = _baseRot;
                return;
            }
            // 減衰しつつ burstDuration でちょうど 0 に収束する包絡線
            float env = Mathf.Exp(-decayRate * _t) * (1f - _t / burstDuration);
            float ang = Mathf.Sin(_t * frequency) * amplitudeDeg * env;
            transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, ang);
        } else {
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, _baseRot, Time.unscaledDeltaTime * returnSpeed);
        }
    }

    void OnDisable() {
        _playing = false;
        transform.localRotation = _baseRot;
    }
}
