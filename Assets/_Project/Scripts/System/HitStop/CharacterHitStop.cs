/**===========================================================================

 File   : キャラクターヒットストップ [CharacterHitStop.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 RigidbodyHitStop を拡張し、ヒットストップ中は Animator の再生速度も
 停止・復元するキャラクター用ヒットストップ。
 ===========================================================================**/
using UnityEngine;

[DisallowMultipleComponent]
public class CharacterHitStop : RigidbodyHitStop {
    private Animator animator;
    private float originalAnimSpeed = 1f;

    protected override void Awake() {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }

    protected override void OnFreeze() {
        base.OnFreeze();
        if (animator == null) return;
        originalAnimSpeed = animator.speed;
        animator.speed = 0f;
    }

    protected override void OnRestore() {
        base.OnRestore();
        if (animator != null) animator.speed = originalAnimSpeed;
    }
}
