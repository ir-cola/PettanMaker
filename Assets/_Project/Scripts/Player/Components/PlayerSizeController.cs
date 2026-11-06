/**===========================================================================

 File   : プレイヤー体格スケール制御 [PlayerSizeController.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/13
 -----------------------------------------------------------------------------
 プレイヤー root の localScale を一元管理する。服スキル(PlayerClothSkills)から
 「パッシブ倍率(着ている間ずっと)」と「アクティブ倍率(時限オーバーライド)」を
 受け取り、実スケールへ滑らかに反映する。

   ・パッシブ  : SetPassiveScale(mul)  … 装備服に応じた持続倍率 (未装備=1)
   ・アクティブ : TriggerActive(mul,dur) … 発動中はパッシブを上書きする一時倍率

 root の CharacterController は center.y≈height/2 なので、root を等倍スケールしても
 カプセル底(足元)がほぼ据え置きのまま上へ伸縮する = 地面に埋まらない/浮かない。
 補間は unscaledDeltaTime (世界停止=着せ替えチュートリアル中でも滑らかに動く)、
 アクティブの残時間は Time.time (スケール時間。ポーズで一時停止)。ロード中
 (SkinInset.IsBusy) は補間せず即時スナップして、不可視のプリキャッシュ着替えで
 スケールが暴れないようにする。
 ===========================================================================**/

using UnityEngine;

public class PlayerSizeController : MonoBehaviour {

    [Header("補間")]
    [Tooltip("スケール変化の追従速度 (指数平滑の係数。大きいほど素早く目標へ)。約 12 で ~0.3 秒で収束")]
    [SerializeField] private float transitionSpeed = 12f;
    [Tooltip("これ以下の倍率差になったら目標へスナップして補間を打ち切る")]
    [SerializeField] private float snapEpsilon = 0.001f;

    // authoring スケール (Awake で確定。プレハブ側の等倍前提だが実値を尊重する)
    private Vector3 _baseScale = Vector3.one;

    private float _passiveMul = 1f;    // 装備服のパッシブ持続倍率
    private float _activeMul   = 1f;    // アクティブ発動中の一時倍率
    private bool  _activeRunning = false;
    private float _activeUntil = 0f;    // Time.time 基準の失効時刻
    private float _currentMul = 1f;     // 実際に適用中の倍率 (補間の状態)

    /// <summary>いま適用中のスケール倍率 (authoring=1 基準)。</summary>
    public float CurrentMultiplier => _currentMul;
    /// <summary>authoring の localScale。</summary>
    public Vector3 BaseScale => _baseScale;
    /// <summary>アクティブ(時限)スキルが進行中か。</summary>
    public bool IsActive => _activeRunning;
    /// <summary>アクティブの残り秒 (未発動は 0)。</summary>
    public float ActiveRemaining => _activeRunning ? Mathf.Max(0f, _activeUntil - Time.time) : 0f;

    private void Awake() {
        _baseScale = transform.localScale;
        _currentMul = 1f;
        _passiveMul = 1f;
        _activeMul  = 1f;
    }

    /// <summary>装備服のパッシブ倍率を設定 (未装備/スキル無しは 1)。</summary>
    public void SetPassiveScale(float mul) {
        _passiveMul = Mathf.Max(0.01f, mul);
    }

    /// <summary>アクティブ(時限)スキル発動: dur 秒だけ倍率 mul でパッシブを上書きする。</summary>
    public void TriggerActive(float mul, float duration) {
        _activeMul     = Mathf.Max(0.01f, mul);
        _activeUntil   = Time.time + Mathf.Max(0f, duration);
        _activeRunning = true;
    }

    /// <summary>アクティブを即時終了 (服を脱ぐ/着替えた時など)。パッシブへ戻る。</summary>
    public void CancelActive() {
        _activeRunning = false;
    }

    private void Update() {
        if (_activeRunning && Time.time >= _activeUntil) _activeRunning = false;

        float target = _activeRunning ? _activeMul : _passiveMul;

        // ロード中(不可視のプリキャッシュ着替え)は補間せず即時反映してスケールの暴れを避ける
        if (SkinInset.IsBusy) {
            _currentMul = target;
        }
        else {
            float k = 1f - Mathf.Exp(-transitionSpeed * Time.unscaledDeltaTime);
            _currentMul = Mathf.Lerp(_currentMul, target, k);
            if (Mathf.Abs(_currentMul - target) <= snapEpsilon) _currentMul = target;
        }

        transform.localScale = _baseScale * _currentMul;
    }
}
