/*
 File   : プレイヤー設定アセット [PlayerSettings.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 移動 / 回避 / ジャンプ等のチューニング値を ScriptableObject として資産化したもの。
 既定値は Cheshire シーンの Player (PlayerLifetimeScope) の調整済み値。
 PlayerLifetimeScope にアサインすると Awake でシーン側の値を上書きする。
*/
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSettings", menuName = "Player/Player Settings")]
public class PlayerSettings : ScriptableObject {

    [Header("移動設定")]
    public float Speed = 9.0f;
    public float Gravity = -9.81f;
    public float RotationSpeed = 10.0f;

    [Header("移動フィール (C++参照実装由来。既定値=現行の操作感)")]
    [Tooltip("滑り出しの鋭さ。初期加速 = Speed × この値。最高速は変わらない")]
    public float MoveDrag = 7.0f;
    [Tooltip("最小入力時の速度倍率 (歩き)。1 に近いほど弱い入力でも全速")]
    [Range(0f, 1f)]
    public float WalkFloor = 0.3f;
    [Tooltip("入力を離した後の慣性スライドの減衰。大きいほどすぐ止まる")]
    public float InertiaDecay = 6.0f;
    [Tooltip("向き直り速度 (度/秒)。目標方向との角度差が 90°を超えるとき")]
    public float QuickTurnSpeed = 720f;
    [Tooltip("向き直り速度 (度/秒)。角度差 45〜90°のとき")]
    public float MediumTurnSpeed = 540f;
    [Tooltip("角度差 45°未満の指数イーズ速度。大きいほど機敏に向き直る")]
    public float RotEaseSpeed = 8f;

    [Header("攻撃フィール")]
    [Tooltip("攻撃発動後、さらに移動をロックする時間 (秒)。攻撃の手応え")]
    public float AttackRecoveryTime = 0.40f;
    [Tooltip("チャージ中の移動速度倍率")]
    [Range(0f, 1f)]
    public float ChargeMoveSpeedMul = 0.40f;

    [Header("回避設定")]
    [Tooltip("回避の移動距離 (m)。速度を変えても距離は変わらない")]
    public float DodgeDistance = 2.5f;
    [Tooltip("回避の速度倍率 (Speed × この値)。上げると同じ距離をより速く駆け抜ける")]
    public float DodgeMultiplier = 1.5f;
    [Tooltip("ダッシュ(回避)のクールタイム (秒)。回避後この時間は再回避できない")]
    [Min(0f)] public float DodgeCooldown = 0.5f;

    [Header("ジャンプ設定")]
    public float JumpHeight = 1.8f;

    [Header("接地判定")]
    public float GroundCheckDistance = 0.3f;

    [Header("インプットバッファ")]
    public float BufferTime = 0.2f;
}
