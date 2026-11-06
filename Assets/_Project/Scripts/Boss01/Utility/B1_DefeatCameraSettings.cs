using UnityEngine;

/// <summary>
/// 撃破カメラ設定。フォグのみ(タイトルUI/衝撃波/登場ディゾルブは持たない)。
/// カメラの移動は B1_Defeat.playable(Timeline)側で作る。
/// </summary>
[CreateAssetMenu(fileName = "B1_CameraSettings_Defeat", menuName = "Boss01/Camera Settings/Defeat")]
public sealed class B1_DefeatCameraSettings : B1_CameraSceneSettings
{
    [Header("Defeat")]
    [Tooltip("注視の基準高さ")]
    [SerializeField] private float lookHeight = 1.8f;
    [SerializeField] private FogSettings fog = FogSettings.For(B1_CameraSceneKind.Defeat);

    public override B1_CameraSceneKind SceneKind => B1_CameraSceneKind.Defeat;
    public override float LookHeight => lookHeight;
    public override FogSettings Fog => fog;
}
