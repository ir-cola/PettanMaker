/*=============================================================================

 File   : [B2_Animator.cs]
 Desc   : ボスのアニメーションを管理するクラス
 ------------------------------------------------------------------------------

 Date   : 2026/06/25
 Author : Yokoyama Haruki
 
===============================================================================*/


using Boss02;
using UnityEngine;

public class B2_Animator : IPlayerAnimator
{
    B2_Boss boss;

    private readonly Animator animator;
    public Animator UnityAnimator => animator;

    public static readonly int ToNeedleMissile = Animator.StringToHash("ToNeedleMissile");
    public static readonly int ToTrumpShower = Animator.StringToHash("ToTrumpShower");
    public static readonly int ToSwordAttack = Animator.StringToHash("ToSwordAttack");
    public static readonly int ToSummonHedgehog = Animator.StringToHash("ToSummonHedgehog");
    public static readonly int ToSummonSoldier = Animator.StringToHash("ToSummonSoldier");
    public static readonly int ToDown = Animator.StringToHash("ToDown");
    public static readonly int NextAnimation = Animator.StringToHash("NextAnimation");

    public B2_Animator(Animator animator)
    {
        this.animator = animator;
    }

    public void SetFloat(int paramHash, float value)    => animator.SetFloat(paramHash, value);
    public void SetFloat(int paramHash,
                        float value,
                        float dampTime,
                        float dt)                       => animator.SetFloat(paramHash, value, dampTime, dt);
    public void SetBool(int paramHash, bool value)      => animator.SetBool(paramHash, value);
    public void SetTrigger(int paramHash)               => animator.SetTrigger(paramHash);
    public void ResetTrigger(int paramHash)             => animator.ResetTrigger(paramHash);
    public void SetInteger(int paramHash, int value)    => animator.SetInteger(paramHash, value);

    public bool FinishedAnimation(int layerIndex = 0) =>
        animator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1.0f;

    public bool FinishedAnimationWithTag(string tag, int layerIndex = 0)
    {
        var info = animator.GetCurrentAnimatorStateInfo(layerIndex);
        return info.IsTag(tag) && info.normalizedTime >= 1.0f;
    }

}
