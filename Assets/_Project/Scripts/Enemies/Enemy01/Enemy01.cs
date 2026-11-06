using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Enemy01 : EnemyBase
{
    public enum MotionState { Idle_00, Idle_01, Alert, Walk, Run, Finding, Attack_0, Attack_1 }
    public MotionState CurrentMotion { get; internal set; } = MotionState.Idle_00;

    [Header("投擲位置（子オブジェクト）")]
    [Tooltip("投擲物を生成する地点（バッグ）")]
    [SerializeField] private Transform bagPoint;
    [Tooltip("投擲物を運んでから投げ始める地点（耳）")]
    [SerializeField] private Transform earPoint;
    [Tooltip("旧: 単一投擲地点。bag/ear 未設定時のフォールバックに使う")]
    [SerializeField] private Transform throwPoint;

    [Header("VFX - 投擲物発見")]
    [Tooltip("VFX_Enemy01_ProjectileDiscover をアタッチする。")]
    [SerializeField] private GameObject projectileDiscoverVFXPrefab;
    [Tooltip("ニンジンを見つけた演出位置。未設定なら bagPoint。")]
    [SerializeField] private Transform projectileDiscoverVFXTargetPoint;
    [SerializeField, Min(0f)] private float projectileDiscoverVFXLifetime = 3f;

    private Enemy01Settings S => (Enemy01Settings)settings;

    [Header("Face")]
    [SerializeField] private ToonFaceTextureController faceTextureController;
    [SerializeField] private int attackFaceIndex = 1;
    [SerializeField] private int damageFaceIndex = 2;
    [SerializeField] private int defaultFaceIndex = 0;
    [SerializeField] private float faceTransitionDuration = 0.2f;

    [Header("Face - Blink (Idle)")]
    [SerializeField] private int blinkFaceIndex = 1;
    [SerializeField] private float blinkIntervalMin = 2f;
    [SerializeField] private float blinkIntervalMax = 5f;
    [SerializeField] private float blinkDuration = 0.12f;

    [Header("Face - Damage")]
    [SerializeField] private float damageHoldDuration = 0.25f;

    private float idleTimer = 0f;
    private int idleVariant = 0;
    private float blinkTimer = 0f;
    private Coroutine blinkCoroutine;
    private Coroutine damageCoroutine;

    protected override void Start()
    {
        base.Start();
        navMeshAgent.stoppingDistance = 0f;
        if (Animator != null) UpdateAnimator(0f, false);
        idleTimer = S.IdleSwapInterval;
        ApplyIdleAnimatorParameter();
        blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
        enemyHealth.onHPChanged.AddListener(OnDamaged);
    }

    protected override void UpdateState(float distance)
    {
        State previous = currentState;

        if (distance <= S.ThrowRange)
            currentState = State.Attack;
        else if (distance <= DetectionRange)
            currentState = State.Chase;
        else
            currentState = State.Idle;

        if (previous == State.Idle && currentState != State.Idle)
        {
            OnPlayerDetected();
            CancelBlink();
        }
    }

    protected override void OnPlayerDetected()
    {
        CurrentMotion = MotionState.Alert;
        base.OnPlayerDetected();
    }

    protected override void ExecuteState(float distance)
    {
        if (HandleAlertTimer()) { CurrentMotion = MotionState.Alert; return; }
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.Idle:   ExecuteIdle(); break;
            case State.Chase:  ExecuteChase(); break;
            case State.Attack: ExecuteAttack(distance); break;
        }
    }

    private void ExecuteIdle()
    {
        UpdateBlink();

        if (ExecutePreAlertWander(S.WalkSpeed))
        {
            CurrentMotion = navMeshAgent.velocity.sqrMagnitude > 0.01f ? MotionState.Walk : MotionState.Idle_00;
            return;
        }

        navMeshAgent.ResetPath();
        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            idleVariant = Random.Range(0, 2);
            idleTimer = Random.Range(S.IdleSwapInterval * 0.75f, S.IdleSwapInterval * 1.25f);
        }
        CurrentMotion = idleVariant == 0 ? MotionState.Idle_00 : MotionState.Idle_01;
        ApplyIdleAnimatorParameter();
    }

    private void ApplyIdleAnimatorParameter()
    {
        animatorBridge?.SetInteger(S.ParamIdleIndex, idleVariant);
    }

    private void ExecuteChase()
    {
        if (playerTransform == null) return;
        navMeshAgent.speed = S.RunSpeed;
        CurrentMotion = MotionState.Run;
        navMeshAgent.SetDestination(playerTransform.position);
        FaceMoveDirection();
    }

    private void ExecuteAttack(float distance)
    {
        if (playerTransform == null) return;

        if (distance < S.KeepDistanceMin)
        {
            navMeshAgent.speed = S.WalkSpeed;
            MoveAwayFromPlayer();
            FacePlayer();
            CurrentMotion = MotionState.Walk;
            return;
        }

        navMeshAgent.ResetPath();
        FacePlayer();

        if (cooldownTimer <= 0f && !actionController.HasAction)
        {
            actionController.SetAction(new Enemy01_ThrowAction(
                this,
                S.FindingDuration, S.WindupDuration, S.ThrowRecovery, S.AttackCooldown,
                S.TriggerFinding, S.TriggerAttack0, S.BoolIsAttacking,
                S.AttackClipFPS, S.CarryEndFrame, S.ThrowFrame));
            OnAttackStarted();
        }
        else
        {
            CurrentMotion = MotionState.Idle_00;
        }
    }

    // Attack アニメ Frame0 で投擲物をバッグ地点に生成し「保持」状態で返す。
    // 以降の運搬(→CarryEndFrame)・投擲(→ThrowFrame)は Enemy01_ThrowAction が
    // アニメのフレーム進捗に合わせて UpdateCarry / Release で駆動する。
    internal Enemy01_Projectile SpawnThrowProjectile()
    {
        if (S.ProjectilePrefabs == null || S.ProjectilePrefabs.Length == 0)
        {
            Debug.LogWarning("[Enemy01] ProjectilePrefabs が未設定です。");
            return null;
        }
        if (playerTransform == null) return null;

        GameObject prefab = SelectProjectilePrefab();
        if (prefab == null) return null;

        // 生成=バッグ地点、投擲開始=耳地点。未設定なら throwPoint → 本体上をフォールバック。
        Transform bag = bagPoint != null ? bagPoint : (throwPoint != null ? throwPoint : transform);
        Transform ear = earPoint != null ? earPoint : (throwPoint != null ? throwPoint : transform);

        SpawnOneShotVFX(
            projectileDiscoverVFXPrefab,
            projectileDiscoverVFXTargetPoint != null ? projectileDiscoverVFXTargetPoint : bag,
            projectileDiscoverVFXLifetime);

        Vector3 spawnPos = bag != null ? bag.position : transform.position + Vector3.up * 1.0f;
        GameObject proj = Instantiate(prefab, spawnPos, Quaternion.identity);

        Enemy01_Projectile rp = proj.GetComponent<Enemy01_Projectile>();
        if (rp == null)
        {
            Debug.LogWarning("[Enemy01] Projectile prefab に Enemy01_Projectile がありません。", proj);
            Destroy(proj);
            return null;
        }

        // 駆動されない場合の保険：ThrowFrame 相当 + 1秒 で自動投擲。
        float fps = Mathf.Max(1f, S.AttackClipFPS);
        float safety = Mathf.Max(S.CarryEndFrame, S.ThrowFrame) / fps + 1.0f;

        rp.BeginHold(new Enemy01_Projectile.ThrowConfig
        {
            bagPoint          = bag,
            earPoint          = ear,
            target            = playerTransform,
            aimHeightOffset   = S.AimHeightOffset,
            launchAngle       = S.LaunchAngle,
            fallbackSpeed     = S.ProjectileSpeed,
            spinDegPerSec     = S.ProjectileSpin,
            damage            = AttackDamage,
            damageType        = AttackDamageType,
            ignoreColliders   = GetComponentsInChildren<Collider>(),
            safetyReleaseTime = safety,
            ownerTransform    = transform,
            carryArcHeight    = S.CarryArcHeight,
            carryArcOutward   = S.CarryArcOutward,
            throwSE           = S.ProjectileThrowSE,
            throwSEVolume     = S.ProjectileThrowSEVolume,
            hitSE             = S.ProjectileHitSE,
            hitSEVolume       = S.ProjectileHitSEVolume,
        });
        return rp;
    }

    private GameObject SelectProjectilePrefab()
    {
        int start = Random.Range(0, S.ProjectilePrefabs.Length);
        for (int i = 0; i < S.ProjectilePrefabs.Length; i++)
        {
            GameObject p = S.ProjectilePrefabs[(start + i) % S.ProjectilePrefabs.Length];
            if (p != null) return p;
        }
        return null;
    }

    private void MoveAwayFromPlayer()
    {
        Vector3 away = transform.position - playerTransform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        away.Normalize();

        Vector3 target = transform.position + away * 3.0f;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            navMeshAgent.SetDestination(hit.position);
        else
            navMeshAgent.SetDestination(target);
    }

    internal void OnAttackStarted()
    {
        if (damageCoroutine != null) return;
        faceTextureController?.TransitionFaceTexture(attackFaceIndex, faceTransitionDuration);
    }

    internal void OnAttackFinished()
    {
        if (damageCoroutine != null) return;
        faceTextureController?.TransitionFaceTexture(defaultFaceIndex, faceTransitionDuration);
    }

    private void OnDamaged(int currentHP)
    {
        CancelBlink();
        if (damageCoroutine != null) StopCoroutine(damageCoroutine);
        damageCoroutine = StartCoroutine(DamageFaceRoutine());
    }

    private IEnumerator DamageFaceRoutine()
    {
        faceTextureController?.TransitionFaceTexture(damageFaceIndex, faceTransitionDuration);
        yield return new WaitForSeconds(damageHoldDuration);
        int revertIndex = currentState == State.Attack ? attackFaceIndex : defaultFaceIndex;
        faceTextureController?.TransitionFaceTexture(revertIndex, faceTransitionDuration);
        damageCoroutine = null;
    }

    private void UpdateBlink()
    {
        blinkTimer -= Time.deltaTime;
        // ダメージ表情の保持中に瞬きが割り込むと、その表情を上書きしてしまう。
        if (blinkTimer > 0f || blinkCoroutine != null || damageCoroutine != null)
            return;

        blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
        blinkCoroutine = StartCoroutine(BlinkRoutine());
    }

    private IEnumerator BlinkRoutine()
    {
        faceTextureController?.SetFaceTexture(blinkFaceIndex);
        yield return new WaitForSeconds(blinkDuration);
        faceTextureController?.SetFaceTexture(defaultFaceIndex);
        blinkCoroutine = null;
    }

    private void CancelBlink()
    {
        if (blinkCoroutine == null) return;
        StopCoroutine(blinkCoroutine);
        blinkCoroutine = null;
        faceTextureController?.SetFaceTexture(defaultFaceIndex);
    }

    protected override void OnDied()
    {
        CancelBlink();
        if (damageCoroutine != null) StopCoroutine(damageCoroutine);
        base.OnDied();
    }

    public override string DebugMotionName => CurrentMotion.ToString();

    protected override void DrawDebugRanges()
    {
        if (settings is not Enemy01Settings s) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, s.ThrowRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, s.KeepDistanceMin);
    }
}
