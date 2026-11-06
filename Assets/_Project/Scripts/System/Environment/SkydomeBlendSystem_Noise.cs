using UnityEngine;

// 地平線→天頂へ有機ノイズで溶けるスカイドーム遷移。
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Skydome/Skydome Blend System (Noise)")]
public sealed class SkydomeBlendSystem_Noise : SkydomeBlendSystem
{
    [Header("Noise Pattern")]
    [Tooltip("溶け模様の有機ノイズのスケール。大きいほど細かい。")]
    [SerializeField, Range(0.5f, 8f)] float noiseScale = 3.0f;

    static readonly int NoiseScaleId = Shader.PropertyToID("_SkydomeDissolveScale");

    protected override float DissolveMode => 0f;

    protected override void ApplyPatternGlobals()
    {
        Shader.SetGlobalFloat(NoiseScaleId, noiseScale);
    }
}
