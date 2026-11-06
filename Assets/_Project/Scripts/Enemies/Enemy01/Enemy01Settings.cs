using UnityEngine;

[CreateAssetMenu(fileName = "Enemy01Settings", menuName = "Enemies/Enemy01 Settings")]
public class Enemy01Settings : EnemySettings
{
    [Header("間合い")]
    public float ThrowRange = 8.0f;
    public float KeepDistanceMin = 5.0f;

    [Header("移動")]
    public float RunSpeed = 3.5f;
    public float WalkSpeed = 1.6f;
    public float IdleSwapInterval = 2.5f;

    [Header("投擲")]
    public GameObject[] ProjectilePrefabs;
    public float ProjectileSpeed = 12.0f;        // 弾道計算できない場合の直線フォールバック初速
    public float AimHeightOffset = 1.0f;
    public float LaunchAngle = 45.0f;            // 放物線の打ち上げ角度(度)
    public float ProjectileSpin = 180.0f;        // 飛行中にかける角速度(度/秒)

    [Header("SE - Projectile")]
    [Tooltip("Projectileを投げた瞬間に再生するSE。")]
    public AudioClip ProjectileThrowSE;
    [Range(0f, 1f)] public float ProjectileThrowSEVolume = 1f;
    [Tooltip("ProjectileがPlayerに命中した瞬間に再生するSE。")]
    public AudioClip ProjectileHitSE;
    [Range(0f, 1f)] public float ProjectileHitSEVolume = 1f;

    [Header("投擲タイミング（Enemy01_Attack アニメのフレーム基準）")]
    public float AttackClipFPS = 30f;            // Enemy01_Attack のサンプルレート
    public int CarryEndFrame = 16;               // バッグ→耳へ運び終えるフレーム（開始=Frame0）
    public int ThrowFrame = 25;                  // 投擲（放す）フレーム

    [Header("運搬の弧（バッグ→耳。直線だとモデルを貫通するので弧を描く）")]
    public float CarryArcHeight = 0.4f;          // 弧の高さ（上方向の膨らみ）
    public float CarryArcOutward = 0.5f;         // 体の外側への膨らみ（負で逆側）

    [Header("攻撃タイミング")]
    public float FindingDuration = 0.6f;
    public float WindupDuration = 0.35f;
    public float ThrowRecovery = 0.3f;
    public float AttackCooldown = 1.5f;

    [Header("Animator")]
    public string ParamIdleIndex = "IdleIndex";
    public string TriggerFinding = "TriggerFinding";
    public string TriggerAttack0 = "TriggerAttack0";
    public string BoolIsAttacking = "IsAttacking";
}
