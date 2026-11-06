using UnityEngine;

[CreateAssetMenu(fileName = "Enemy04Settings", menuName = "Enemies/Enemy04 Settings")]
public class Enemy04Settings : EnemySettings
{
    [Header("追跡・間合い")]
    public float ChargeTriggerRange = 5.0f;
    public float ChaseSpeed = 3.0f;

    [Header("徘徊（未発見時）")]
    public float WanderSpeed = 1.2f;
    public float IdleHoverDuration = 2.0f;
    public float IdleWanderDuration = 2.5f;
    public float WanderRadius = 4.0f;

    [Header("突進")]
    public float WindupDuration = 0.5f;
    [Tooltip("タメ中に後ろへ引く距離（m）。アンチシペーション。0で無効。")]
    public float WindupPullback = 0.6f;
    [Tooltip("タメの潰し量（0〜0.5）。横に膨らみ縦に縮む。突進開始時は逆に伸びる。")]
    [Range(0f, 0.5f)] public float WindupSquash = 0.18f;
    public float DashSpeed = 11.0f;
    public float DashDistance = 7.0f;
    [Tooltip("ダッシュのイーズアウト指数。1で等速、大きいほど出だしが鋭く後半減速。")]
    public float DashEase = 2.0f;
    public float HitRadius = 1.2f;
    public float RecoverDuration = 0.4f;
    public float StunDuration = 1.5f;
    public float RecoverShakeDuration = 0.6f;
    public float AttackCooldown = 0.5f;
    public float PostChargeDisengageDuration = 1.0f;

    [Header("SE - 攻撃")]
    [Tooltip("突進攻撃のタメを開始した瞬間に再生するSE。")]
    public AudioClip AttackSE;
    [Range(0f, 1f)] public float AttackSEVolume = 1f;

    [Header("Animator トリガー")]
    public string TriggerAttack = "Attack";
    public string ParamIsDown = "isDown";
}
