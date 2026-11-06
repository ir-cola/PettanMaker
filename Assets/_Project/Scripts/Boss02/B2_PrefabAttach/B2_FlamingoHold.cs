/*=============================================================================

 File   : [B2_FlamingoHold.cs]
 Desc   : フラミンゴを女王の手に「持たせる」ための追従コンポーネント。
          Flamingo_Root にアタッチする。

          Grab(handBone) を呼ぶと、その瞬間の手との位置関係を保ったまま、
          以降 LateUpdate で毎フレーム手のボーンに追従する。Release() で解除。

          ■ なぜ親子付け(SetParent)ではなく追従なのか
            Animator の Flamingo レイヤーが
            "Flamingo_Root/flamingo/flamingo_Body_JNT" というパスでカーブを
            バインドしている。Flamingo_Root を手のボーンの子へ移すと
            Animator ルートから見たパスが変わってこのバインドが壊れ、
            フラミンゴ自身のアニメが死ぬ。
            一方 Flamingo_Root 自身はどのクリップも動かしていないので、
            ここをコードで動かす分には衝突しない。
            → 階層は変えずに追従するのが唯一の両立解。

          ■ 実行順
            Animator がボーンを書くのは Update と LateUpdate の間なので、
            LateUpdate で手を読めばそのフレームの最終姿勢が取れる。
            さらに DefaultExecutionOrder で B2_FlamingoMallet(既定=0) より
            先に動かし、マレットが常に「移動後」の自分の位置で速度と距離を
            測れるようにする(1フレームのズレを出さない)。

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using UnityEngine;

namespace Boss02
{
    /// <summary>手のボーンに追従してフラミンゴを持たせる(階層は変えない)。</summary>
    [DefaultExecutionOrder(-50)] // B2_FlamingoMallet より先に動かす
    public class B2_FlamingoHold : MonoBehaviour
    {
        private Transform hand;
        private Vector3 gripLocalPosition;    // 手のローカル空間での保持位置
        private Quaternion gripLocalRotation; // 手のローカル空間での保持姿勢
        private bool held;

        /// <summary>今 手に持たれているか</summary>
        public bool IsHeld => held;

        /// <summary>
        /// 手に持たせる。呼んだ瞬間の見た目(手との位置関係)をそのまま維持して追従を始めるので、
        /// 掴んだ位置から飛んだりしない。
        /// </summary>
        public void Grab(Transform handBone)
        {
            if (handBone == null) return;

            hand = handBone;
            // 現在の姿勢を手のローカル空間へ変換して覚える
            gripLocalPosition = hand.InverseTransformPoint(transform.position);
            gripLocalRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
            held = true;

            Follow(); // 掴んだフレームから反映しておく
        }

        /// <summary>追従を解除する。以降は呼び出し側が自由に動かせる。</summary>
        public void Release()
        {
            held = false;
            hand = null;
        }

        private void LateUpdate()
        {
            if (!held || hand == null) return;
            Follow();
        }

        private void Follow()
        {
            transform.SetPositionAndRotation(
                hand.TransformPoint(gripLocalPosition),
                hand.rotation * gripLocalRotation);
        }
    }
}
