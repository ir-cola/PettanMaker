/*=============================================================================

 File   : [B2_FallCamera.cs]
 Desc   : 開幕の落下中、プレイヤーを後ろ上から見下ろすカメラ。
          Begin() で追従開始、End() で停止する。B2_BossRoomTrigger が
          落下開始で Begin、着地で End を呼ぶ。

          追従はプレイヤーの位置に followOffset(ワールド)を足した所へ毎フレーム
          カメラを置き、プレイヤーを見下ろす。落下でプレイヤーが下がると
          カメラも一緒に下がるので、ずっと同じ画角で落下を追える。

          ■ TPSカメラとの関係
            動かすのは TPSCameraController.transform(=実カメラ)そのもの。
            Begin() で TPS を無効化して奪い、End() 後は次のショット
            (CinemaTrack)が同じように奪うので、落下→旋回の間もカメラは
            演出側が握ったままになる。
            ※ TPS の再有効化は演出の最後(B2_QueenIntroCamera)がまとめて行う。

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using UnityEngine;

namespace Boss02
{
    /// <summary>落下中プレイヤーを後ろ上から見下ろすカメラ。</summary>
    [DefaultExecutionOrder(200)] // TPS/他のカメラ処理より後に上書きする
    public class B2_FallCamera : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("追う対象。未設定なら Player タグを探す")]
        [SerializeField] private Transform target;
        [Tooltip("動かすカメラ。未設定なら TPSCameraController / MainCamera を探す")]
        [SerializeField] private TPSCameraController tps;

        [Header("画角")]
        [Tooltip("対象からのカメラ位置オフセット(ワールド)。\n" +
                 "既定 (2, 6, 8) = 後ろ(+Z)・上(+Y)・少し右(+X)から見下ろす")]
        [SerializeField] private Vector3 followOffset = new Vector3(2f, 6f, 8f);
        [Tooltip("注視点のオフセット(対象基準)。少し上を見たいなら y を上げる")]
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("追従の滑らかさ。0で即追従、大きいほどゆっくり追う")]
        [SerializeField, Range(0f, 0.5f)] private float smoothTime = 0.06f;

        private Transform cam;
        private bool active;
        private Vector3 vel;

        /// <summary>落下追従を開始する。TPSカメラを奪う。</summary>
        public void Begin()
        {
            EnsureRefs();
            if (cam == null || target == null) return;

            if (tps != null) tps.enabled = false;
            active = true;
            // 初回はスナップ(補間の初速をリセット)
            cam.position = target.position + followOffset;
            vel = Vector3.zero;
            AimAtTarget();
        }

        /// <summary>落下追従を停止する。カメラはそのまま次のショットへ引き継ぐ。</summary>
        public void End() => active = false;

        private void LateUpdate()
        {
            if (!active || cam == null || target == null) return;

            Vector3 desired = target.position + followOffset;
            cam.position = smoothTime > 0f
                ? Vector3.SmoothDamp(cam.position, desired, ref vel, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime)
                : desired;
            AimAtTarget();
        }

        private void AimAtTarget()
        {
            Vector3 look = target.position + lookAtOffset;
            Vector3 dir = look - cam.position;
            if (dir.sqrMagnitude > 1e-6f) cam.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private void EnsureRefs()
        {
            if (target == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) target = p.transform;
            }
            if (tps == null) tps = TPSCameraController.Instance;
            if (tps == null) tps = FindObjectOfType<TPSCameraController>();
            cam = tps != null ? tps.transform : (Camera.main != null ? Camera.main.transform : null);
        }

#if UNITY_EDITOR
        // Scene ビューで落下開始位置からの画角を確認できるようにする
        private void OnDrawGizmosSelected()
        {
            if (target == null) return;
            Vector3 camPos = target.position + followOffset;
            Vector3 look = target.position + lookAtOffset;
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawWireSphere(camPos, 0.4f);
            Gizmos.DrawLine(camPos, look);
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(look, 0.2f);
        }
#endif
    }
}
