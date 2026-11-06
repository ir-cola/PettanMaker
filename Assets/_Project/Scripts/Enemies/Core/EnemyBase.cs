/**===========================================================================

 File   : 敵AIクラス [EnemyAI.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22

 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/**===========================================================================
  敵AIクラス
  プレイヤーを検知して追跡・攻撃する基底クラス。
  共通フィールド/ユーティリティをここに集約し、サブクラスで override する。
===========================================================================**/
public abstract class EnemyBase : MonoBehaviour {

    [Header("共通設定")]
    [Tooltip("難易度なしの直接指定。settingsSet が割り当てられている場合はそちらが優先される。")]
    [SerializeField] protected EnemySettings settings;

    [Header("難易度別設定（任意）")]
    [Tooltip("割り当てると DifficultyManager.Current に応じて Easy/Normal/Hard の設定を自動選択する。")]
    [SerializeField] protected EnemySettingsSet settingsSet;

    [Header("Animator")]
    [SerializeField] protected EnemyAnimatorBridge animatorBridge;

    [Header("被弾ヒットストップ")]
    [SerializeField] protected AnimationHitStop hitStop;

    [Header("VFX - 共通発見")]
    [Tooltip("VFX_EnemyCommon_Discover をアタッチする。")]
    [SerializeField] private GameObject discoverVFXPrefab;
    [Tooltip("発見VFXの表示位置。未設定なら敵本体の位置。")]
    [SerializeField] private Transform discoverVFXTargetPoint;
    [SerializeField, Min(0f)] private float discoverVFXLifetime = 3f;

    // ── Animator は bridge 経由で取得（bridge が唯一のオーナー） ──
    protected Animator Animator => animatorBridge?.Animator;

    // ── settings から直読みする保護プロパティ（唯一の真実 = EnemySettings） ──
    // フォールバック値はコード上の安全網。実運用は必ず settings をアサインすること。
    protected float DetectionRange => settings != null ? settings.DetectionRange : 10f;
    protected float AttackRange    => settings != null ? settings.AttackRange    : 1.5f;
    protected float AttackInterval => settings != null ? settings.AttackInterval : 1.5f;
    protected float AlertDuration  => settings != null ? settings.AlertDuration  : 0.4f;
    protected float Knockback      => settings != null ? settings.Knockback      : 0f;
    protected float KnockbackDuration => settings != null ? settings.KnockbackDuration : 0.12f;

    // コンポーネント参照
    protected NavMeshAgent navMeshAgent;
    protected EnemyHealth enemyHealth;
    protected Transform playerTransform;
    protected IHealth playerHealth;

    // 内部変数
    protected float attackTimer = 0f;
    protected float alertTimer = 0f;
    protected float cooldownTimer = 0f;
    protected bool isAlerting = false;
    protected bool isAttacking = false;
    protected bool hasDetectedPlayer = false;
    private Vector3 wanderOrigin;
    private bool hasWanderOrigin = false;
    private float wanderTimer = 0f;
    private bool searchingAfterPlayerDeath;
    private Coroutine knockbackCoroutine;

    // 被弾ヒットストップ：Animator だけでなく移動・AI・アクションも実時間で一瞬凍結する。
    private float hitStopTimer = 0f;
    public bool IsHitStopped => hitStopTimer > 0f;

    // 被弾ビジュアル演出（ブレ＋スカッシュ）。設定はすべて EnemySettings が持ち、
    // モデル側 Transform（Animator）の localPosition/localScale を LateUpdate で操作する。
    // ルート（NavMeshAgent が動かす）は触らないので移動・当たり判定・NavMesh と非干渉。
    private Transform hitFxTarget;              // 揺らす／潰す対象（モデル側）
    // ブレ
    private Vector3 shakeBaseLocalPos;
    private bool shakeHasBase;
    private float shakeTimer;
    private float shakeDuration;
    private float shakeAmplitudeMul = 1f;
    private float shakeNextSampleTime;
    private Vector3 shakeOffset;
    // スカッシュ
    private Vector3 squashBaseScale;
    private bool squashHasBase;
    private float squashTimer;
    private float squashDuration;
    private Vector3 squashedScale;
    private const float SquashBackC1 = 1.70158f;   // OutBack オーバーシュート係数

    // 旋回のイージング用（SmoothDampAngle の状態）
    private float currentYaw;
    private float yawVelocity;
    private bool yawInitialized;

    // Action パターン（Boss01 の B1_ActionController 相当）
    protected EnemyActionController actionController;

    protected enum State { Idle, Chase, Attack, Dead, Flee, Stan, Search }
    protected State currentState = State.Idle;

    // EnemyAction クラスから参照するパブリック API
    public NavMeshAgent Agent       => navMeshAgent;
    public Transform    PlayerTransform => playerTransform;
    public IHealth      PlayerHealth    => playerHealth;
    public Animator     EnemyAnimator   => animatorBridge?.Animator;
    public EnemyAnimatorBridge AnimatorBridge => animatorBridge;
    public int          AttackDamage    => settings?.AttackDamage ?? 10;
    public int          AttackDamageType => Mathf.Max(0, settings?.DamageType ?? 1);
    public void SetCooldown(float duration) { cooldownTimer = duration; }

    // デバッグ用パブリック API（EnemyDebugManager / エディタ拡張から参照）
    public EnemySettings Settings => settings;
    public string CurrentStateName => currentState.ToString();
    public EnemyActionController ActionController => actionController;
    protected bool HasDetectedPlayer => hasDetectedPlayer;
    public float PlayerDistance => playerTransform != null
        ? Vector3.Distance(transform.position, playerTransform.position) : -1f;
    /// <summary>サブクラスで override してモーション名を返す（各エネミーの MotionState など）。</summary>
    public virtual string DebugMotionName => "-";

    /**=======================================================================
      初期化
    =======================================================================**/
    protected virtual void Awake() {
        // 難易度別 SO が割り当てられていれば現在の難易度で解決して settings を差し替える。
        if (settingsSet != null) {
            var resolved = settingsSet.Resolve(DifficultyManager.Current);
            if (resolved != null) settings = resolved;
        }

        if (settings == null) {
            Debug.LogError($"[{GetType().Name}] EnemySettings または EnemySettingsSet を割り当ててください。HP を含む敵設定は ScriptableObject から読み込みます。", this);
            enabled = false;
            return;
        }

        navMeshAgent = GetComponent<NavMeshAgent>();
        navMeshAgent.updateRotation = false;
        navMeshAgent.angularSpeed = 0f;
        enemyHealth = GetComponent<EnemyHealth>();
        if (animatorBridge == null)
            animatorBridge = GetComponentInChildren<EnemyAnimatorBridge>(true)
                          ?? gameObject.AddComponent<EnemyAnimatorBridge>();
        animatorBridge.Initialize(
            settings?.SpeedParameter ?? "Speed",
            settings?.AlertParameter ?? "IsAlert");

        // 被弾ヒットストップ（bridge が持つ Animator を共有して一瞬止める）
        if (hitStop == null)
            hitStop = GetComponent<AnimationHitStop>() ?? gameObject.AddComponent<AnimationHitStop>();
        hitStop.SetAnimator(animatorBridge.Animator);
        if (settings != null)
            hitStop.Configure(settings.HitStopDuration, settings.HitStopFrozenSpeed);

        // 被弾ブレ／スカッシュの対象＝モデル側 Transform（Animator）。
        // ルート（NavMeshAgent）は触らない。演出ロジックは EnemyBase が LateUpdate で直接処理する。
        hitFxTarget = animatorBridge.Animator != null
            ? animatorBridge.Animator.transform : transform;

        // HP は settings（難易度解決済み）から初期化する。
        enemyHealth.Initialize(settings.MaxHP);

        actionController = new EnemyActionController();
    }

    /**=======================================================================
      開始処理
    =======================================================================**/
    protected virtual void Start() {
        navMeshAgent.stoppingDistance = AttackRange;
        wanderOrigin = transform.position;
        hasWanderOrigin = true;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) {
            playerTransform = player.transform;
            playerHealth = player.GetComponent<IHealth>();
        }

        enemyHealth.onDied.AddListener(OnDied);
        // 被弾リアクション（ヒットストップ・ブレ・スカッシュ・ノックバック）は
        // ダメージ量と攻撃元を持つ OnDamageDealt に一本化する。Enemy01〜04 共通。
        // 購読した実体を保持しておき、破棄時は必ずこの実体から解除する
        // （DontDestroyOnLoad の CombatSystem に対し FindObjectOfType が null を返しても漏れないように）。
        _combatSystem = CombatSystem.Instance;
        if (_combatSystem != null)
            _combatSystem.onDamageDealt.AddListener(OnDamageDealt);
    }

    private CombatSystem _combatSystem;

    /// <summary>被弾時に移動・AI・アクションを実時間で一瞬凍結する。Animator は AnimationHitStop が別途止める。</summary>
    private void BeginHitStopFreeze(float freezeDuration) {
        if (freezeDuration <= 0f) return;

        hitStopTimer = freezeDuration;
        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh) {
            navMeshAgent.velocity = Vector3.zero;
            navMeshAgent.isStopped = true;
        }
    }

    /// <summary>ダメージ量から被弾リアクションのスケール倍率(0.5〜2.0 等)を求める。</summary>
    private float ComputeHitMultiplier(int amount) {
        if (settings == null || !settings.HitScaleByDamage) return 1f;
        float raw = amount / Mathf.Max(1f, settings.HitReferenceDamage);
        return Mathf.Clamp(raw, settings.HitScaleRange.x, settings.HitScaleRange.y);
    }

    /// <summary>ダメージ倍率に応じてヒットストップ（Animator凍結＋AI/移動凍結）を掛ける。</summary>
    private void ApplyScaledHitStop(float multiplier, float weight) {
        float baseDur = settings != null ? settings.HitStopDuration : 0.08f;
        float dur = baseDur * multiplier;
        if (dur <= 0f) return;

        if (hitStop != null) {
            float frozen = settings != null
                ? Mathf.Lerp(settings.HitStopFrozenSpeedLight, settings.HitStopFrozenSpeed, weight)
                : 0f;
            hitStop.Configure(dur, frozen);
            hitStop.HitStop(dur);
        }
        BeginHitStopFreeze(dur);
    }

    // ─── 被弾ビジュアル演出（ブレ＋スカッシュ）：設定は EnemySettings、対象はモデル Transform ───

    /// <summary>被弾ブレを開始する。amplitudeMul はダメージスケール倍率。</summary>
    private void TriggerHitShake(float amplitudeMul) {
        if (settings == null || hitFxTarget == null || settings.HitShakeAmplitude <= 0f) return;

        // ブレ時間：未指定(0)ならヒットストップ時間に合わせ、ダメージ倍率でスケールする。
        float baseDur = settings.HitShakeDuration > 0f ? settings.HitShakeDuration : settings.HitStopDuration;
        float dur = baseDur * amplitudeMul;
        if (dur <= 0f) return;

        if (!shakeHasBase) { shakeBaseLocalPos = hitFxTarget.localPosition; shakeHasBase = true; }
        shakeTimer = dur;
        shakeDuration = dur;
        shakeAmplitudeMul = Mathf.Max(0f, amplitudeMul);
        shakeNextSampleTime = 0f;   // 次の LateUpdate で即サンプルし直す
    }

    /// <summary>被弾スカッシュを開始する。worldHitDir=攻撃方向、strength=変形強度(0..1)。</summary>
    private void TriggerHitSquash(Vector3 worldHitDir, float strength) {
        if (settings == null || !settings.HitSquashEnabled || hitFxTarget == null) return;
        if (settings.HitSquashDuration <= 0f || strength <= 0f) return;

        if (!squashHasBase) { squashBaseScale = hitFxTarget.localScale; squashHasBase = true; }

        // 攻撃方向をローカル水平へ変換し、各軸への寄与(0..1)を求める（軸沿いつぶしの近似）。
        Vector3 localDir = hitFxTarget.InverseTransformDirection(worldHitDir);
        localDir.y = 0f;
        if (localDir.sqrMagnitude < 1e-4f) localDir = Vector3.forward;
        localDir.Normalize();
        float ax = localDir.x * localDir.x;
        float az = localDir.z * localDir.z;

        float along = Mathf.Lerp(1f, settings.HitSquashAlong, strength);
        float perp  = Mathf.Lerp(1f, settings.HitSquashPerp, strength);
        float vy    = Mathf.Lerp(1f, settings.HitSquashVertical, strength);
        float sx = Mathf.Lerp(perp, along, ax);
        float sz = Mathf.Lerp(perp, along, az);

        squashedScale = new Vector3(squashBaseScale.x * sx, squashBaseScale.y * vy, squashBaseScale.z * sz);
        hitFxTarget.localScale = squashedScale;
        squashTimer = settings.HitSquashDuration;
        squashDuration = settings.HitSquashDuration;
    }

    /// <summary>ブレ／スカッシュは Animator がポーズを書き込んだ後（LateUpdate）に乗せる。</summary>
    protected virtual void LateUpdate() {
        UpdateHitShake();
        UpdateHitSquash();
    }

    private void UpdateHitShake() {
        if (shakeTimer <= 0f) {
            if (shakeHasBase) {
                if (hitFxTarget != null) hitFxTarget.localPosition = shakeBaseLocalPos;
                shakeOffset = Vector3.zero;
                shakeHasBase = false;
            }
            return;
        }
        if (hitFxTarget == null) { shakeTimer = 0f; return; }

        shakeTimer -= Time.unscaledDeltaTime;
        float envelope = shakeDuration > 0f ? Mathf.Clamp01(shakeTimer / shakeDuration) : 1f;

        if (Time.unscaledTime >= shakeNextSampleTime) {
            Vector2 dir = Random.insideUnitCircle;
            shakeOffset = new Vector3(dir.x, dir.y * 0.6f, 0f);   // 縦揺れは控えめ
            float freq = Mathf.Max(1f, settings.HitShakeFrequency);
            shakeNextSampleTime = Time.unscaledTime + 1f / freq;
        }

        hitFxTarget.localPosition = shakeBaseLocalPos
            + shakeOffset * (settings.HitShakeAmplitude * shakeAmplitudeMul * envelope);
    }

    private void UpdateHitSquash() {
        if (squashTimer <= 0f) {
            if (squashHasBase) {
                if (hitFxTarget != null) hitFxTarget.localScale = squashBaseScale;
                squashHasBase = false;
            }
            return;
        }
        if (hitFxTarget == null) { squashTimer = 0f; return; }

        squashTimer -= Time.unscaledDeltaTime;
        float p = squashDuration > 0f ? Mathf.Clamp01(1f - squashTimer / squashDuration) : 1f;
        float ease = EaseOutBack(p);   // 潰れ→元→少し反対へ弾んで収束
        hitFxTarget.localScale = Vector3.LerpUnclamped(squashedScale, squashBaseScale, ease);

        if (squashTimer <= 0f) {
            hitFxTarget.localScale = squashBaseScale;
            squashHasBase = false;
        }
    }

    private static float EaseOutBack(float t) {
        float c1 = SquashBackC1;
        float c3 = c1 + 1f;
        float tm = t - 1f;
        return 1f + c3 * tm * tm * tm + c1 * tm * tm;
    }

    protected virtual void OnDestroy() {
        // 購読した実体から解除する。取りこぼしの保険として現存インスタンスでも試みる。
        CombatSystem combatSystem = _combatSystem != null ? _combatSystem : CombatSystem.Instance;
        if (combatSystem == null) combatSystem = FindObjectOfType<CombatSystem>();
        if (combatSystem != null)
            combatSystem.onDamageDealt.RemoveListener(OnDamageDealt);
    }

    private void OnDamageDealt(GameObject source, IHealth target, int amount) {
        if (target != enemyHealth || amount <= 0) return;
        // トドメ（この一撃で死亡）は死亡演出に任せてリアクションはスキップする。
        if (currentState == State.Dead) return;

        // 攻撃方向（攻撃元→敵の水平方向）。スカッシュ・ノックバック共通で使う。
        Vector3 sourcePosition;
        if (source != null)
            sourcePosition = source.transform.position;
        else if (playerTransform != null)
            sourcePosition = playerTransform.position;
        else
            sourcePosition = transform.position + transform.forward;

        Vector3 direction = transform.position - sourcePosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            direction = -transform.forward;
        direction.Normalize();

        // ダメージ量からヒット強度を算出（軽い一撃と重い一撃で手応えを変える）。
        float multiplier = ComputeHitMultiplier(amount);
        float weight = settings != null && settings.HitScaleRange.y > settings.HitScaleRange.x
            ? Mathf.InverseLerp(settings.HitScaleRange.x, settings.HitScaleRange.y, multiplier)
            : 0.5f;

        // ① ヒットストップ（Animator凍結＋AI/移動凍結）
        ApplyScaledHitStop(multiplier, weight);

        // ② 被弾ブレ（振幅もダメージでスケール）
        TriggerHitShake(multiplier);

        // ③ 方向つぶしスカッシュ（強度もダメージでスケール）
        {
            float minStrength = settings != null ? settings.HitSquashMinStrength : 0.35f;
            TriggerHitSquash(direction, Mathf.Lerp(minStrength, 1f, weight));
        }

        // ④ ノックバック（従来通り。Knockback<=0 や NavMesh 不在時はスキップ）
        if (Knockback > 0f && navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh) {
            if (knockbackCoroutine != null)
                StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = StartCoroutine(KnockbackRoutine(direction, Knockback, KnockbackDuration));
        }
    }

    private IEnumerator KnockbackRoutine(Vector3 direction, float distance, float duration) {
        float elapsed = 0f;
        float moved = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration && currentState != State.Dead) {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
                break;

            // ヒットストップ中はノックバックも止め、凍結解除後に「重み」を残して弾ける。
            if (hitStopTimer > 0f) {
                yield return null;
                continue;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedDistance = distance * (1f - Mathf.Pow(1f - t, 2f));
            float step = easedDistance - moved;
            moved = easedDistance;

            navMeshAgent.Move(direction * step);
            yield return null;
        }

        knockbackCoroutine = null;
    }

    /**=======================================================================
      更新処理
      ActionController が Action を保持中はステートマシンを止めて Action に委譲する。
      ExecuteState の後に UpdateAnimator を呼ぶことで、
      サブクラス内の冗長な呼び出しを不要にする。
    =======================================================================**/
    protected virtual void Update() {
        if (currentState == State.Dead) return;

        // Player死亡後は戦闘を即座に中断し、Playerを追わず歩いて周囲を探索する。
        if (playerHealth != null && playerHealth.IsDead) {
            UpdatePlayerDeathSearch();
            return;
        }
        if (searchingAfterPlayerDeath)
            ExitPlayerDeathSearch();

        // 被弾ヒットストップ中は AI・移動・アクションを凍結する（実時間で計測）。
        // Animator は AnimationHitStop が並行して止めるためポーズが保持される。
        if (hitStopTimer > 0f) {
            hitStopTimer -= Time.unscaledDeltaTime;
            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh) {
                navMeshAgent.velocity = Vector3.zero;
                if (hitStopTimer <= 0f)
                    navMeshAgent.isStopped = false;
            }
            return;
        }

        // Action 実行中はステートマシンを止めて Action に委譲する
        if (actionController.HasAction) {
            actionController.Update();
            UpdateAnimator(0f, isAlerting);
            return;
        }

        if (playerTransform == null) return;

        float distance = Vector3.Distance(transform.position, playerTransform.position);
        UpdateState(distance);
        ExecuteState(distance);
        UpdateAnimator(navMeshAgent.velocity.magnitude, isAlerting);
    }

    /**=======================================================================
      ステート更新
    =======================================================================**/
    protected virtual void UpdateState(float distance) {
        if (distance <= AttackRange)
            currentState = State.Attack;
        else if (distance <= DetectionRange)
            currentState = State.Chase;
        else
            currentState = State.Idle;
    }

    /**=======================================================================
      ステート実行
    =======================================================================**/
    protected virtual void ExecuteState(float distance) {
        switch (currentState) {
            case State.Idle:
                navMeshAgent.ResetPath();
                break;

            case State.Chase:
                navMeshAgent.SetDestination(playerTransform.position);
                break;

            case State.Attack:
                navMeshAgent.ResetPath();
                FacePlayer();
                attackTimer += Time.deltaTime;
                if (attackTimer >= AttackInterval) {
                    attackTimer = 0f;
                    PerformAttack();
                }
                break;

            case State.Flee:
                // サブクラスで ExecuteState を override して処理する
                break;
        }
    }

    /**=======================================================================
      攻撃処理（近接デフォルト実装。サブクラスで override する）
    =======================================================================**/
    protected virtual void PerformAttack() {
        CombatSystem.Instance.Damage(
            playerHealth,
            AttackDamage,
            default(DamageContext).WithPlayerDamageType(AttackDamageType),
            this);
        Debug.Log($"[{gameObject.name}] プレイヤーに {AttackDamage} ダメージ！");
    }

    /**=======================================================================
      死亡時コールバック
    =======================================================================**/
    protected virtual void OnDied() {
        if (knockbackCoroutine != null) {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }
        actionController.Abort();
        currentState = State.Dead;
        navMeshAgent.ResetPath();
        navMeshAgent.enabled = false;
    }

    private void UpdatePlayerDeathSearch() {
        if (!searchingAfterPlayerDeath) {
            searchingAfterPlayerDeath = true;
            actionController?.Abort();
            hitStopTimer = 0f;
            attackTimer = 0f;
            alertTimer = 0f;
            cooldownTimer = 0f;
            isAlerting = false;
            isAttacking = false;
            hasDetectedPlayer = false;
            currentState = State.Search;
            wanderOrigin = transform.position;
            hasWanderOrigin = true;
            wanderTimer = 0f;

            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh) {
                navMeshAgent.ResetPath();
                navMeshAgent.isStopped = false;
            }
            animatorBridge?.SetAlert(false);
        }

        ExecutePreAlertWander(navMeshAgent != null ? navMeshAgent.speed : 0f, force: true);
        float speed = navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh
            ? navMeshAgent.velocity.magnitude
            : 0f;
        UpdateAnimator(speed, false);
    }

    private void ExitPlayerDeathSearch() {
        searchingAfterPlayerDeath = false;
        currentState = State.Idle;
        wanderOrigin = transform.position;
        wanderTimer = 0f;
    }

    // ─── 共通ユーティリティ（サブクラスの重複コードをここに集約） ────────────

    public void FacePlayer() {
        if (playerTransform == null) return;
        Vector3 lookDir = playerTransform.position - transform.position;
        lookDir.y = 0f;
        FaceDirection(lookDir);
    }

    protected void FaceMoveDirection(float turnSpeedOverride = -1f) {
        Vector3 v = navMeshAgent.desiredVelocity.sqrMagnitude > 0.01f
            ? navMeshAgent.desiredVelocity
            : navMeshAgent.velocity;
        v.y = 0f;
        FaceDirection(v, turnSpeedOverride);
    }

    protected void FaceDirection(Vector3 direction, float turnSpeedOverride = -1f) {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.01f) return;

        if (!yawInitialized) {
            currentYaw = transform.eulerAngles.y;
            yawInitialized = true;
        }

        float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float turnSpeed = turnSpeedOverride > 0f
            ? turnSpeedOverride
            : settings != null ? settings.TurnSpeed : 360f;

        // TurnSpeed<=0 は即時（従来どおり）。バンクも掛けず素直に向ける。
        if (turnSpeed <= 0f) {
            currentYaw = targetYaw;
            yawVelocity = 0f;
            transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
            return;
        }

        float smoothTime = settings != null ? settings.TurnSmoothTime : 0.12f;
        if (smoothTime <= 0f) {
            // イージング無効時は従来の等速旋回を維持する。
            currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, turnSpeed * Time.deltaTime);
            yawVelocity = 0f;
        } else {
            // 減速しながら向く（yawVelocity は度/秒。バンク量の元にも使う）。
            currentYaw = Mathf.SmoothDampAngle(
                currentYaw, targetYaw, ref yawVelocity, smoothTime, turnSpeed, Time.deltaTime);
        }

        // 進行方向（旋回方向）へロールして傾ける。forward は変わらないので移動計算に影響しない。
        float bankAngle = 0f;
        float maxBank = settings != null ? settings.TurnBankAngle : 0f;
        if (maxBank > 0f)
            bankAngle = Mathf.Clamp(-yawVelocity * 0.05f, -maxBank, maxBank);

        transform.rotation = Quaternion.AngleAxis(currentYaw, Vector3.up)
                           * Quaternion.AngleAxis(bankAngle, Vector3.forward);
    }

    public void UpdateAnimator(float speed, bool isAlert) {
        animatorBridge?.SetSpeed(speed);
        animatorBridge?.SetAlert(isAlert);
    }

    protected bool ExecutePreAlertWander(float speed, bool force = false) {
        if (settings == null || (!force && !settings.PreAlertWanderEnabled)) return false;
        if (hasDetectedPlayer || isAlerting) return false;
        if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) return false;

        if (!hasWanderOrigin) {
            wanderOrigin = transform.position;
            hasWanderOrigin = true;
        }

        navMeshAgent.speed = settings.PreAlertWanderSpeed > 0f
            ? settings.PreAlertWanderSpeed
            : speed;

        bool moving = navMeshAgent.hasPath
            && !navMeshAgent.pathPending
            && navMeshAgent.remainingDistance > settings.PreAlertWanderArriveDistance;
        if (moving) {
            FaceMoveDirection(settings.PreAlertWanderTurnSpeed);
            return true;
        }

        navMeshAgent.ResetPath();
        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return true;

        wanderTimer = settings.PreAlertWanderInterval;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.01f) forward = Vector3.forward;
        forward.Normalize();

        float angle = Random.Range(-settings.PreAlertWanderTurnAngle, settings.PreAlertWanderTurnAngle);
        float distance = settings.PreAlertWanderRadius
            * Random.Range(settings.PreAlertWanderMinStepRatio, 1f);
        Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
        Vector3 target = transform.position + direction * distance;

        Vector3 fromOrigin = target - wanderOrigin;
        fromOrigin.y = 0f;
        if (fromOrigin.sqrMagnitude > settings.PreAlertWanderRadius * settings.PreAlertWanderRadius)
            target = wanderOrigin + fromOrigin.normalized * settings.PreAlertWanderRadius;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, settings.PreAlertWanderRadius, NavMesh.AllAreas))
            navMeshAgent.SetDestination(hit.position);

        return true;
    }

    protected IEnumerator WaitFacing(float duration) {
        float elapsed = 0f;
        while (elapsed < duration) {
            if (currentState == State.Dead) yield break;
            FacePlayer();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /**=======================================================================
      発見演出タイマーを処理する。
      true を返したら ExecuteState 側は早期 return すること。
      UpdateAnimator は base Update が後続で呼ぶため、ここでは呼ばない。
    =======================================================================**/
    protected bool HandleAlertTimer() {
        if (alertTimer > 0f) {
            alertTimer -= Time.deltaTime;
            navMeshAgent.ResetPath();
            FacePlayer();
            isAlerting = true;
            return true;
        }
        if (isAlerting) isAlerting = false;
        return false;
    }

    /**=======================================================================
      プレイヤー発見時処理
      サブクラスで override して CurrentMotion を設定すること。
    =======================================================================**/
    protected virtual void OnPlayerDetected() {
        hasDetectedPlayer = true;
        alertTimer = AlertDuration;
        isAlerting = true;
        animatorBridge?.SetAlert(true);
        SpawnOneShotVFX(discoverVFXPrefab, discoverVFXTargetPoint, discoverVFXLifetime);
        if (settings.PlayerDetectedSE != null)
            AudioSource.PlayClipAtPoint(
                settings.PlayerDetectedSE,
                transform.position,
                settings.PlayerDetectedSEVolume);
        Debug.Log($"[{gameObject.name}] プレイヤーを発見！");
    }

    protected GameObject SpawnOneShotVFX(GameObject prefab, Transform targetPoint, float lifetime)
    {
        if (prefab == null) return null;
        Transform point = targetPoint != null ? targetPoint : transform;
        GameObject instance = Instantiate(prefab, point.position, point.rotation);
        if (lifetime > 0f) Destroy(instance, lifetime);
        return instance;
    }

    protected void PlayOneShotSE(AudioClip clip, float volume = 1f, Vector3? position = null)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource.PlayClipAtPoint(clip, position ?? transform.position, Mathf.Clamp01(volume));
    }

    protected void PlayLimitedOneShotSE(
        AudioClip clip,
        float volume,
        float playbackDuration,
        Vector3? position = null)
    {
        if (clip == null || volume <= 0f || playbackDuration <= 0f) return;

        GameObject sourceObject = new GameObject("OneShotSE");
        sourceObject.transform.position = position ?? transform.position;

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.spatialBlend = 1f;
        source.Play();

        Destroy(sourceObject, Mathf.Min(playbackDuration, clip.length));
    }

    protected void UpdateMovementSE(
        ref float timer,
        bool isMoving,
        AudioClip clip,
        float volume,
        float interval,
        float playbackDuration = 0f)
    {
        if (!isMoving || clip == null)
        {
            timer = 0f;
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        if (playbackDuration > 0f)
            PlayLimitedOneShotSE(clip, volume, playbackDuration);
        else
            PlayOneShotSE(clip, volume);
        timer = Mathf.Max(0.05f, interval);
    }

    /**=======================================================================
      ギズモ描画（デバッグ用）
      DebugEnabled=true  → OnDrawGizmos で常時表示（Game/Scene 両ビュー）
      DebugEnabled=false → OnDrawGizmosSelected で選択時のみ表示（従来通り）
    =======================================================================**/
    protected virtual void OnDrawGizmos() {
        if (settings != null && settings.DebugEnabled && settings.ShowRangeRings)
            DrawAllRangeGizmos();
    }

    protected virtual void OnDrawGizmosSelected() {
        if (settings == null || !settings.DebugEnabled)
            DrawAllRangeGizmos();
    }

    private void DrawAllRangeGizmos() {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, DetectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
        DrawDebugRanges();
    }

    /// <summary>サブクラスで override して追加のデバッグ範囲を描画する（突進範囲など）。</summary>
    protected virtual void DrawDebugRanges() { }
}
