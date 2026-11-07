/**===========================================================================

 File   : 髪メッシュのボーン追従 [HairBoneFollower.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/17
 -----------------------------------------------------------------------------
 髪グループはスケルトンの外 (モデルのルート直下) にあり、アニメーションに
 追従しない。Player_Model に付けると起動時に髪グループを Head ボーン配下へ
 移し替え、頭の動きに剛体追従させる。
 ※ 恒久対応は DCC 側で髪を Head にスキンし直すこと。本コンポーネントは
   モデル再インポートでも壊れないランタイム回避策。
 ===========================================================================**/

using UnityEngine;

[DisallowMultipleComponent]
public class HairBoneFollower : MonoBehaviour {

    [Tooltip("追従させたい髪グループの名前 (Player_Model 配下から再帰検索)")]
    [SerializeField] private string hairGroupName = "character01_rescale_clean3:group5";

    [Tooltip("追従先 Head ボーンへの相対パス (このオブジェクトからの transform.Find)")]
    [SerializeField] private string headBonePath = "Root/Hips/Spine1/Spine2/Spine3/Neck/Head";

    private void Awake() {
        Transform head = transform.Find(headBonePath);
        if (head == null) head = FindDeep(transform, "Head"); // パスが変わっていても名前で救済
        if (head == null) {
            Debug.LogWarning($"[HairBoneFollower] Head ボーンが見つかりません (path='{headBonePath}')");
            return;
        }

        Transform hair = FindDeep(transform, hairGroupName);
        if (hair == null) {
            Debug.LogWarning($"[HairBoneFollower] 髪グループ '{hairGroupName}' が見つかりません");
            return;
        }

        if (hair.parent == head) return; // 既に追従済み
        hair.SetParent(head, worldPositionStays: true); // 見た目の位置を保ったまま頭に追従
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
