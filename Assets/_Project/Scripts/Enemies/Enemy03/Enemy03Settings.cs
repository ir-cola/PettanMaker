using UnityEngine;

[CreateAssetMenu(fileName = "Enemy03Settings", menuName = "Enemies/Enemy03 Settings")]
public class Enemy03Settings : EnemySettings
{
    [Header("移動")]
    public float WalkSpeed = 1.5f;
    public float RunSpeed = 3.6f;

    [Header("SE - Walk")]
    public AudioClip WalkSE;
    [Range(0f, 1f)] public float WalkSEVolume = 1f;
    [Tooltip("徘徊Walk中にSEを鳴らす間隔（秒）。")]
    [Min(0.05f)] public float WalkSEInterval = 0.45f;
    [Tooltip("Walk SEの先頭から再生する長さ（秒）。")]
    [Min(0.01f)] public float WalkSEPlaybackDuration = 0.4f;

    [Header("攻撃")]
    public float ThrustDelay = 0.45f;
    [Min(0f)] public float HitboxEnableDelay = 0.45f;
    [Min(0f)] public float HitboxActiveDuration = 0.05f;
    public bool EndHitboxOnImpactEvent = true;

    [Header("SE - Attack")]
    [Tooltip("突き攻撃を開始する瞬間に再生するSE。")]
    public AudioClip AttackSE;
    [Range(0f, 1f)] public float AttackSEVolume = 1f;

    [Header("Animator")]
    public string TriggerAttackPrep = "AttackPrep";
    public string TriggerAttackThrust = "AttackThrust";
}
