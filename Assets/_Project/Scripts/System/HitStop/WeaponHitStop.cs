/**===========================================================================

 File   : 武器ヒットストップ [WeaponHitStop.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 ヒットストップ中は武器の Animator を停止し、復帰時に元の再生速度へ戻す。
 ===========================================================================**/
using UnityEngine;

public class WeaponHitStop : HitStopBase {
    private Animator animator;
    private float originalSpeed = 1f;

    private void Awake() => animator = GetComponentInChildren<Animator>();

    protected override void OnFreeze() {
        if (animator == null) return;
        originalSpeed = animator.speed;
        animator.speed = 0f;
    }

    protected override void OnRestore() {
        if (animator != null) animator.speed = originalSpeed;
    }
}
