/**===========================================================================

 File   : スキルゲージ [SkillGauge.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------
 服のアクティブスキル(アリス巨大化など)の発動リソースとなる「ゲージ」の状態を
 一元管理する低レベルコンポーネント。UI(SkillGaugeUI)・Qアイコン
 (SkillReadyIndicator)・敵撃破報酬(SkillGaugeReward)から静的 Active で参照する。

   ・充填  : チャージ許可中(スキル服を着ていて非発動)は時間経過(fillSeconds)で
             満タンへ。敵撃破では AddCharge / AddChargeToFull で加算。
   ・発動  : BeginDrain(dur) で満タン→0 を dur 秒で減らす。これが「制限時間」。
             0 になると OnDrainEmpty が飛び、持続スキルを終了させる。
   ・リセット: 着替えで ResetGauge()(0 から溜め直し)。

 服ごとに制限時間(減る時間)が変わる想定なので、減る速度は BeginDrain の引数
 (=服の activeDuration)で決まる拡張可能な設計。スキルの有無や発動状態を
 このクラスは知らない(オーケストレーションは PlayerClothSkills が担う)。
 ===========================================================================**/

using System;
using UnityEngine;

public class SkillGauge : MonoBehaviour {

    /// <summary>現在アクティブなゲージ(通常プレイヤーは1体)。</summary>
    public static SkillGauge Active { get; private set; }

    // チューニング値は Resources/SkillGaugeSettings.asset (ScriptableObject) で調整する。
    // 以下はアセットが無い場合のコード既定値 (Awake でアセット値に上書き)。
    private float fillSeconds = 30f;   // 時間経過だけで空→満タンになるまでの秒数

    [Tooltip("現在の溜まり量 0..1 (読み取り用。実行時に変化)")]
    [Range(0f, 1f)]
    [SerializeField] private float value01 = 0f;

    private void Awake() {
        // ランタイム装着され得るため Inspector 参照は持たず、Resources から自動ロードする。
        var settings = Resources.Load<SkillGaugeSettings>("SkillGaugeSettings");
        if (settings == null) {
            Debug.LogWarning("[SkillGauge] Resources/SkillGaugeSettings.asset が見つかりません。コード既定値で動作します。", this);
            return;
        }
        fillSeconds = settings.FillSeconds;
    }

    private bool  _draining        = false;   // スキル発動中(減少中)か
    private float _drainRate        = 0f;      // 秒あたり減少量
    private bool  _chargingAllowed  = false;   // 溜めてよいか(スキル服着用中など。外部から設定)

    // ---- 参照用状態 ----
    public float Value01        => value01;
    public bool  IsFull         => value01 >= 1f - 1e-4f;
    public bool  IsDraining     => _draining;
    public bool  ChargingAllowed=> _chargingAllowed;

    // ---- 通知 ----
    public event Action<float> OnChanged;     // 値が変わった(0..1)
    public event Action        OnBecameFull;  // 空/途中→満タンになった瞬間
    public event Action        OnDrainEmpty;  // 発動中の減少が 0 に達した瞬間

    private void OnEnable()  { Active = this; }
    private void OnDisable() { if (Active == this) Active = null; }

    /// <summary>溜めを許可するか(スキル服を着ていて未発動、など)。PlayerClothSkills が毎フレーム設定。</summary>
    public void SetChargingAllowed(bool allowed) { _chargingAllowed = allowed; }

    /// <summary>ゲージを空にする(着替え時など)。発動中フラグも解除。</summary>
    public void ResetGauge() {
        value01   = 0f;
        _draining = false;
        _drainRate = 0f;
        OnChanged?.Invoke(value01);
    }

    /// <summary>ゲージを amount01 (0..1) 加算する。発動中や充填不許可時は無視。</summary>
    public void AddCharge(float amount01) {
        if (_draining || !_chargingAllowed) return;
        SetValue(value01 + Mathf.Max(0f, amount01));
    }

    /// <summary>ゲージを一気に満タンにする(白兎/ボス撃破など)。発動中や充填不許可時は無視。</summary>
    public void AddChargeToFull() {
        if (_draining || !_chargingAllowed) return;
        SetValue(1f);
    }

    /// <summary>スキル発動: 満タンにして durationSeconds かけて 0 へ減らす(=制限時間)。</summary>
    public void BeginDrain(float durationSeconds) {
        value01    = 1f;
        _draining  = true;
        _drainRate = durationSeconds > 1e-4f ? 1f / durationSeconds : 1000f;
        OnChanged?.Invoke(value01);
    }

    /// <summary>減少を打ち切る(スキルを外部要因で中断したときなど)。値はそのまま。</summary>
    public void StopDrain() {
        _draining  = false;
        _drainRate = 0f;
    }

    private void Update() {
        if (_draining) {
            if (value01 > 0f) {
                value01 = Mathf.Max(0f, value01 - _drainRate * Time.deltaTime);
                OnChanged?.Invoke(value01);
                if (value01 <= 0f) {
                    _draining = false;
                    OnDrainEmpty?.Invoke();
                }
            }
        }
        else if (_chargingAllowed && value01 < 1f && fillSeconds > 0f) {
            SetValue(value01 + Time.deltaTime / fillSeconds);
        }
    }

    private void SetValue(float v) {
        bool wasFull = IsFull;
        value01 = Mathf.Clamp01(v);
        OnChanged?.Invoke(value01);
        if (!wasFull && IsFull) OnBecameFull?.Invoke();
    }
}
