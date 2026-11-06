/**===========================================================================

 File   : 撃破ゲージ報酬 [SkillGaugeReward.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------
 敵/ボスに付ける。撃破(死亡)時に SkillGauge.Active へゲージを加算する。
 EnemyHealth / B1_Health / B2_Health の onDied を購読する(在るものを自動判定)。

   ・白兎 (Enemy02) / ボス (Boss01/Boss02) : fillToMax = true  (一気に満タン)
   ・青兎 (Enemy01) / 帽子 (Enemy04) / トランプ兵 (Enemy03) : fillFraction ≒ 1/3

 加算量は敵ごとに Inspector で調整できる(拡張しやすいデータ駆動)。
 ゲージ側が充填不許可(スキル服未着用など)の間は自動的に無視される。
 ===========================================================================**/

using UnityEngine;

public class SkillGaugeReward : MonoBehaviour {

    [Header("撃破時のゲージ報酬")]
    [Tooltip("撃破でゲージを一気に満タンにする(白兎/ボスなど)")]
    [SerializeField] private bool fillToMax = false;

    [Tooltip("満タンにしない場合の加算量 0..1 (青兎/帽子/トランプ兵 ≒ 1/3)")]
    [Range(0f, 1f)]
    [SerializeField] private float fillFraction = 0.334f;

    private bool _rewarded = false;

    private void Start() {
        EnemyHealth eh = GetComponent<EnemyHealth>();
        if (eh != null) { eh.onDied.AddListener(Reward); return; }

        B1_Health b1 = GetComponent<B1_Health>();
        if (b1 != null) { b1.onDied.AddListener(Reward); return; }

        Boss02.B2_Health b2 = GetComponent<Boss02.B2_Health>();
        if (b2 != null) { b2.onDied.AddListener(Reward); return; }

        Debug.LogWarning("[SkillGaugeReward] EnemyHealth / B1_Health / B2_Health が見つかりません: " + name, this);
    }

    private void Reward() {
        if (_rewarded) return;   // 二重加算防止
        _rewarded = true;

        SkillGauge g = SkillGauge.Active;
        if (g == null) return;

        if (fillToMax) g.AddChargeToFull();
        else           g.AddCharge(fillFraction);
    }
}
