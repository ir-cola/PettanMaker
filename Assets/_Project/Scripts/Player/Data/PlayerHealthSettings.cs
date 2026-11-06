/*
 File   : プレイヤーHP設定アセット [PlayerHealthSettings.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 HP / 被弾無敵 / ノックバックのチューニング値を ScriptableObject として資産化したもの。
 既定値は Cheshire シーンの Player (PlayerHealth) の調整済み値。
 PlayerHealth にアサインすると Awake でシーン側の値を上書きする。
*/
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerHealthSettings", menuName = "Player/Health Settings")]
public class PlayerHealthSettings : ScriptableObject {

    [Header("HP設定")]
    [Min(1)] public int MaxHP = 5;

    [Header("被弾無敵")]
    [Tooltip("被弾後の無敵時間 (秒)。この間はダメージを受けず、PlayerHitReaction がモデルを点滅させる")]
    [Min(0f)] public float InvincibleDuration = 1.0f;

    [Header("被弾ノックバック")]
    [Tooltip("被弾時に押し出される合計距離 (m)")]
    [Min(0f)] public float KnockbackDistance = 2.0f;
    [System.Serializable]
    public class KnockbackPhaseDurations {
        [Tooltip("仰け反り (KnockBack_Idle) の総時間 (秒)。倒れ込まないタイプ (DamageType 1) で使用。押し出し〜Locomotion復帰まで")]
        [Min(0f)] public float Idle = 0.25f;
        [Tooltip("倒れ込み (KnockBack_Ground) にかける時間 (秒)。DamageType 2 以上で使用。押し出しはこの時間+Recoverの合計で行われる")]
        [Min(0f)] public float Ground = 0.25f;
        [Tooltip("起き上がり (Recover) にかける時間 (秒)。DamageType 2 以上で使用。この時間の終了と同時に移動可能になる")]
        [Min(0f)] public float Recover = 0.25f;
    }

    [Tooltip("DamageType 別のフェーズ時間。要素番号 = DamageType " +
             "(0=未使用 / 1=仰け反り / 2=地面倒れ込み)。要素が足りなければ末尾の値を使う")]
    public KnockbackPhaseDurations[] KnockbackTimings = { new(), new(), new() };

    [Tooltip("ノックバックから復帰した (移動可能になった) 直後の追加無敵時間 (秒)。" +
             "復帰した瞬間に猶予ゼロで連続被弾しないようにするための猶予。0 で無効")]
    [Min(0f)] public float KnockbackRecoveryGrace = 0.3f;
}
