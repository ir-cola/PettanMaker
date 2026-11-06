/**===========================================================================

 File   : プレイヤーアニメーター [PlayerAnimator.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 UnityEngine.Animator のラッパー。パラメータハッシュをキャッシュ + 短縮 API。
 ===========================================================================**/

using UnityEngine;

public class PlayerAnimator : IPlayerAnimator {

    private readonly Animator animator;
    public Animator UnityAnimator => animator;

    // Animator パラメータのハッシュ (生成コスト削減 + タイポ防止)
    public static readonly int MoveParam          = Animator.StringToHash("Move");
    public static readonly int JumpParam          = Animator.StringToHash("Jump");
    public static readonly int JumpStartParam     = Animator.StringToHash("JumpStart");
    public static readonly int AttackParam        = Animator.StringToHash("Attack");
    public static readonly int DeathParam         = Animator.StringToHash("Death");
    public static readonly int AttackSpeedParam   = Animator.StringToHash("AttackSpeed");
    public static readonly int ChargeParam        = Animator.StringToHash("Charge");
    // 3段コンボ (タップ攻撃)。Combo トリガー + ComboIndex(0/1/2) で AnyState から各段へ。
    public static readonly int ComboParam         = Animator.StringToHash("Combo");
    public static readonly int ComboIndexParam    = Animator.StringToHash("ComboIndex");
    public static readonly int DamageParam        = Animator.StringToHash("Damage");
    public static readonly int IsRollParam        = Animator.StringToHash("IsRoll");
    public static readonly int IsLandParam        = Animator.StringToHash("IsLand");
    public static readonly int UseItemParam       = Animator.StringToHash("UseItem");
    public static readonly int DamageTypeParam    = Animator.StringToHash("DamageType");
    public static readonly int KnockBackStartParam = Animator.StringToHash("KnockBack_Start");
    public static readonly int IsKnockBackForWallParam = Animator.StringToHash("IsKnockBackForWall");
    public static readonly int KnockBackHitWallParam = Animator.StringToHash("KnockBack_HitWall");
    public static readonly int RecoverFromKnockBackParam = Animator.StringToHash("RecoverFromKnockBack");
    public static readonly int IsKnockBackParam = Animator.StringToHash("IsKnockBack");
    public static readonly int ChargeLvParam      = Animator.StringToHash("ChargeLv");
    public static readonly int VerticalSpeedParam = Animator.StringToHash("VerticalSpeed");
    public static readonly int ToMoveParam        = Animator.StringToHash("ToMove");
    public static readonly int ScaryWalkParam     = Animator.StringToHash("ScaryWalk");
    public static readonly int LowerSpeedParam    = Animator.StringToHash("LowerSpeed"); // 下半身レイヤー再生速度(後退で-1=逆再生)
    // 各モーションの再生速度倍率 (PlayerMotionSpeeds が一括設定)。AttackSpeed は既存を流用。
    public static readonly int LocoSpeedParam     = Animator.StringToHash("LocoSpeed");
    public static readonly int JumpSpeedParam     = Animator.StringToHash("JumpSpeed");
    public static readonly int FallSpeedParam     = Animator.StringToHash("FallSpeed");
    public static readonly int DodgeSpeedParam    = Animator.StringToHash("DodgeSpeed");
    public static readonly int ChargeSpeedParam   = Animator.StringToHash("ChargeSpeed");
    public static readonly int ComboSpeedParam    = Animator.StringToHash("ComboSpeed"); // 3段コンボの再生速度

    public PlayerAnimator(Animator animator) {
        this.animator = animator;
    }

    public void SetFloat(int paramHash, float value)                                 => animator.SetFloat(paramHash, value);
    public void SetFloat(int paramHash, float value, float dampTime, float dt)       => animator.SetFloat(paramHash, value, dampTime, dt);
    public void SetBool(int paramHash, bool value)                                   => animator.SetBool(paramHash, value);
    public void SetTrigger(int paramHash)                                            => animator.SetTrigger(paramHash);
    public void ResetTrigger(int paramHash)                                          => animator.ResetTrigger(paramHash);
    public void SetInteger(int paramHash, int value)                                 => animator.SetInteger(paramHash, value);

    public bool FinishedAnimation(int layerIndex = 0) =>
        animator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1.0f;

    public bool FinishedAnimationWithTag(string tag, int layerIndex = 0) {
        var info = animator.GetCurrentAnimatorStateInfo(layerIndex);
        return info.IsTag(tag) && info.normalizedTime >= 1.0f;
    }
}
