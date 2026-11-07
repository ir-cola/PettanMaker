/**===========================================================================

 File   : プレイヤー被弾リアクション [PlayerHitReaction.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 -----------------------------------------------------------------------------
 被弾時のノックバックと、無敵時間中のモデル点滅を扱う。
 CombatSystem.onDamageDealtDetailed を購読し、攻撃元 (source) から離れる方向へ
 CharacterController を短時間押し出しつつ DamageType に応じて被弾アニメを分岐する。
 無敵時間そのもの (IsInvincible) は
 PlayerHealth が管理し、本コンポーネントは演出のみを担当する。
 PlayerHealth.Awake() で自動追加されるため、プレハブ直付けは不要。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHitReaction : MonoBehaviour {

    private const float DefaultKnockbackDistance = 2.0f;
    private const float DefaultKnockbackIdleDuration = 0.25f;
    private const float DefaultKnockbackGroundDuration = 0.25f;
    private const float DefaultKnockbackRecoverDuration = 0.25f;
    private const float DefaultKnockbackRecoveryGrace = 0.3f;
    // Locomotion へ戻すときの最小ブレンド時間 (秒)。倒れ込みなし/壁ヒット時のスナップ復帰に使う。
    private const float DefaultLocomotionBlend = 0.05f;
    // Animator の遷移不成立・無効化・ループ設定があっても操作ロックを永久化させない下限値。
    private const float MinimumRecoverWaitTimeout = 1.5f;
    private static readonly int LocomotionState = Animator.StringToHash("Base Layer.Locomotion");
    private static readonly int KnockbackGroundState = Animator.StringToHash("Base Layer.KnockBack_Ground");
    private static readonly int KnockbackRecoverState = Animator.StringToHash("Base Layer.Recover_From_Knockback");

    [Header("壁衝突")]
    [Tooltip("ノックバック中に壁リアクションを発生させるレイヤー。未設定なら Wall レイヤーを自動使用する。")]
    [SerializeField] private LayerMask wallLayers;
    [Tooltip("壁として扱う接触面の法線Y成分上限。床や斜面を壁と誤判定しないための値。")]
    [SerializeField, Range(0f, 1f)] private float wallNormalMaxY = 0.35f;

    [Header("無敵中の点滅")]
    [Tooltip("表示/非表示を切り替える間隔 (秒)")]
    [SerializeField] private float blinkInterval = 0.1f;

    private CharacterController characterController;
    private PlayerHealth health;
    private PlayerLifetimeScope playerScope;
    private Animator animator;
    private Transform animationRoot;
    private Quaternion animationRootRotationBeforeWallHit;
    private bool wallHitRootRotationApplied;
    private Coroutine knockbackRoutine;
    private Coroutine blinkRoutine;
    private bool knockbackMovementActive;
    private bool hitWallDuringKnockback;
    // 進行中ノックバックの DamageType。より弱い被弾での上書き防止に使う (非進行中は 0)
    private int activeKnockbackDamageType;

    // 点滅対象 (点滅開始時に「表示中」だったレンダラーのみ。
    // 服スキル等で意図的に隠されているレンダラーを誤って再表示しないため)
    private readonly List<Renderer> blinkTargets = new();
    // ノックバック(押し出し＋復帰待ち)中か。ロコモーションステートが移動入力をロックするために参照。
    // KnockbackRoutine が走っている間 true (復帰アニメ待機中も含む)。
    public bool IsKnockingBack => knockbackRoutine != null;
    private float KnockbackDistance => health != null && health.Settings != null
        ? health.Settings.KnockbackDistance
        : DefaultKnockbackDistance;

    // ノックバック復帰直後の追加無敵 (秒)。復帰した瞬間に猶予ゼロで連続被弾しないための猶予
    private float KnockbackRecoveryGrace => health != null && health.Settings != null
        ? health.Settings.KnockbackRecoveryGrace
        : DefaultKnockbackRecoveryGrace;

    /// <summary>DamageType に対応するフェーズ時間設定。要素不足なら末尾、未設定なら null。</summary>
    private PlayerHealthSettings.KnockbackPhaseDurations TimingFor(int damageType) {
        var timings = health != null && health.Settings != null
                    ? health.Settings.KnockbackTimings : null;
        if (timings == null || timings.Length == 0)
            return null;
        return timings[Mathf.Clamp(damageType, 0, timings.Length - 1)];
    }

    private float KnockbackIdleDurationFor(int damageType)
        => TimingFor(damageType)?.Idle ?? DefaultKnockbackIdleDuration;
    private float KnockbackGroundDurationFor(int damageType)
        => TimingFor(damageType)?.Ground ?? DefaultKnockbackGroundDuration;
    private float KnockbackRecoverDurationFor(int damageType)
        => TimingFor(damageType)?.Recover ?? DefaultKnockbackRecoverDuration;

    /// <summary>
    /// DamageType 別の総ノックバック時間 (秒)。
    /// 1 = Idle 時間のみ / 2 以上 = Ground + Recover の合計。
    /// </summary>
    private float KnockbackDurationFor(int damageType) {
        return damageType >= 2
            ? KnockbackGroundDurationFor(damageType) + KnockbackRecoverDurationFor(damageType)
            : KnockbackIdleDurationFor(damageType);
    }

    /// <summary>
    /// DamageType 2 以上で Recover (起き上がり) へ移す正規化時刻 (0-1)。
    /// その DamageType の Ground : Recover 比率で総時間を分割する。両方 0 なら半分で切り替える。
    /// </summary>
    private float RecoverStartNormalized(int damageType) {
        float ground = Mathf.Max(0f, KnockbackGroundDurationFor(damageType));
        float recover = Mathf.Max(0f, KnockbackRecoverDurationFor(damageType));
        float total = ground + recover;
        return total > 0f ? ground / total : 0.5f;
    }

    private void Awake() {
        characterController = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        playerScope = GetComponent<PlayerLifetimeScope>();
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        animationRoot = animator != null ? animator.transform.Find("Root") : null;

        if (wallLayers.value == 0) {
            int wallLayer = LayerMask.NameToLayer("Wall");
            if (wallLayer >= 0)
                wallLayers = 1 << wallLayer;
        }
    }

    private void OnEnable() {
        CombatSystem.Instance.onDamageDealtDetailed.AddListener(OnDamageDealt);
    }

    private void OnDisable() {
        // シーン破棄中に Instance getter で CombatSystem を再生成しないよう TryInstance を使う
        CombatSystem.TryInstance?.onDamageDealtDetailed.RemoveListener(OnDamageDealt);
        StopReaction();
    }

    private void OnDamageDealt(GameObject source, IHealth target, int amount, DamageContext context) {
        // 自分 (プレイヤー) への被弾のみ反応する
        if (!ReferenceEquals(target, health) || health == null || health.IsDead)
            return;

        // ノックバック方向 = 攻撃元から離れる水平方向。source 不明なら後方へ
        Vector3 dir = source != null
                    ? transform.position - source.transform.position
                    : -transform.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;

        int damageType = context.PlayerDamageType;

        // DamageType 別の被弾リアクション:
        //   0 = 軽い被弾。ノックバック(押し出し/硬直)はさせず、移動もロックしない (無敵点滅のみ)。
        //   1 = ノックバック (押し出し → 立ち直り)。
        //   2 = ノックバック + 地面倒れ込み (押し出し → 倒れ → 起き上がり)。
        // ただし、より強いノックバック (Roar の演出ノックバック等) が進行中なら
        // 弱い被弾で上書きしない。上書きすると DamageType が下がり、
        // KnockBack_Ground への遷移条件 (DamageType > 1) が崩れて Idle 止まりになる。
        if (damageType > 0 && !(IsKnockingBack && damageType < activeKnockbackDamageType)) {
            PlayKnockback(dir, KnockbackDistance, damageType);
        }

        if (blinkRoutine != null) {
            StopCoroutine(blinkRoutine);
            // 非表示フレームで中断されると CollectBlinkTargets が隠れたレンダラーを
            // 対象から取りこぼし、透明のまま置き去りになるため必ず再表示してから集め直す
            SetVisible(true);
        }
        blinkRoutine = StartCoroutine(BlinkWhileInvincible());
    }

    /// <summary>
    /// 指定位置から離れる水平方向へ、ダメージを与えずにノックバックさせる。
    /// durationOverride が正なら設定のフェーズ別時間の代わりにその総時間を使う (Roar 演出用など)。
    /// </summary>
    public void PlayKnockbackFrom(Vector3 sourcePosition, float distance, int damageType = 2,
                                  float durationOverride = 0f) {
        Vector3 dir = transform.position - sourcePosition;
        dir.y = 0f;
        PlayKnockback(dir, distance, damageType, durationOverride);
    }

    /// <summary>
    /// 指定した水平方向と距離でノックバックリアクションを開始する。
    /// durationOverride が正なら設定のフェーズ別時間の代わりにその総時間を使う。
    /// </summary>
    public void PlayKnockback(Vector3 direction, float distance, int damageType = 2,
                              float durationOverride = 0f) {
        if (health != null && health.IsDead)
            return;

        direction.y = 0f;
        Vector3 dir = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : -transform.forward;
        int resolvedDamageType = Mathf.Max(1, damageType);

        BeginKnockbackAnimation(resolvedDamageType);
        if (knockbackRoutine != null)
            StopCoroutine(knockbackRoutine);
        activeKnockbackDamageType = resolvedDamageType;
        float duration = durationOverride > 0f
            ? durationOverride
            : KnockbackDurationFor(resolvedDamageType);
        knockbackRoutine = StartCoroutine(
            KnockbackRoutine(dir, resolvedDamageType, Mathf.Max(0f, distance), duration));
    }

    /// <summary>
    /// Locomotion 復帰までの合計を指定秒数 (既定は DamageType 1 = Idle 時間 / 2 = Ground + Recover の合計) ちょうどにする。
    /// その総時間いっぱいをかけてイーズアウトで押し出し、末尾で強制的に移動へ復帰させる。
    /// アニメが残っていても総時間ちょうどでロックを解除する (＝厳密にこの秒数で動けるようになる)。
    /// </summary>
    private IEnumerator KnockbackRoutine(Vector3 dir, int damageType, float distance, float duration) {
        float total = Mathf.Max(0.05f, duration);
        knockbackMovementActive = true;
        hitWallDuringKnockback = false;

        float startTime = Time.time;
        // 線形減速で距離 D を時間 T で進む初速: v0 = 2D / T (押し出しを総時間全体へ引き伸ばす)
        float initialSpeed = 2f * distance / total;
        bool wallReacted   = false;
        bool recoverFired  = false;
        // その DamageType の Ground : Recover 比率で「倒れ込み → 起き上がり」の切り替え時刻を決める
        float recoverStart = RecoverStartNormalized(damageType);
        while (Time.time - startTime < total) {
            float e = Mathf.Clamp01((Time.time - startTime) / total);

            // 押し出し (壁に当たったら以降は止める)
            if (!hitWallDuringKnockback && characterController != null && characterController.enabled) {
                float speed = Mathf.Lerp(initialSpeed, 0f, e);
                characterController.Move(dir * (speed * Time.deltaTime));
            }

            // 壁ヒット演出 (1回だけ。倒れ姿勢へ傾ける)
            if (hitWallDuringKnockback && !wallReacted && animator != null) {
                animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, true);
                animator.SetTrigger(PlayerAnimator.KnockBackHitWallParam);
                SetWallHitRootRotation(true);
                wallReacted = true;
            }

            // DamageType 2: その DamageType の Ground 時間経過後に「倒れ込み → 起き上がり (Recover)」へ移す (壁でない場合)。
            // Recover は Animator Controller の Any State 遷移で受ける。
            // Ground へのブレンド中でもトリガーを取りこぼさず、Controller 側の遷移時間も適用できる。
            if (damageType >= 2 && !hitWallDuringKnockback && !recoverFired && e >= recoverStart && animator != null) {
                animator.ResetTrigger(PlayerAnimator.RecoverFromKnockBackParam);
                animator.SetTrigger(PlayerAnimator.RecoverFromKnockBackParam);
                recoverFired = true;
            }

            yield return null;
        }
        knockbackMovementActive = false;

        // durationOverride が Recover クリップより短くても、起き上がりを途中で切って
        // Locomotion を遷移先にしない。Recover の最終姿勢まで再生してから復帰する。
        if (recoverFired && !hitWallDuringKnockback && animator != null) {
            // Recover ステートが遷移競合などで完了しない場合でも、
            // IsKnockingBack (入力ロック) が永久に true にならないよう上限を設ける。
            float recoverTimeout = Mathf.Max(
                MinimumRecoverWaitTimeout,
                KnockbackRecoverDurationFor(damageType) * 2f + 0.5f);
            yield return WaitForRecoverAnimation(recoverTimeout);
        }

        // Recover 完了後に Locomotion へ短くブレンドしてロックを解除する。
        if (animator != null) {
            SetWallHitRootRotation(false);
            ResetLowerPriorityMotionTriggers();
            animator.ResetTrigger(PlayerAnimator.RecoverFromKnockBackParam);
            animator.ResetTrigger(PlayerAnimator.KnockBackHitWallParam);
            animator.SetBool(PlayerAnimator.IsKnockBackParam, false);
            animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, false);
            PlayLocomotionWithPriority(DefaultLocomotionBlend);
        }

        // 復帰した瞬間に猶予ゼロで次弾を食らわないよう、無敵を少しだけ延長する
        // (ManaBurst 連打中など、固定無敵がノックバック中に切れているケースへの救済)
        health?.ExtendInvincibility(KnockbackRecoveryGrace);
        activeKnockbackDamageType = 0;
        knockbackRoutine = null;
    }

    private IEnumerator WaitForRecoverAnimation(float timeout) {
        // Animator.speed=0 のヒットストップ中でもフェイルセーフを進められるよう、
        // Time.time ではなく unscaledTime を使う。
        float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeout);

        // Any State 遷移が Animator に反映されるまで待つ。別遷移に奪われた場合は直接 Recover を選ぶ。
        yield return null;
        if (animator == null)
            yield break;
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : default;
        if (current.fullPathHash != KnockbackRecoverState && next.fullPathHash != KnockbackRecoverState) {
            animator.CrossFadeInFixedTime(KnockbackRecoverState, 0.1f, 0, 0f);
            yield return null;
        }

        // 遷移中は Next、遷移完了後は Current の Recover が1周するまで待つ。
        while (animator != null && Time.unscaledTime < deadline) {
            bool transitioning = animator.IsInTransition(0);
            current = animator.GetCurrentAnimatorStateInfo(0);
            next = transitioning ? animator.GetNextAnimatorStateInfo(0) : default;

            if (transitioning && next.fullPathHash == KnockbackRecoverState) {
                yield return null;
                continue;
            }
            if (current.fullPathHash == KnockbackRecoverState && current.normalizedTime < 1f) {
                yield return null;
                continue;
            }
            break;
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit) {
        if (!knockbackMovementActive || hit.collider == null)
            return;

        int hitLayerMask = 1 << hit.collider.gameObject.layer;
        if ((wallLayers.value & hitLayerMask) == 0)
            return;
        if (Mathf.Abs(hit.normal.y) > wallNormalMaxY)
            return;

        hitWallDuringKnockback = true;
        if (animator != null)
            animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, true);
    }

    private void BeginKnockbackAnimation(int damageType) {
        // 攻撃・回避などの Action と残留トリガーを先に破棄し、Knockback を最優先にする。
        playerScope?.StateMachine?.ChangeActionState(new StatePlayerNull());
        playerScope?.SetScaryWalkLowerIdle(false);

        if (animator == null) return;

        // 下半身レイヤーも即座に外して、チャージ歩行が Knockback を上書きしないようにする。
        if (animator.layerCount > 1)
            animator.SetLayerWeight(1, 0f);

        SetWallHitRootRotation(false);
        ResetLowerPriorityMotionTriggers();
        animator.ResetTrigger(PlayerAnimator.ToMoveParam);
        animator.ResetTrigger(PlayerAnimator.KnockBackStartParam);
        animator.ResetTrigger(PlayerAnimator.RecoverFromKnockBackParam);
        animator.ResetTrigger(PlayerAnimator.KnockBackHitWallParam);
        animator.SetInteger(PlayerAnimator.DamageTypeParam, Mathf.Max(0, damageType));
        animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, false);
        animator.SetBool(PlayerAnimator.IsKnockBackParam, true);

        // DamageType 2 (Roar 等の地面へ倒れ込む吹き飛び) は KnockBack_Idle を経由すると、
        // 短いリアクション時間の大半が遷移ブレンドに使われて Ground がほぼ見えない。
        // 対象ステートへ直接入り、吹き飛び開始フレームから KnockBack_Ground を再生する。
        if (damageType >= 2 && animator.HasState(0, KnockbackGroundState))
            animator.CrossFadeInFixedTime(KnockbackGroundState, 0.05f, 0, 0f);
        else
            animator.SetTrigger(PlayerAnimator.KnockBackStartParam);
    }

    private void ResetLowerPriorityMotionTriggers() {
        animator.ResetTrigger(PlayerAnimator.AttackParam);
        animator.ResetTrigger(PlayerAnimator.ChargeParam);
        animator.ResetTrigger(PlayerAnimator.ComboParam);
        animator.ResetTrigger(PlayerAnimator.DamageParam);
        animator.ResetTrigger(PlayerAnimator.JumpParam);
        animator.ResetTrigger(PlayerAnimator.JumpStartParam);
        animator.ResetTrigger(PlayerAnimator.ScaryWalkParam);
    }

    private void PlayLocomotionWithPriority(float blendDuration) {
        animator.ResetTrigger(PlayerAnimator.KnockBackStartParam);
        animator.ResetTrigger(PlayerAnimator.ToMoveParam);
        animator.SetBool(PlayerAnimator.IsRollParam, false);

        // Knockback ステート側の Exit Time を待たず、終了フレームで Locomotion を選ぶ。
        // blendDuration ぶんかけて溶かし込むことで、起き上がり姿勢から立ちへ滑らかに戻す。
        if (animator.HasState(0, LocomotionState))
            animator.CrossFadeInFixedTime(LocomotionState, Mathf.Max(DefaultLocomotionBlend, blendDuration), 0, 0f);
        else
            animator.SetTrigger(PlayerAnimator.ToMoveParam);
    }

    private void SetWallHitRootRotation(bool active) {
        if (animationRoot == null) return;

        if (active) {
            if (wallHitRootRotationApplied) return;
            animationRootRotationBeforeWallHit = animationRoot.localRotation;
            animationRoot.localRotation =
                animationRootRotationBeforeWallHit * Quaternion.Euler(90f, 0f, 0f);
            wallHitRootRotationApplied = true;
            return;
        }

        if (!wallHitRootRotationApplied) return;
        animationRoot.localRotation = animationRootRotationBeforeWallHit;
        wallHitRootRotationApplied = false;
    }

    /// <summary>PlayerHealth.IsHitInvincible の間レンダラーを点滅させ、終了時に必ず再表示する。
    /// デバッグ無敵 (常時 ON) を条件にすると点滅が永遠に終わらないため、被弾無敵時間のみを見る。</summary>
    private IEnumerator BlinkWhileInvincible() {
        CollectBlinkTargets();
        bool visible = true;
        float nextToggleAt = Time.time + blinkInterval;

        // 無敵判定は毎フレーム確認し、切れた瞬間に必ず再表示して終える。
        // (interval 待ちで判定すると、無敵終了後も最大 interval ぶん点滅が残り
        //  「無敵に見えるのに被弾する」ズレが生まれる)
        while (health != null && health.IsHitInvincible) {
            if (Time.time >= nextToggleAt) {
                visible = !visible;
                SetVisible(visible);
                nextToggleAt = Time.time + blinkInterval;
            }
            yield return null;
        }

        SetVisible(true);
        blinkRoutine = null;
    }

    private void CollectBlinkTargets() {
        blinkTargets.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(false)) {
            // 残像・パーティクル等の VFX は点滅させない
            if (r is TrailRenderer || r is ParticleSystemRenderer || r is LineRenderer)
                continue;
            if (r.enabled)
                blinkTargets.Add(r);
        }
    }

    private void SetVisible(bool visible) {
        foreach (Renderer r in blinkTargets)
            if (r != null) r.enabled = visible;
    }

    private void StopReaction() {
        knockbackMovementActive = false;
        hitWallDuringKnockback = false;
        activeKnockbackDamageType = 0;
        SetWallHitRootRotation(false);
        if (knockbackRoutine != null) { StopCoroutine(knockbackRoutine); knockbackRoutine = null; }
        if (blinkRoutine != null)     { StopCoroutine(blinkRoutine);     blinkRoutine = null; }
        SetVisible(true);
        if (animator != null) {
            animator.SetBool(PlayerAnimator.IsKnockBackParam, false);
            animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, false);
        }
    }
}
