using UnityEngine;

/// <summary>
/// 攻撃ステートにアタッチして、正規化時間でヒットボックスイベントを発火する。
/// タイミングは B1_Settings.Scratch.HitboxTimings[scratchIndex] で管理する。
/// </summary>
public class B1_AttackStateBehaviour : StateMachineBehaviour
{
    [Tooltip("Scratch01=0, Scratch02=1, Scratch03=2")]
    [SerializeField] private int scratchIndex = 0;

    private B1_AnimatorBridge bridge;
    private float hitboxEnterTime;
    private float hitboxExitTime;
    private bool hitboxOpened;
    private bool hitboxClosed;
    private float anticipationSpeed;
    private float swingSpeed;
    private float swingStartTime;
    private bool swingSpeedApplied;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layer)
    {
        bridge = animator.GetComponentInParent<B1_AnimatorBridge>(true);
        hitboxOpened = hitboxClosed = false;
        swingSpeedApplied = false;
        bridge?.OnAttackStateStarted();

        var scratch = animator.GetComponentInParent<B1_Boss>(true)?.Settings.Scratch;
        var timings = scratch?.HitboxTimings;

        if (timings != null && scratchIndex < timings.Length)
        {
            hitboxEnterTime = timings[scratchIndex].EnterTime;
            hitboxExitTime  = timings[scratchIndex].ExitTime;
        }
        else
        {
            hitboxEnterTime = 0.4f;
            hitboxExitTime  = 0.7f;
        }

        var speedTimings = scratch?.AnimationSpeedTimings;
        if (speedTimings != null && scratchIndex < speedTimings.Length && speedTimings[scratchIndex] != null)
        {
            var speedTiming = speedTimings[scratchIndex];
            anticipationSpeed = Mathf.Max(0.05f, speedTiming.AnticipationSpeed);
            swingSpeed = Mathf.Max(0.05f, speedTiming.SwingSpeed);
            swingStartTime = Mathf.Clamp01(speedTiming.SwingStartTime);
        }
        else
        {
            anticipationSpeed = 1f;
            swingSpeed = 1f;
            swingStartTime = hitboxEnterTime;
        }

        animator.speed = anticipationSpeed;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo info, int layer)
    {
        if (bridge == null) return;
        float t = info.normalizedTime;

        if (!swingSpeedApplied && t >= swingStartTime)
        {
            swingSpeedApplied = true;
            animator.speed = swingSpeed;
        }

        if (!hitboxOpened && t >= hitboxEnterTime)
        {
            hitboxOpened = true;
            bridge.OnHitboxActivate();
        }
        if (!hitboxClosed && t >= hitboxExitTime)
        {
            hitboxClosed = true;
            bridge.OnHitboxDeactivate();
        }
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layer)
    {
        animator.speed = 1f;
        if (bridge == null) return;
        if (!hitboxClosed) bridge.OnHitboxDeactivate();
        bridge.OnAttackFinished();
    }
}
