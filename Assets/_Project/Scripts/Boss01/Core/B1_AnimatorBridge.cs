using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ActionクラスとAnimatorを繋ぐ中間層。
/// 移動状態は MoveSpeed パラメータで Animator を直接制御する。
/// 攻撃・リアクションは SetTrigger で再生し、AnimationEvent からイベントを発火する。
/// クリップ未実装時はコルーチンでタイミングを代替する。
/// </summary>
public class B1_AnimatorBridge : MonoBehaviour
{
    private static readonly int PMoveSpeed   = Animator.StringToHash("MoveSpeed");
    private static readonly int HScratch    = Animator.StringToHash("Scratch");
    private static readonly int HCharge     = Animator.StringToHash("Charge");
    private static readonly int HRush       = Animator.StringToHash("Rush");
    private static readonly int HTailAttack = Animator.StringToHash("TailAttack");
    private static readonly int HTailAttackBJump = Animator.StringToHash("TailAttackBJump");
    private static readonly int HTailAttackLand = Animator.StringToHash("TailAttackLand");
    private static readonly int HTailAttackJumpState = Animator.StringToHash("TailAttackJump");
    private static readonly int HTailAttackLandState = Animator.StringToHash("TailAttack_Land");
    private static readonly int HDeath      = Animator.StringToHash("Death");
    private static readonly int HTurnLeft   = Animator.StringToHash("TurnLeft");
    private static readonly int HTurnRight  = Animator.StringToHash("TurnRight");
    private static readonly int HBackStep01 = Animator.StringToHash("BackStep_01");
    private static readonly int HBackStep02 = Animator.StringToHash("BackStep_02");
    private static readonly int HBackStep03 = Animator.StringToHash("BackStep_03");
    private static readonly int HDown       = Animator.StringToHash("Down");
    private static readonly int HDownIdle   = Animator.StringToHash("Down_Idle");
    private static readonly int HLocomotion = Animator.StringToHash("Locomotion");
    private static readonly int PDownPoseFinished = Animator.StringToHash("DownPoseFinished");

    [Header("クリップ未実装時のフォールバック（秒）")]
    [SerializeField] private float telegraphDuration = 1.5f;
    [SerializeField] private float hitboxDuration    = 0.5f;
    [SerializeField] private float recoveryDuration  = 0.8f;

    public event Action OnTelegraphStartEvent;
    public event Action OnTelegraphEndEvent;
    public event Action OnAttackStateStartedEvent;
    public event Action OnHitboxActivateEvent;
    public event Action OnHitboxDeactivateEvent;
    public event Action OnAttackFinishedEvent;
    public event Action OnDeathFinishedEvent;

    private Animator anim;
    private B1_Boss owner;

    /// <summary>ボスが制御している Animator。ヒットストップなど外部演出が共有する。</summary>
    public Animator Animator => anim;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        if (anim == null)
            anim = GetComponentInChildren<Animator>(true);
        owner = GetComponentInParent<B1_Boss>();
    }

    // ---- 移動パラメータ ----

    public void SetMoving(bool isMoving)  => anim.SetFloat(PMoveSpeed, isMoving ? 1f : 0f);
    public void SetMoveSpeed(float speed) => anim.SetFloat(PMoveSpeed, speed);

    /// <summary>Walk / Run クリップの足接地 AnimationEvent 受け口。</summary>
    public void AnimationEvent_Footstep()
    {
        if (owner == null) owner = GetComponentInParent<B1_Boss>();
        owner?.PlayWalkStepSound();
    }

    /// <summary>TailAttack_Landの接地フレームから呼ぶ。</summary>
    public void AnimationEvent_TailAttackLand()
    {
        if (owner == null) owner = GetComponentInParent<B1_Boss>();
        owner?.PlayTailLandSound();
    }

    /// <summary>BackStep_03の接地フレームから呼ぶ。</summary>
    public void AnimationEvent_BackStepLand()
    {
        if (owner == null) owner = GetComponentInParent<B1_Boss>();
        owner?.PlayBackStepLandSound();
    }

    /// <summary>90度ターン(Turn_L / Turn_R)を再生する。回転自体は B1_Locomotion のスクリプト側が行い、これは見た目のみ。</summary>
    public void PlayTurn(bool left)
    {
        if (HasTriggerParam(left ? HTurnLeft : HTurnRight))
            anim.SetTrigger(left ? HTurnLeft : HTurnRight);
    }

    // ---- 後退ジャンプ (BackStep_01=飛び上がり → 02=空中Idle → 03=着地) ----
    // 01→02→03 は Animator の ExitTime 遷移で自動連鎖する。スクリプト側のジャンプ尺と
    // ずれる場合に備え、滞空保持 (HoldBackStepAir) と着地 (PlayBackStepLand) で強制同期する。

    /// <summary>後退ジャンプの飛び上がり (BackStep_01) を再生する。以降 02 へは自動遷移。</summary>
    public void PlayBackStepJump()
    {
        if (anim != null && anim.HasState(0, HBackStep01))
            anim.CrossFadeInFixedTime(HBackStep01, 0.1f, 0);
    }

    /// <summary>滞空中に毎フレーム呼ぶ。自動遷移が先に 03 以降へ抜けてしまった場合、空中Idle (02) へ戻す。</summary>
    public void HoldBackStepAir()
    {
        if (anim == null || anim.IsInTransition(0)) return;
        int current = anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (current == HBackStep01 || current == HBackStep02) return;
        if (anim.HasState(0, HBackStep02))
            anim.CrossFadeInFixedTime(HBackStep02, 0.1f, 0);
    }

    /// <summary>着地の瞬間に呼ぶ。着地モーション (BackStep_03) を強制再生する。以降 Locomotion へ自動復帰。</summary>
    public void PlayBackStepLand()
    {
        if (anim != null && anim.HasState(0, HBackStep03))
            anim.CrossFadeInFixedTime(HBackStep03, 0.05f, 0);
    }

    // ---- 攻撃・リアクション再生 ----

    public void PlayAttack(string stateName)
    {
        StopAllCoroutines();
        int hash = Animator.StringToHash(stateName);
        if (anim != null && anim.HasState(0, hash))
        {
            // 連続攻撃は OnStateExit 中にも呼ばれるため、Trigger 遷移を待たず直接切り替える。
            anim.CrossFadeInFixedTime(hash, 0.05f, 0);
            StartCoroutine(AttackFinishTimeout()); // SMB未アタッチ時の安全弁
        }
        else
        {
            StartCoroutine(AttackSequenceFallback()); // ステート未設定時の完全フォールバック
        }
    }

    public void PlayRush()       { if (HasTriggerParam(HRush))       anim.SetTrigger(HRush); }

    /// <summary>テールアタック前の沈み込みからジャンプ準備モーションへ遷移する。</summary>
    public void PlayTailAttackBeforeJump()
    {
        if (HasTriggerParam(HTailAttackBJump))
            anim.SetTrigger(HTailAttackBJump);
    }

    public void PlayTailAttack()
    {
        StopAllCoroutines();
        if (HasTriggerParam(HTailAttack))
        {
            anim.SetTrigger(HTailAttack);
            StartCoroutine(AttackFinishTimeout()); // AnimationEvent 未設定時の安全弁
        }
    }

    /// <summary>TailAttackJump ステートへ直接クロスフェードする。Trigger 遷移が張られていない
    /// ステート（TailRain でしっぽ立ちのまま次のジャンプへ入るときなど）からでも確実に跳び上がれる。</summary>
    public void PlayTailAttackJumpDirect()
    {
        StopAllCoroutines();
        if (anim != null && anim.HasState(0, HTailAttackJumpState))
            anim.CrossFadeInFixedTime(HTailAttackJumpState, 0.1f, 0);
        else
            PlayTailAttackBeforeJump();
    }

    /// <summary>TailAttack（しっぽ立ち）ステートへ遷移を挟まず即座に入る。
    /// 衝撃波と同フレームで PauseAnimator しても、しっぽ立ちポーズで静止できる
    /// （CrossFade + Trigger だと speed=0 で遷移が進まず、前のポーズのまま固まってしまう）。</summary>
    public void PlayTailAttackImmediate()
    {
        StopAllCoroutines();
        if (anim != null && anim.HasState(0, HTailAttack))
            anim.Play(HTailAttack, 0, 0f);
        else
            PlayTailAttack();
    }

    /// <summary>TailAttack_Land ステートへ直接クロスフェードする。TailAttackLand トリガーの遷移は
    /// TailAttack(しっぽ立ち)からしか張られていないため、TailAttackJump など他ステートから
    /// 確実に着地モーションへ入りたいときに使う（Phase2 突入演出の着地など）。</summary>
    public void PlayTailAttackLandDirect()
    {
        StopAllCoroutines();
        if (anim != null && anim.HasState(0, HTailAttackLandState))
        {
            anim.CrossFadeInFixedTime(HTailAttackLandState, 0.05f, 0);
        }
        else
        {
            PlayTailAttackLand();
        }
    }

    /// <summary>ループ中のTailAttackを終了し、着地モーションへ遷移する。</summary>
    public void PlayTailAttackLand()
    {
        StopAllCoroutines();
        if (HasTriggerParam(HTailAttackLand))
            anim.SetTrigger(HTailAttackLand);
    }
    // ---- ダウン (体力半分 → Phase2 移行前の無防備状態) ----
    // Down (静止モーション・浮遊) → Down_Idle (着地してプレイヤーが殴れる) の2段構成。

    /// <summary>ダウンモーション (Down 静止) を再生する。</summary>
    public void PlayDown()
    {
        StopAllCoroutines();
        SetDownPoseFinished(false);
        if (anim != null && anim.HasState(0, HDown))
            anim.CrossFadeInFixedTime(HDown, 0.15f, 0);
    }

    /// <summary>Down → Down_Idle へブレンド遷移する。blendDuration は浮遊解除(降下)と同じ尺を渡す。</summary>
    public void PlayDownIdle(float blendDuration)
    {
        SetDownPoseFinished(true);
        if (anim != null && anim.HasState(0, HDownIdle))
            anim.CrossFadeInFixedTime(HDownIdle, Mathf.Max(0.01f, blendDuration), 0);
    }

    /// <summary>Down 静止中に毎フレーム呼ぶ。ExitTime 等で Down から抜けてしまった場合に戻す。</summary>
    public void HoldDown() => HoldState(HDown);

    /// <summary>Down_Idle 中に毎フレーム呼ぶ。ExitTime 等で Down_Idle から抜けてしまった場合に戻す。</summary>
    public void HoldDownIdle() => HoldState(HDownIdle);

    private void HoldState(int stateHash)
    {
        if (anim == null || anim.IsInTransition(0)) return;
        if (anim.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash) return;
        if (anim.HasState(0, stateHash))
            anim.CrossFadeInFixedTime(stateHash, 0.1f, 0);
    }

    /// <summary>ダウン解除。Locomotion (通常の移動ブレンド) へ戻す。</summary>
    public void PlayDownRecover()
        => PlayLocomotion();

    /// <summary>攻撃などを中断し、通常の移動ブレンドへ強制的に戻す。</summary>
    public void PlayLocomotion(float blendDuration = 0.2f)
    {
        StopAllCoroutines();
        if (anim != null && anim.HasState(0, HLocomotion))
            anim.CrossFadeInFixedTime(HLocomotion, Mathf.Max(0f, blendDuration), 0);
    }

    public void PauseAnimator()  => anim.speed = 0f;
    public void ResumeAnimator() => anim.speed = speedMultiplier;

    /// <summary>攻撃の緩急（予備動作スロー→振り抜き高速）用に再生速度を直接指定する。
    /// アクション終了時は ResumeAnimator()（=倍率込みの1）で必ず戻すこと。</summary>
    public void SetAnimatorSpeed(float speed) => anim.speed = Mathf.Max(0f, speed) * speedMultiplier;

    // 激昂（怒りモード）などの全体速度倍率。Pause(0) 以外の速度指定すべてに乗算される。
    private float speedMultiplier = 1f;

    /// <summary>Animator 全体の再生速度倍率を設定する（B1_Boss が激昂状態に応じて毎フレーム流し込む）。
    /// 通常再生中（速度が旧倍率と一致）なら即座に新しい倍率を反映する。</summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        multiplier = Mathf.Max(0.01f, multiplier);
        if (Mathf.Approximately(multiplier, speedMultiplier)) return;
        if (anim != null && Mathf.Approximately(anim.speed, speedMultiplier))
            anim.speed = multiplier;
        speedMultiplier = multiplier;
    }
    public void PlayDeath()      { if (HasTriggerParam(HDeath))      anim.SetTrigger(HDeath); }

    // ---- AnimationEvent コールバック（各クリップのキーフレームで呼ぶ） ----

    public void OnTelegraphStart()   => OnTelegraphStartEvent?.Invoke();
    public void OnTelegraphEnd()     => OnTelegraphEndEvent?.Invoke();
    public void OnAttackStateStarted() => OnAttackStateStartedEvent?.Invoke();
    public void OnHitboxActivate()   => OnHitboxActivateEvent?.Invoke();
    public void OnHitboxDeactivate() => OnHitboxDeactivateEvent?.Invoke();
    public void OnAttackFinished()
    {
        StopAllCoroutines();
        OnAttackFinishedEvent?.Invoke();
    }

    // 死亡アニメの最終フレームに AnimationEvent として登録する
    public void OnDeathFinished() => OnDeathFinishedEvent?.Invoke();

    // ---- 内部ヘルパー ----

    private bool HasTriggerParam(int nameHash)
    {
        if (anim == null)
            return false;
        foreach (var p in anim.parameters)
            if (p.nameHash == nameHash && p.type == AnimatorControllerParameterType.Trigger)
                return true;
        return false;
    }

    private bool HasParameter(int nameHash, AnimatorControllerParameterType type)
    {
        if (anim == null)
            return false;
        foreach (var p in anim.parameters)
            if (p.nameHash == nameHash && p.type == type)
                return true;
        return false;
    }

    private void SetDownPoseFinished(bool finished)
    {
        if (HasParameter(PDownPoseFinished, AnimatorControllerParameterType.Bool))
            anim.SetBool(PDownPoseFinished, finished);
    }

    private IEnumerator AttackFinishTimeout()
    {
        yield return new WaitForSeconds(telegraphDuration + hitboxDuration + recoveryDuration);
        OnAttackFinishedEvent?.Invoke();
    }

    private IEnumerator AttackSequenceFallback()
    {
        OnAttackStateStartedEvent?.Invoke();
        OnTelegraphStartEvent?.Invoke();
        yield return new WaitForSeconds(telegraphDuration);
        OnTelegraphEndEvent?.Invoke();
        OnHitboxActivateEvent?.Invoke();
        yield return new WaitForSeconds(hitboxDuration);
        OnHitboxDeactivateEvent?.Invoke();
        yield return new WaitForSeconds(recoveryDuration);
        OnAttackFinishedEvent?.Invoke();
    }
}
