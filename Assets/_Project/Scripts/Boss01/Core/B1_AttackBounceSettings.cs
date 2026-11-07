using UnityEngine;

/// <summary>B1_AttackBounceObject 共通の物理リアクション設定。</summary>
[CreateAssetMenu(fileName = "B1_AttackBounceSettings", menuName = "Boss01/Attack Bounce Settings")]
public sealed class B1_AttackBounceSettings : ScriptableObject
{
    [Header("有効化")]
    [Tooltip("OFFにすると、すべてのBounceリアクションを無効化する。")]
    public bool Enabled = true;
    [Tooltip("TailAttackとTailRainの着地リアクションを有効化する。")]
    public bool EnableTailLanding = true;
    [Tooltip("Rush通過時の跳ね上がりを有効化する。")]
    public bool EnableRush = true;
    [Tooltip("咆哮時のXZ方向リアクションを有効化する。")]
    public bool EnableRoar = true;

    [Header("物理設定")]
    [Tooltip("Colliderが無い場合、対象へBoxColliderを自動追加する。")]
    public bool AddBoxColliderWhenMissing = true;

    [Header("TailAttack / TailRain")]
    [Min(0f)] public float UpwardVelocity = 8f;
    [Min(0f)] public float OutwardVelocity = 2f;
    [Min(0f)] public float RandomTorque = 8f;
    [Range(0f, 1f)] public float StrengthAtRadiusEdge = 0.35f;

    [Header("Rush")]
    [Min(0f)] public float RushUpwardVelocity = 3.5f;
    [Min(0f)] public float RushOutwardVelocity = 5f;
    [Min(0f)] public float RushRandomTorque = 6f;

    [Header("咆哮（XZ方向）")]
    [Min(0f)] public float RoarHorizontalVelocity = 6f;
    [Tooltip("外向き方向へ加えるランダムな横ブレ。0なら中心から真っ直ぐ外へ動く。")]
    [Range(0f, 1f)] public float RoarDirectionRandomness = 0.25f;
    [Min(0f)] public float RoarYawTorque = 4f;
    [Tooltip("咆哮で物理化してXZ方向へ動かす時間。")]
    [Min(0.02f)] public float RoarMoveDuration = 0.3f;

    [Header("着地後")]
    [Tooltip("反応後、開始時のRigidbody設定（Is Kinematicなど）へ戻す。")]
    public bool RestoreRigidbodyState = true;
    [Tooltip("反応後、位置と回転もシーン開始時へ戻す。OFFなら移動先で静止する。")]
    public bool RestoreStartPose = true;
    [Min(0f)] public float MinimumFlightTime = 0.15f;
    [Min(0.01f)] public float GroundProbeDistance = 0.12f;
    [Min(0.1f)] public float MaximumFlightTime = 5f;
}
