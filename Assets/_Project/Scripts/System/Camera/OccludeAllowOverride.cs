/**===========================================================================

 File   : オクルージョン透過のオブジェクト単位上書き [OccludeAllowOverride.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/5
 -----------------------------------------------------------------------------
 カメラオクルージョン透過 (OccludablePolka) を「このオブジェクトだけ」
 許可 / 禁止するためのマーカー。配下すべての Renderer に
 MaterialPropertyBlock で _OccludeAllow を書き込む。
 共有マテリアルの巻き添えを避けたい場合に使う (アセットは汚さない)。
============================================================================*/

using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class OccludeAllowOverride : MonoBehaviour
{
    static readonly int OccludeAllowId = Shader.PropertyToID("_OccludeAllow");

    [Tooltip("false = このオブジェクト (配下の Renderer 全て) はオクルージョン透過させない")]
    [SerializeField] bool allowOcclusion = false;

    void OnEnable()  { Apply(); }
    void OnValidate() { Apply(); }

    void Apply()
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(mpb);                       // 既存の MPB 値 (_OcclusionFade 等) を保持
            mpb.SetFloat(OccludeAllowId, allowOcclusion ? 1f : 0f);
            r.SetPropertyBlock(mpb);
        }
    }
}
