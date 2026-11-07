using UnityEngine;

// 格子状のタイルが順に切り替わるスカイドーム遷移。
// 地平線側のタイルから（tileRandom で散らしつつ）順に、燃える目地を伴って切り替わる。
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Skydome/Skydome Blend System (Tile)")]
public sealed class SkydomeBlendSystem_Tile : SkydomeBlendSystem
{
    [Header("Tile Pattern")]
    [Tooltip("縦方向のタイル数（横は約2倍）。大きいほど細かい格子。")]
    [SerializeField, Range(2, 40)] int tileCount = 12;

    [Tooltip("タイルの切り替え順のランダムさ。0=地平線から整然と, 1=バラバラに点灯。")]
    [SerializeField, Range(0f, 1f)] float tileRandom = 0.4f;

    static readonly int TilesId      = Shader.PropertyToID("_SkydomeDissolveTiles");
    static readonly int TileRandomId = Shader.PropertyToID("_SkydomeDissolveTileRandom");

    protected override float DissolveMode => 1f;

    protected override void ApplyPatternGlobals()
    {
        Shader.SetGlobalFloat(TilesId, tileCount);
        Shader.SetGlobalFloat(TileRandomId, tileRandom);
    }
}
