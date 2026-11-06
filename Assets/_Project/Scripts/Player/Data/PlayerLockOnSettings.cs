/*
 File   : プレイヤーロックオン設定アセット [PlayerLockOnSettings.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 攻撃中ロックオン (PlayerLockOn) のチューニング値を ScriptableObject として資産化したもの。
 PlayerLockOn はランタイム装着 (Ensure) されるため、Resources/PlayerLockOnSettings.asset を
 Awake で自動ロードする。アセットが無い場合はコード既定値で動作する。
*/
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerLockOnSettings", menuName = "Player/LockOn Settings")]
public class PlayerLockOnSettings : ScriptableObject {

    [Header("索敵設定")]
    [Tooltip("ロック対象を探す半径 (m)")]
    public float LockOnRadius = 12f;
    [Tooltip("この距離より離れたらロック解除 (m)。索敵半径より大きめにしてチラつき防止")]
    public float LockOnBreakRadius = 18f;
    [Tooltip("未ロック時の索敵間隔 (秒)")]
    [Min(0.02f)] public float ScanInterval = 0.15f;

    [Header("レティクル")]
    public Color ReticleColor = new Color(1f, 0.3f, 0.45f, 1f);
    [Tooltip("レティクルの一辺サイズ (m)")]
    public float ReticleSize = 1.4f;
    [Tooltip("サイズのパルス振幅 (割合)")]
    [Range(0f, 0.3f)] public float ReticlePulse = 0.07f;
}
