using System;
using UnityEngine;

/// <summary>
/// Connects EnemyAction/EnemyAI code to Animator parameters and animation events.
/// Mirrors the Boss01 bridge pattern so gameplay code does not need to know
/// whether a motion is driven by a trigger, bool, or animation event.
/// </summary>
public class EnemyAnimatorBridge : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private string speedParameter = "Speed";
    private string alertParameter = "IsAlert";

    public event Action OnAttackImpactEvent;
    public event Action OnAttackFinishedEvent;
    public event Action OnMotionEvent;

    public Animator Animator => animator;

    private int speedHash;
    private int alertHash;

    private void Awake()
    {
        ResolveAnimator();
        RebuildHashes();
    }

    public void Initialize(string speedParam, string alertParam)
    {
        if (!string.IsNullOrEmpty(speedParam)) speedParameter = speedParam;
        if (!string.IsNullOrEmpty(alertParam)) alertParameter = alertParam;
        ResolveAnimator();
        RebuildHashes();
    }

    public void SetSpeed(float speed)
    {
        if (animator != null && HasParameter(speedHash, AnimatorControllerParameterType.Float))
            animator.SetFloat(speedHash, speed);
    }

    public void SetAlert(bool isAlert)
    {
        if (animator != null && HasParameter(alertHash, AnimatorControllerParameterType.Bool))
            animator.SetBool(alertHash, isAlert);
    }

    public void SetInteger(string parameterName, int value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return;

        int hash = Animator.StringToHash(parameterName);
        if (HasParameter(hash, AnimatorControllerParameterType.Int))
            animator.SetInteger(hash, value);
    }

    public void SetBool(string parameterName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return;

        int hash = Animator.StringToHash(parameterName);
        if (HasParameter(hash, AnimatorControllerParameterType.Bool))
            animator.SetBool(hash, value);
    }

    public void SetTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
            return;

        int hash = Animator.StringToHash(triggerName);
        if (HasParameter(hash, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(hash);
    }

    public void CrossFade(string stateName, float fixedTransitionDuration = 0.05f)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, fixedTransitionDuration, 0);
    }

    public void OnAttackImpact() => OnAttackImpactEvent?.Invoke();
    public void OnAttackFinished() => OnAttackFinishedEvent?.Invoke();
    public void OnMotionEventRaised() => OnMotionEvent?.Invoke();

    private void ResolveAnimator()
    {
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    private void RebuildHashes()
    {
        speedHash = Animator.StringToHash(speedParameter);
        alertHash = Animator.StringToHash(alertParameter);
    }

    private bool HasParameter(int nameHash, AnimatorControllerParameterType type)
    {
        if (animator == null)
            return false;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.nameHash == nameHash && parameter.type == type)
                return true;
        }

        return false;
    }
}
