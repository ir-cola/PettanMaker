using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Custom/FaceToon の「頭ボーン基準の固定ライト方向」を駆動するコンポーネント。
/// 頭ボーンの forward/right/up（ワールド空間）を毎フレーム MaterialPropertyBlock で
/// 対象 Renderer に渡す。シェーダー側は _FaceLightDir（頭ローカル方向）をこの基底で
/// ワールド空間に組み立てるため、キャラが振り向いても顔の陰影が頭に追従して安定する。
///
/// ToonFaceTextureController / ToonDamageFlash と同じ Renderer に同居してもよい
/// （各コンポーネントが GetPropertyBlock → 自分のキーだけ更新 → SetPropertyBlock で合成する）。
/// </summary>
[ExecuteAlways]
public class ToonFaceLightController : MonoBehaviour
{
    private static readonly int FaceForwardId = Shader.PropertyToID("_FaceForward");
    private static readonly int FaceRightId   = Shader.PropertyToID("_FaceRight");
    private static readonly int FaceUpId       = Shader.PropertyToID("_FaceUp");

    [Tooltip("顔のライト基準にする頭ボーン。未設定ならこの Transform を使う")]
    [SerializeField] private Transform headBone;

    [Tooltip("_FaceForward/Right/Up を書き込む対象。未設定なら子の Renderer を自動収集")]
    [SerializeField] private List<Renderer> targetRenderers = new();

    private MaterialPropertyBlock propertyBlock;

    private void Awake() => EnsureSetup();

    private void OnValidate()
    {
        if (!gameObject.scene.IsValid()) return;
        EnsureSetup();
        Apply();
    }

    private void EnsureSetup()
    {
        propertyBlock ??= new MaterialPropertyBlock();
        if (headBone == null) headBone = transform;
        if (targetRenderers == null) targetRenderers = new List<Renderer>();
        if (targetRenderers.Count == 0) targetRenderers.AddRange(GetComponentsInChildren<Renderer>());
    }

    // アニメーションでボーンが最終姿勢に更新された後に読み取るため LateUpdate。
    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (headBone == null || targetRenderers == null)
            return;

        // シェーダーは float3 として読むが、頭ローカル方向との内積基底に使うだけなので
        // w は無視される。念のため方向ベクトルとして正規化して渡す。
        Vector3 forward = headBone.forward;
        Vector3 right   = headBone.right;
        Vector3 up      = headBone.up;

        foreach (var renderer in targetRenderers)
        {
            if (renderer == null) continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(FaceForwardId, forward);
            propertyBlock.SetVector(FaceRightId, right);
            propertyBlock.SetVector(FaceUpId, up);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
