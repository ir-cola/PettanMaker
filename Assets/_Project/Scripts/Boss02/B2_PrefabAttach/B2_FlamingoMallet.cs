/*=============================================================================

 File   : [B2_FlamingoMallet.cs]
 Desc   : フラミンゴ(マレット)の打撃部にアタッチするコンポーネント。
          自身の移動速度を追跡し、登録された対象ハリネズミへ一定距離まで
          近づいた瞬間に、その速度方向へ打ち出す。
          フラミンゴ本体は B2_SummonHedgehog がコード制御で動かすため、
          Rigidbody/Trigger などの物理には依存せず、距離判定で接触を取る。
 ------------------------------------------------------------------------------

 Date   : 2026/07/08
 Author : akatu

===============================================================================*/

using UnityEngine;

namespace Boss02
{
    public class B2_FlamingoMallet : MonoBehaviour
    {
        [Tooltip("打撃部の中心(未設定ならこのTransform)")]
        [SerializeField] Transform hitPoint;
        [Tooltip("この距離までハリネズミに近づいたら打ち出す")]
        [SerializeField] float hitRadius = 0.6f;
        [Tooltip("これ未満の移動速度では打ち出さない(前方向にフォールバック)")]
        [SerializeField] float minLaunchSpeed = 0.5f;
        [Tooltip("打ち出し方向に加える上向き成分")]
        [SerializeField] float upwardBias = 0.2f;

        B2_MoveHedgeHog target;
        Vector3 prevPos;
        Vector3 velocity;

        Transform Point => hitPoint != null ? hitPoint : transform;

        void Awake()
        {
            prevPos = Point.position;
        }

        /// <summary>この打撃で打ち出す対象のハリネズミを登録する。</summary>
        public void SetTarget(B2_MoveHedgeHog hedgehog)
        {
            target = hedgehog;
        }

        /// <summary>打ち出しが完了(または対象なし)なら true。</summary>
        public bool HasLaunched => target == null;

        void LateUpdate()
        {
            // 自身の移動速度を追跡(コード制御で動くため物理から取得できない)
            Vector3 pos = Point.position;
            if (Time.deltaTime > 0f)
            {
                velocity = (pos - prevPos) / Time.deltaTime;
            }
            prevPos = pos;

            if (target == null) return;
            if (!target.IsLaunchable) return;
            if (Vector3.Distance(pos, target.transform.position) > hitRadius) return;

            // 打ち出し方向 = マレットの水平移動方向(速度が無ければ前方)
            Vector3 dir = velocity;
            dir.y = 0f;
            if (dir.magnitude < minLaunchSpeed)
            {
                dir = Point.forward;
                dir.y = 0f;
            }
            dir = (dir.normalized + Vector3.up * upwardBias).normalized;

            target.Launch(dir);
            target = null; // 二重打ち出し防止
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Point.position, hitRadius);
        }
#endif
    }
}
