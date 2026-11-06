/**===========================================================================

 File   : 自動LOD/オクルージョン設定ツール [AutoLODTool.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 ステージ最適化の一括設定メニュー。
 ・Auto LOD    : 小物の MeshRenderer にサイズ依存の距離カリング LODGroup を付与。
 ・オクルージョン : 動かない地形に静的フラグを立てて Umbra をベイク。

===========================================================================**/

using UnityEditor;
using UnityEngine;

/**===========================================================================
  自動LOD/オクルージョン設定ツール
===========================================================================**/
public static class AutoLODTool {

    // LOD 付与の対象 (小物・装飾)。地面ブロック (Stage/Groud) は
    // 消えると地形に穴が見えるため対象外。
    static readonly string[] LodRoots = {
        "Stage/Fence", "Stage/Enviroment", "Stage/Props", "Stage/Background Mountains",
    };

    // オクルージョンの静的フラグ対象 (動かない地形・遮蔽物)
    static readonly string[] OcclusionRoots = {
        "Map", "Stage/Groud", "Stage/Fence", "Stage/Background Mountains",
        "Stage/Props", "Stage/Enviroment",
    };

    // ==========================================================================
    //  Auto LOD
    // ==========================================================================
    [MenuItem("PettanMaker/Optimization/Auto LOD を設定 (小物の距離カリング)")]
    public static void SetupAutoLod() {
        int added = 0, skipBig = 0, skipExisting = 0;

        foreach (string path in LodRoots) {
            GameObject root = GameObject.Find(path);
            if (root == null) { Debug.LogWarning($"[AutoLOD] {path} が見つかりません"); continue; }

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) {
                if (mr.GetComponentInParent<LODGroup>() != null) { skipExisting++; continue; }

                // 道標など「遠くでも見えてほしい」目印は対象外
                if (mr.gameObject.name.ToLowerInvariant().Contains("arrow")) continue;

                // サイズに応じて「画面高さの何%を下回ったら消すか」を決める
                float size = mr.bounds.size.magnitude;
                float cull = size < 3f ? 0.008f
                           : size < 8f ? 0.004f
                           : size < 15f ? 0.002f
                           : 0f;                       // 大物 (山・崖) は消さない
                if (cull <= 0f) { skipBig++; continue; }

                GameObject go = mr.gameObject;
                var lg = Undo.AddComponent<LODGroup>(go);
                lg.SetLODs(new[] { new LOD(cull, new Renderer[] { mr }) });
                lg.RecalculateBounds();
                Undo.AddComponent<AutoLODMarker>(go);
                added++;
            }
        }

        MarkSceneDirty();
        Debug.Log($"[AutoLOD] 追加 {added} 件 / 既存LODスキップ {skipExisting} 件 / 大物スキップ {skipBig} 件");
    }

    [MenuItem("PettanMaker/Optimization/Auto LOD を解除")]
    public static void RemoveAutoLod() {
        int removed = 0;
        foreach (var marker in Object.FindObjectsOfType<AutoLODMarker>(true)) {
            var lg = marker.GetComponent<LODGroup>();
            if (lg != null) Undo.DestroyObjectImmediate(lg);
            Undo.DestroyObjectImmediate(marker);
            removed++;
        }
        MarkSceneDirty();
        Debug.Log($"[AutoLOD] 解除 {removed} 件");
    }

    // ==========================================================================
    //  オクルージョンカリング
    // ==========================================================================
    [MenuItem("PettanMaker/Optimization/静的フラグ+オクルージョンベイク")]
    public static void MarkStaticAndBakeOcclusion() {
        int marked = 0;
        foreach (string path in OcclusionRoots) {
            GameObject root = GameObject.Find(path);
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
                GameObject go = r.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                flags |= StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                marked++;
            }
        }

        // 段差地形 (約2m) が遮蔽物として効くようにベイク粒度を細かめに
        StaticOcclusionCulling.smallestOccluder = 2f;
        StaticOcclusionCulling.smallestHole = 0.25f;
        StaticOcclusionCulling.backfaceThreshold = 100f;
        StaticOcclusionCulling.Compute();

        MarkSceneDirty();
        Debug.Log($"[AutoLOD] 静的フラグ {marked} 件 → オクルージョンベイク完了 " +
                  $"(データ {StaticOcclusionCulling.umbraDataSize / 1024} KB)");
    }

    [MenuItem("PettanMaker/Optimization/オクルージョンベイクを削除")]
    public static void ClearOcclusion() {
        StaticOcclusionCulling.Clear();
        Debug.Log("[AutoLOD] オクルージョンベイクを削除しました");
    }

    static void MarkSceneDirty() {
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
    }
}
