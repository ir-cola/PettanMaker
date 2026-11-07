using UnityEngine;

// WonderSkydome へ切り替える専用の遷移。
// 溶けるタイルを WonderSkydome のチェッカー（歪み込み）に一致させ、
// Wonder のマス目に沿ってタイル状に切り替わる。
//
// 切り替え先（Target Skydome）に WonderSkydome マテリアルを割り当てること。
// そのマテリアルの _WarpScale / _DistortStrength / _DistortSpeed / _CheckScale を
// 読み取り、ディゾルブ側の一致計算へグローバルとして渡す。
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Skydome/Skydome Blend System (To Wonder)")]
public sealed class SkydomeBlendSystem_ToWonder : SkydomeBlendSystem
{
    [Header("To-Wonder Tile Pattern")]
    [Tooltip("タイルの切り替え順のランダムさ。0=地平線から整然と, 1=バラバラに点灯。")]
    [SerializeField, Range(0f, 1f)] float tileRandom = 0.35f;

    [Header("Override (Target マテリアルに無い場合の既定値)")]
    [SerializeField] float warpScale = 3.0f;
    [SerializeField] float distortStrength = 0.15f;
    [SerializeField] float distortSpeed = 0.4f;
    [SerializeField] float checkScale = 8.0f;

    // ディゾルブ側グローバル
    static readonly int TileRandomId    = Shader.PropertyToID("_SkydomeDissolveTileRandom");
    static readonly int WarpScaleId     = Shader.PropertyToID("_SkydomeWonderWarpScale");
    static readonly int DistortStrId    = Shader.PropertyToID("_SkydomeWonderDistortStrength");
    static readonly int DistortSpeedId  = Shader.PropertyToID("_SkydomeWonderDistortSpeed");
    static readonly int CheckScaleId    = Shader.PropertyToID("_SkydomeWonderCheckScale");

    // WonderSkydome マテリアル側プロパティ
    static readonly int MatWarpScale    = Shader.PropertyToID("_WarpScale");
    static readonly int MatDistortStr   = Shader.PropertyToID("_DistortStrength");
    static readonly int MatDistortSpeed = Shader.PropertyToID("_DistortSpeed");
    static readonly int MatCheckScale   = Shader.PropertyToID("_CheckScale");

    protected override float DissolveMode => 2f;

    protected override void ApplyPatternGlobals()
    {
        // Target が Wonder マテリアルなら実値を読み取り、無ければ Inspector 既定値
        Material mat = TargetMaterial;

        Shader.SetGlobalFloat(WarpScaleId,    ReadOr(mat, MatWarpScale,    warpScale));
        Shader.SetGlobalFloat(DistortStrId,   ReadOr(mat, MatDistortStr,   distortStrength));
        Shader.SetGlobalFloat(DistortSpeedId, ReadOr(mat, MatDistortSpeed, distortSpeed));
        Shader.SetGlobalFloat(CheckScaleId,   ReadOr(mat, MatCheckScale,   checkScale));
        Shader.SetGlobalFloat(TileRandomId,   tileRandom);
    }

    static float ReadOr(Material mat, int propId, float fallback)
    {
        return (mat != null && mat.HasProperty(propId)) ? mat.GetFloat(propId) : fallback;
    }
}
