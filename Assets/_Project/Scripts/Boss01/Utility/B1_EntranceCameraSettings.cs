using UnityEngine;

/// <summary>
/// 登場カメラ設定。フル(フォグ + タイトルUI + 衝撃波 + 登場ディゾルブ)。
/// カメラの移動・ビート(Appear/Roar/Title)の発火タイミングは B1_Entrance.playable
/// (Timeline)側で作る。ここは B1_CameraTimelineBridge が Signal 発火時に読む「見た目」の設定のみ。
/// </summary>
[CreateAssetMenu(fileName = "B1_CameraSettings_Entrance", menuName = "Boss01/Camera Settings/Entrance")]
public sealed class B1_EntranceCameraSettings : B1_CameraSceneSettings
{
    [Header("Entrance")]
    [Tooltip("注視/出現の基準高さ(ボス基準 transform からの上オフセット)")]
    [SerializeField] private float lookHeight = 2.0f;
    [SerializeField] private FogSettings fog = FogSettings.For(B1_CameraSceneKind.Entrance);
    [SerializeField] private UISettings ui = UISettings.For(B1_CameraSceneKind.Entrance);
    [Header("Entrance Orbit Letterbox")]
    [SerializeField] private LetterboxSettings letterbox = new();
    [SerializeField] private ShockwaveSettings shockwave = new();
    [SerializeField] private CameraShakeSettings roarShake = new();
    [SerializeField] private RoarImpactSettings roarImpact = new();
    [SerializeField] private ColorDrainSettings colorDrain = new() { enabled = false };
    [SerializeField] private RevealSettings reveal = new();

    public override B1_CameraSceneKind SceneKind => B1_CameraSceneKind.Entrance;
    public override float LookHeight => lookHeight;
    public override FogSettings Fog => fog;
    public override UISettings UI => ui;
    public override LetterboxSettings Letterbox => letterbox;
    public override ShockwaveSettings Shockwave => shockwave;
    public override CameraShakeSettings RoarShake => roarShake;
    public override RoarImpactSettings RoarImpact => roarImpact;
    public override ColorDrainSettings ColorDrain => colorDrain;
    public override RevealSettings Reveal => reveal;
}
