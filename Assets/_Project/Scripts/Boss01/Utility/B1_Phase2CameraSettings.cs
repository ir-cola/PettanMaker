using UnityEngine;

/// <summary>
/// Phase2 突入カメラ設定。フォグとPhase2専用Roar衝撃波、Roar時のカメラシェイクを持つ。
/// カメラの移動は B1_Phase2.playable(Timeline)側で作る。
/// </summary>
[CreateAssetMenu(fileName = "B1_CameraSettings_Phase2", menuName = "Boss01/Camera Settings/Phase2")]
public sealed class B1_Phase2CameraSettings : B1_CameraSceneSettings
{
    [Header("Phase2")]
    [Tooltip("注視の基準高さ")]
    [SerializeField] private float lookHeight = 2.2f;
    [Tooltip("Phase2 Cinema終了後、最初の行動を始めるまでその場で待機する時間（秒）。0で即行動")]
    [SerializeField, Min(0f)] private float postCinemaIdleDuration = 1f;
    [SerializeField] private FogSettings fog = FogSettings.For(B1_CameraSceneKind.Phase2);
    [SerializeField] private ShockwaveSettings shockwave = new();
    [SerializeField] private CameraShakeSettings roarShake = new();
    [SerializeField] private ColorDrainSettings colorDrain = new();

    [Header("Phase2 Roar Player Knockback")]
    [Tooltip("ONでRoar時にプレイヤーをボスから離れる方向へ吹き飛ばし、KnockBack_Groundを経由してRecoverさせる")]
    [SerializeField] private bool roarKnockbackPlayer = true;
    [Tooltip("Roarでプレイヤーを後方へ押し出す距離")]
    [SerializeField, Min(0f)] private float roarKnockbackDistance = 6f;
    [Tooltip("Roarノックバックの総時間（秒）。倒れ込み→起き上がりまでを含む。0以下で通常のGround+Recover時間を使用")]
    [SerializeField, Min(0f)] private float roarKnockbackDuration = 1.2f;

    public override B1_CameraSceneKind SceneKind => B1_CameraSceneKind.Phase2;
    public override float LookHeight => lookHeight;
    public float PostCinemaIdleDuration => Mathf.Max(0f, postCinemaIdleDuration);
    public override FogSettings Fog => fog;
    public override ShockwaveSettings Shockwave => shockwave;
    public override CameraShakeSettings RoarShake => roarShake;
    public override ColorDrainSettings ColorDrain => colorDrain;
    public bool RoarKnockbackPlayer => roarKnockbackPlayer;
    public float RoarKnockbackDistance => Mathf.Max(0f, roarKnockbackDistance);
    public float RoarKnockbackDuration => Mathf.Max(0f, roarKnockbackDuration);
}
