/**===========================================================================

 File   : プレイヤーシール貼付コンポーネント [PlayerPasteManager.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 TABでシール帳を開いたとき、左パネルの RawImage に
 実際のシーンのプレイヤーを映す。
 マウスドラッグでカメラをプレイヤーの周りに周回できる。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerPasteManager : MonoBehaviour {

    [Header("シール設定")]
    [SerializeField] private float stickerSize = 0.1f;
    [SerializeField] private float surfaceOffset = 0.002f;  // 小さいほど表面に密着
    [SerializeField] private int gridResolution = 10;
    [SerializeField] private int maxStickers = 20;

    [Tooltip("URP/Unlit (Transparent) のマテリアルをアサイン（テクスチャは実行時に差し替わる）")]
    [SerializeField] private Material stickerMatTemplate;

    // 貼り付けが成功した瞬間に呼ばれる（アイテム効果発動用）
    public UnityEvent<StickerData> onStickerPasted;

    [Header("SE")]
    [Tooltip("シールを貼った瞬間に鳴らすSE。")]
    [SerializeField] private AudioClip pasteSE;
    [Range(0f, 1f)]
    [SerializeField] private float pasteSEVolume = 1f;

    [Header("貼り付け有効判定")]
    [Tooltip("支配ボーンとヒット点がこの距離以内なら貼り付け有効（緑）")]
    [SerializeField] private float pasteValidRadius = 0.25f;

    [Header("メッシュ参照")]
    [Tooltip("モデルの SkinnedMeshRenderer を直接ここにアサイン（空の場合は子から自動検索）")]
    [SerializeField] private SkinnedMeshRenderer[] skinnedRenderersOverride;

    private SkinnedMeshRenderer[] skinnedRenderers;
    private readonly List<GameObject> activeStickers = new List<GameObject>();

    private void Awake() {
        RefreshRenderers();
    }

    private void Start() {
        // Awake で見つからなかった場合に Start で再試行（FBX 子階層の初期化タイミング対策）
        if (skinnedRenderers == null || skinnedRenderers.Length == 0)
            RefreshRenderers();
    }

    /// <summary>SkinnedMeshRenderer を再検索する。モデル切り替え後にも呼べる。</summary>
    public void RefreshRenderers() {
        // 1. Inspector で明示的にセットされていればそれを使う
        if (skinnedRenderersOverride != null && skinnedRenderersOverride.Length > 0) {
            skinnedRenderers = skinnedRenderersOverride;
            return;
        }
        // 2. 自身の子階層を検索（通常ケース）
        skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
        if (skinnedRenderers.Length > 0) return;

        // 3. 自身がモデルの子である場合
        skinnedRenderers = GetComponentsInParent<SkinnedMeshRenderer>(includeInactive: true);
        if (skinnedRenderers.Length > 0) return;

        Debug.LogWarning("[PSM] SkinnedMeshRenderer が見つかりません。" +
            "Player Prefab の PlayerPasteManager に 'Skinned Renderers Override' をアサインしてください。");
    }

    /// <summary>
    /// ドラッグ中に呼ぶ。レイがモデルに当たっているか・支配ボーンに近いかを返す。
    /// onModel=false → ビューア外（判定不要）
    /// onModel=true, valid=true → 緑（貼れる）
    /// onModel=true, valid=false → 赤（ボーンが遠すぎ）
    /// </summary>
    public bool QueryPasteValidity(Ray cameraRay, out bool onModel) {
        onModel = false;
        if (skinnedRenderers == null || skinnedRenderers.Length == 0) RefreshRenderers();
        if (skinnedRenderers == null || skinnedRenderers.Length == 0) return false;

        // 最近傍ヒット点を探す
        float bestDist = float.MaxValue;
        Vector3 hitPoint = Vector3.zero;
        SkinnedMeshRenderer hitSMR = null;

        foreach (var smr in skinnedRenderers) {
            var baked = new Mesh();
            smr.BakeMesh(baked);
            BakeMeshToWorld(baked, smr.transform);
            var tmp = new GameObject("_ValidTemp");
            tmp.SetActive(false);
            var col = tmp.AddComponent<MeshCollider>();
            col.sharedMesh = baked;
            tmp.SetActive(true);
            if (col.Raycast(cameraRay, out RaycastHit rh, 50f) && rh.distance < bestDist) {
                bestDist = rh.distance; hitPoint = rh.point; hitSMR = smr; onModel = true;
            }
            Destroy(tmp); Destroy(baked);
        }

        if (!onModel) return false;

        // 支配ボーンとの距離チェック
        float minBoneDist = float.MaxValue;
        foreach (var smr in skinnedRenderers) {
            foreach (var bone in smr.bones) {
                if (bone == null) continue;
                float d = (bone.position - hitPoint).magnitude;
                if (d < minBoneDist) minBoneDist = d;
            }
        }

        return minBoneDist <= pasteValidRadius;
    }

    public void PlaceSticker(Ray cameraRay, StickerData data) {
        if (data == null || data.decalTexture == null) {
            Debug.LogWarning("[PSM] texture が未設定です。"); return;
        }
        if (skinnedRenderers == null || skinnedRenderers.Length == 0) RefreshRenderers();
        if (skinnedRenderers == null || skinnedRenderers.Length == 0) {
            Debug.LogWarning("[PSM] SMR が見つかりません。PlayerPasteManager の 'Skinned Renderers Override' にプレイヤーモデルの SkinnedMeshRenderer をアサインしてください。"); return;
        }

        bool hasHit = false;
        float bestDist = float.MaxValue;
        RaycastHit bestHit = default;
        SkinnedMeshRenderer hitSMR = null;

        foreach (var smr in skinnedRenderers) {
            var baked = new Mesh();
            smr.BakeMesh(baked);
            BakeMeshToWorld(baked, smr.transform);
            var tmp = new GameObject("_RayTemp");
            tmp.SetActive(false);
            var c = tmp.AddComponent<MeshCollider>();
            c.sharedMesh = baked;
            tmp.SetActive(true);
            if (c.Raycast(cameraRay, out RaycastHit rh, 50f) && rh.distance < bestDist) {
                bestDist = rh.distance; bestHit = rh; hitSMR = smr; hasHit = true;
            }
            Destroy(tmp); Destroy(baked);
        }

        if (!hasHit) { Debug.Log("[PSM] メッシュにヒットしませんでした。"); return; }
        SpawnSticker(bestHit.point, bestHit.normal, hitSMR, data);
    }

    private void SpawnSticker(Vector3 hitPoint, Vector3 hitNormal,
                              SkinnedMeshRenderer smr, StickerData data) {
        if (activeStickers.Count >= maxStickers) {
            Destroy(activeStickers[0]); activeStickers.RemoveAt(0);
        }

        // つなぎ目対策：全 SMR を結合した1つのコライダーで投影する
        var bakedP = BuildCombinedBakedMesh(skinnedRenderers);
        var projTmp = new GameObject("_ProjTemp");
        projTmp.SetActive(false);
        var projCol = projTmp.AddComponent<MeshCollider>();
        projCol.sharedMesh = bakedP;
        projTmp.SetActive(true);

        // スキニングと全く同じ計算で追従するため、最近傍頂点の全ボーンウェイトを取得
        var go = new GameObject("Sticker_" + data.stickerName);
        go.transform.SetParent(this.transform, worldPositionStays: false);
        go.transform.position = hitPoint;
        go.transform.rotation = Quaternion.LookRotation(-hitNormal, Vector3.up);
        go.transform.localScale = Vector3.one;

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = BuildConformedMesh(hitPoint, hitNormal, stickerSize,
                                     surfaceOffset, gridResolution, projCol, go.transform);

        Destroy(projTmp); Destroy(bakedP);

        // 全ウェイトブレンド追従（全SMR横断で最近傍頂点を探す）
        var attach = go.AddComponent<StickerAttachment>();
        attach.Initialize(hitPoint, hitNormal, skinnedRenderers);

        if (stickerMatTemplate == null) {
            Debug.LogError("[PSM] stickerMatTemplate が未アサインです。Inspector からアサインしてください。");
            Destroy(go); return;
        }

        var mr = go.AddComponent<MeshRenderer>();
        // テンプレートの全設定（透過・キーワード等）を完全コピーしてテクスチャのみ差し替え
        var mat = Instantiate(stickerMatTemplate);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", data.decalTexture);
        else if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", data.decalTexture);
        else if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", data.decalTexture);
        mat.mainTexture = data.decalTexture;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        activeStickers.Add(go);
        Debug.Log("[PSM] シール貼付：" + data.stickerName);

        // 貼り付け成功を通知（アイテム効果発動など）
        onStickerPasted?.Invoke(data);

        if (pasteSE != null)
            AudioSource.PlayClipAtPoint(pasteSE, transform.position, pasteSEVolume);
    }

    private static Mesh BuildCombinedBakedMesh(SkinnedMeshRenderer[] renderers) {
        var combines = new CombineInstance[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) {
            var baked = new Mesh();
            renderers[i].BakeMesh(baked);
            BakeMeshToWorld(baked, renderers[i].transform);
            combines[i] = new CombineInstance { mesh = baked, transform = Matrix4x4.identity };
        }
        var combined = new Mesh();
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combined.CombineMeshes(combines, mergeSubMeshes: true, useMatrices: false);
        foreach (var ci in combines) Destroy(ci.mesh);
        return combined;
    }

    private static Mesh BuildConformedMesh(
            Vector3 hitPoint, Vector3 hitNormal, float size, float offset,
            int n, MeshCollider col, Transform localRoot) {
        Vector3 worldUp = Mathf.Abs(Vector3.Dot(hitNormal, Vector3.up)) < 0.99f
                        ? Vector3.up : Vector3.forward;
        Vector3 right = Vector3.Cross(hitNormal, worldUp).normalized;
        Vector3 up = Vector3.Cross(right, hitNormal).normalized;

        int stride = n + 1;
        var verts = new Vector3[stride * stride];
        var uvs = new Vector2[stride * stride];
        var validHit = new bool[stride * stride];

        // stickerSize に比例させることで細い部分でもレイが突き抜けない
        float rayBack = size * 0.8f;
        float rayDist = size * 2.0f;
        // 急傾斜の裏面ヒットを弾くための法線一致しきい値（cos 50° ≒ 0.64）
        const float normalDotThreshold = 0.64f;
        float maxDev = size * 0.8f;

        var worldVerts = new Vector3[stride * stride];

        for (int row = 0; row <= n; row++) {
            for (int c = 0; c <= n; c++) {
                float u = (float)c / n;
                float v = (float)row / n;
                Vector3 tp = hitPoint + right * (u - 0.5f) * size + up * (v - 0.5f) * size;
                Vector3 worldPos;
                bool hit = false;

                Vector3 origin = tp + hitNormal * rayBack;
                if (col.Raycast(new Ray(origin, -hitNormal), out RaycastHit rh, rayDist)
                    && Vector3.Dot(rh.normal, hitNormal) >= normalDotThreshold) {
                    worldPos = rh.point + rh.normal * offset;
                    hit = true;
                }
                else if (col.Raycast(new Ray(tp - hitNormal * rayBack, hitNormal), out RaycastHit rh2, rayDist)
                         && Vector3.Dot(rh2.normal, hitNormal) >= normalDotThreshold) {
                    worldPos = rh2.point + rh2.normal * offset;
                    hit = true;
                }
                else {
                    worldPos = tp + hitNormal * offset;
                }

                if (hit && (worldPos - hitPoint).sqrMagnitude > maxDev * maxDev) {
                    hit = false;
                    worldPos = tp + hitNormal * offset;
                }

                int idx = row * stride + c;
                worldVerts[idx] = worldPos;
                validHit[idx] = hit;
                uvs[idx] = new Vector2(u, v);
            }
        }

        // 無効頂点を隣接有効頂点の平均で補間（収束するまで最大 n 回繰り返す）
        for (int pass = 0; pass < n; pass++) {
            bool anyFixed = false;
            for (int row = 0; row <= n; row++) {
                for (int c = 0; c <= n; c++) {
                    int idx = row * stride + c;
                    if (validHit[idx]) continue;
                    Vector3 sum = Vector3.zero;
                    int cnt = 0;
                    if (row > 0 && validHit[(row - 1) * stride + c]) { sum += worldVerts[(row - 1) * stride + c]; cnt++; }
                    if (row < n && validHit[(row + 1) * stride + c]) { sum += worldVerts[(row + 1) * stride + c]; cnt++; }
                    if (c > 0 && validHit[row * stride + (c - 1)]) { sum += worldVerts[row * stride + (c - 1)]; cnt++; }
                    if (c < n && validHit[row * stride + (c + 1)]) { sum += worldVerts[row * stride + (c + 1)]; cnt++; }
                    if (cnt > 0) { worldVerts[idx] = sum / cnt; validHit[idx] = true; anyFixed = true; }
                }
            }
            if (!anyFixed) break;
        }

        for (int i = 0; i < stride * stride; i++)
            verts[i] = localRoot.InverseTransformPoint(worldVerts[i]);

        var tris = new int[n * n * 6];
        int t = 0;
        for (int row = 0; row < n; row++) {
            for (int c = 0; c < n; c++) {
                int i = row * stride + c;
                tris[t++] = i; tris[t++] = i + stride; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + stride; tris[t++] = i + stride + 1;
            }
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void BakeMeshToWorld(Mesh mesh, Transform t) {
        var v = mesh.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = t.TransformPoint(v[i]);
        mesh.vertices = v;
        mesh.RecalculateBounds();
    }

    public void ClearAllStickers() {
        foreach (var s in activeStickers) { if (s != null) Destroy(s); }
        activeStickers.Clear();
    }
}

/// <summary>
/// スキニングと全く同じボーンウェイトブレンド計算でシールを追従させるコンポーネント。
/// 1本ボーン追従ではなく最大 4本分のウェイトを使って補間する。
/// </summary>
public class StickerAttachment : MonoBehaviour {

    private struct BoneInfluence {
        public Transform bone;
        public Vector3 localPos;     // 貼り付け時のボーンローカル位置オフセット
        public Quaternion localRot;  // 貼り付け時のボーンローカル回転オフセット
        public float weight;
    }

    private BoneInfluence[] influences;

    /// <summary>
    /// 貼り付け時に呼び出す。全SMRを横断して hitPoint 最近傍頂点のボーンウェイトを保存する。
    /// </summary>
    public void Initialize(Vector3 hitPoint, Vector3 hitNormal, SkinnedMeshRenderer[] renderers) {
        float closestDist = float.MaxValue;
        BoneWeight closestBW = default;
        Transform[] closestBones = null;

        foreach (var smr in renderers) {
            if (smr == null) continue;
            BoneWeight[] boneWeights = smr.sharedMesh.boneWeights;
            if (boneWeights == null || boneWeights.Length == 0) continue;

            var baked = new Mesh();
            smr.BakeMesh(baked);
            Vector3[] bakedVerts = baked.vertices;
            Destroy(baked);

            for (int i = 0; i < bakedVerts.Length; i++) {
                // BakeMesh は SMR ローカル空間なので transform でワールド変換
                float d = (smr.transform.TransformPoint(bakedVerts[i]) - hitPoint).sqrMagnitude;
                if (d < closestDist) {
                    closestDist = d;
                    closestBW = boneWeights[i];
                    closestBones = smr.bones;
                }
            }
        }

        if (closestBones == null) {
            influences = System.Array.Empty<BoneInfluence>();
            return;
        }

        var list = new System.Collections.Generic.List<BoneInfluence>(4);
        void TryAdd(int idx, float w) {
            if (w <= 0f || idx < 0 || idx >= closestBones.Length || closestBones[idx] == null) return;
            var b = closestBones[idx];
            list.Add(new BoneInfluence {
                bone = b,
                localPos = b.InverseTransformPoint(hitPoint),
                localRot = Quaternion.Inverse(b.rotation)
                         * Quaternion.LookRotation(-hitNormal, Vector3.up),
                weight = w
            });
        }
        TryAdd(closestBW.boneIndex0, closestBW.weight0);
        TryAdd(closestBW.boneIndex1, closestBW.weight1);
        TryAdd(closestBW.boneIndex2, closestBW.weight2);
        TryAdd(closestBW.boneIndex3, closestBW.weight3);

        if (list.Count == 0) TryAdd(closestBW.boneIndex0, 1f);

        float total = 0f;
        foreach (var inf in list) total += inf.weight;
        for (int i = 0; i < list.Count; i++) {
            var inf = list[i]; inf.weight /= total; list[i] = inf;
        }

        influences = list.ToArray();
    }

    private void LateUpdate() {
        if (influences == null || influences.Length == 0) return;

        // 位置：各ボーンのローカルオフセットをワールド変換してウェイト合成
        Vector3 pos = Vector3.zero;
        foreach (var inf in influences)
            if (inf.bone != null)
                pos += inf.bone.TransformPoint(inf.localPos) * inf.weight;

        // 回転：支配ボーン（weight 最大）で代表させる
        BoneInfluence dominant = influences[0];
        foreach (var inf in influences)
            if (inf.weight > dominant.weight) dominant = inf;
        Quaternion rot = dominant.bone != null
            ? dominant.bone.rotation * dominant.localRot
            : transform.rotation;

        transform.position = pos;
        transform.rotation = rot;
    }
}
