/*=============================================================================

 File   : [B2_WandTrailVFX.cs]
 Desc   : 女王の杖のぶん回しトレイル。既存の VFX_Queen_Trail_001 プレハブ
          (TrailRenderer 付き)を杖先に常駐させ、振っている間だけ emitting する。

          VFX_Boss_Queen_Readme.txt の
          「女王の杖ぶん回し時トレイル。出したい位置(杖の先など)の Locate 位置を
            読み込むスクリプトを作るか、杖が Locate 情報を持ってるならその子に…」
          に対応する接続コード。

          ■ なぜ毎回 Instantiate しないのか
            TrailRenderer は「軌跡」なので、振り始めに生成すると原点や前回位置から
            線が走る。生成は一度だけにして杖先に親付けし、SetTrail(true/false) で
            emitting を切り替える。ON にする瞬間に Clear() して前回の軌跡も消す。

          セットアップ(Inspector):
            - Trail Prefab : VFX_Queen_Trail_001
            - Tip Point    : 杖先の空オブジェクト(WandModel の子に作る)
                             未設定ならこの Transform 自身を使う

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using UnityEngine;

namespace Boss02
{
    /// <summary>杖先のトレイルを SetTrail(true/false) で出し入れする。</summary>
    public class B2_WandTrailVFX : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("トレイルのプレハブ(VFX_Queen_Trail_001)。未設定なら何も出ない")]
        [SerializeField] private GameObject trailPrefab;
        [Tooltip("トレイルを出す位置(杖先)。未設定ならこの Transform 自身")]
        [SerializeField] private Transform tipPoint;

        private TrailRenderer trail;
        private GameObject trailInstance;
        private bool warned;

        private Transform Tip => tipPoint != null ? tipPoint : transform;

        private void OnDestroy()
        {
            if (trailInstance != null) Destroy(trailInstance);
        }

        // トレイルを一度だけ生成して杖先に親付けする。プレハブ未設定なら false。
        private bool EnsureTrail()
        {
            if (trail != null) return true;
            if (trailInstance != null) return false;

            if (trailPrefab == null)
            {
                if (!warned)
                {
                    Debug.LogWarning("[B2_WandTrailVFX] trailPrefab(VFX_Queen_Trail_001) が未設定です。" +
                                     "杖のトレイルは出ません。", this);
                    warned = true;
                }
                return false;
            }

            // 杖先の子として生成 → 以降は杖の動きに自動で追従する
            trailInstance = Instantiate(trailPrefab, Tip, false);
            trailInstance.transform.localPosition = Vector3.zero;
            trailInstance.transform.localRotation = Quaternion.identity;

            trail = trailInstance.GetComponentInChildren<TrailRenderer>();
            if (trail == null)
            {
                Debug.LogWarning("[B2_WandTrailVFX] trailPrefab に TrailRenderer が見つかりません。", this);
                return false;
            }

            // 待機中は出さない。勝手に消えられると困るので autodestruct も切る
            trail.autodestruct = false;
            trail.emitting = false;
            trail.Clear();
            return true;
        }

        /// <summary>振り中のトレイルを開始/停止する。</summary>
        public void SetTrail(bool on)
        {
            if (!EnsureTrail()) return;

            // ON の瞬間に前回の軌跡を消す。残っていると待機位置から線が走る
            if (on) trail.Clear();
            trail.emitting = on;
        }
    }
}
