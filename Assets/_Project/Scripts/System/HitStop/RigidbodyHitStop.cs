/**===========================================================================

 File   : Rigidbody用ヒットストップ [RigidbodyHitStop.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 凍結中は速度を退避して isKinematic を有効化し、復帰時に元の速度へ戻す。
 ===========================================================================**/
using UnityEngine;

public class RigidbodyHitStop : HitStopBase {
    private Rigidbody rb;
    private Vector3 savedVelocity;
    private Vector3 savedAngularVelocity;
    private bool rbWasFrozen;

    protected virtual void Awake() => rb = GetComponent<Rigidbody>();

    protected override void OnFreeze() {
        if (rb == null || rb.isKinematic) return;
        savedVelocity        = rb.linearVelocity;
        savedAngularVelocity = rb.angularVelocity;
        rb.linearVelocity    = Vector3.zero;
        rb.angularVelocity   = Vector3.zero;
        rb.isKinematic       = true;
        rbWasFrozen          = true;
    }

    protected override void OnRestore() {
        if (rb == null || !rbWasFrozen) return;
        rb.isKinematic       = false;
        rb.linearVelocity    = savedVelocity;
        rb.angularVelocity   = savedAngularVelocity;
        rbWasFrozen          = false;
    }
}
