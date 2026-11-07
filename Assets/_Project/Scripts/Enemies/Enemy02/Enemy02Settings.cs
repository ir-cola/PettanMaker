using UnityEngine;

[CreateAssetMenu(fileName = "Enemy02Settings", menuName = "Enemies/Enemy02 Settings")]
public class Enemy02Settings : EnemySettings
{
    [Header("Idle")]
    public float IdleSwapInterval = 2.5f;

    [Header("逃走")]
    public float FleeSpeedMultiplier = 1.6f;
    public float FleeStep = 6.0f;
    public float FleeRepathInterval = 0.4f;
    public float FleeWanderAngle = 55f;

    [Header("SE - Walk")]
    public AudioClip WalkSE;
    [Tooltip("設定すると歩行SEをこの配列からランダムに選ぶ（空なら WalkSE を使用）。")]
    public AudioClip[] WalkSEVariations;
    [Range(0f, 1f)] public float WalkSEVolume = 1f;
    [Tooltip("移動中にWalk SEを鳴らす間隔（秒）。")]
    [Min(0.05f)] public float WalkSEInterval = 0.45f;
    [Tooltip("Walk SEの先頭から再生する長さ（秒）。")]
    [Min(0.01f)] public float WalkSEPlaybackDuration = 0.3f;

    [Header("Animator")]
    public string ParamIdleIndex = "IdleIndex";
}
