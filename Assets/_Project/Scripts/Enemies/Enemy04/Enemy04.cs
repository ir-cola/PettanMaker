using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Enemy04 : EnemyBase
{
    public enum MotionState { Idle_00, Move_01, Move_02, Alert, Attack_01, Attack_02, Attack_03, Down_01, Down_Recover }
    public MotionState CurrentMotion { get; internal set; } = MotionState.Idle_00;

    private Enemy04Settings S => (Enemy04Settings)settings;

    [SerializeField] private EnemyAttackHitbox attackHitbox;

    [Header("VFX - 突進 / ダウン")]
    [Tooltip("VFX_EnemyCommon_Dash をアタッチする。")]
    [SerializeField] private GameObject dashVFXPrefab;
    [SerializeField] private Transform dashVFXTargetPoint;
    [Tooltip("VFX_Enemy04_Down をアタッチする。")]
    [SerializeField] private GameObject downVFXPrefab;
    [SerializeField] private Transform downVFXTargetPoint;
    [Tooltip("VFX_Enemy04_RecoverDown をアタッチする。")]
    [SerializeField] private GameObject recoverDownVFXPrefab;
    [SerializeField] private Transform recoverDownVFXTargetPoint;
    [SerializeField, Min(0f)] private float recoverDownVFXLifetime = 3f;

    [Header("Face")]
    [SerializeField] private ToonFaceTextureController faceTextureController;
    [SerializeField] private int attackFaceIndex = 1;
    [SerializeField] private int stanFaceIndex = 2;
    [SerializeField] private int defaultFaceIndex = 0;
    [SerializeField] private float faceTransitionDuration = 0.2f;

    [Header("Face - Blink (Idle)")]
    [SerializeField] private int blinkFaceIndex = 1;
    [SerializeField] private float blinkIntervalMin = 2f;
    [SerializeField] private float blinkIntervalMax = 5f;
    [SerializeField] private float blinkDuration = 0.12f;

    [Header("Face - Damage")]
    [SerializeField] private int damageFaceIndex = 1;
    [SerializeField] private float damageHoldDuration = 0.25f;

    // ResetPath / SetDestination は NavMesh 上のアクティブなエージェントでしか呼べない
    // (ボス部屋などベイク範囲外に配置されるとエラーになる)
    private bool AgentOnNavMesh => navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;

    private float idleTimer = 0f;
    private bool idleWandering = false;
    private float postChargeDisengageTimer = 0f;
    private float blinkTimer = 0f;
    private Coroutine blinkCoroutine;
    private Coroutine damageCoroutine;
    private bool pendingStun = false;
    private GameObject dashVFXInstance;
    private GameObject downVFXInstance;

    protected override void Start()
    {
        base.Start();
        navMeshAgent.stoppingDistance = 0f;
        idleTimer = S.IdleHoverDuration;
        if (Animator != null) UpdateAnimator(0f, false);
        blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
        enemyHealth.onHPChanged.AddListener(OnDamaged);
    }

    protected override void UpdateState(float distance)
    {
        State previous = currentState;

        if (postChargeDisengageTimer > 0f)
        {
            postChargeDisengageTimer -= Time.deltaTime;
            currentState = State.Idle;
            return;
        }

        if (distance <= S.ChargeTriggerRange)
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
        if (pendingStun)
        {
            pendingStun = false;
            actionController.SetAction(new Enemy04_StunAction(this, S.StunDuration, S.RecoverShakeDuration, S.ParamIsDown));
            return;
        }

        if (HandleAlertTimer()) { CurrentMotion = MotionState.Alert; return; }
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.Idle:   ExecuteIdle(); break;
            case State.Chase:  ExecuteChase(); break;
            case State.Attack: ExecuteAttack(); break;
        }
    }

    private void ExecuteIdle()
    {
        UpdateBlink();

        if (!HasDetectedPlayer && ExecutePreAlertWander(S.WanderSpeed))
        {
            CurrentMotion = navMeshAgent.velocity.sqrMagnitude > 0.01f
                ? MotionState.Move_01
                : MotionState.Idle_00;
            return;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            idleWandering = !idleWandering;
            if (idleWandering)
            {
                idleTimer = S.IdleWanderDuration;
                PickWanderDestination();
            }
            else
            {
                idleTimer = S.IdleHoverDuration;
                if (AgentOnNavMesh) navMeshAgent.ResetPath();
            }
        }

        if (idleWandering && navMeshAgent.hasPath)
        {
            navMeshAgent.speed = S.WanderSpeed;
            CurrentMotion = MotionState.Move_01;
            FaceMoveDirection();
        }
        else
        {
            if (AgentOnNavMesh) navMeshAgent.ResetPath();
            CurrentMotion = MotionState.Idle_00;
        }
    }

    private void ExecuteChase()
    {
        if (playerTransform == null) return;
        navMeshAgent.speed = S.ChaseSpeed;
        CurrentMotion = MotionState.Move_02;
        if (AgentOnNavMesh) navMeshAgent.SetDestination(playerTransform.position);
        FaceMoveDirection();
    }

    private void ExecuteAttack()
    {
        if (playerTransform == null) return;

        if (cooldownTimer <= 0f && !actionController.HasAction)
        {
            FacePlayer();
            actionController.SetAction(new Enemy04_ChargeAction(
                this, attackHitbox,
                S.WindupDuration, S.DashSpeed, S.DashDistance, S.HitRadius,
                S.RecoverDuration, S.AttackCooldown,
                S.TriggerAttack,
                S.WindupPullback, S.WindupSquash, S.DashEase));
        }
        else
        {
            navMeshAgent.speed = S.ChaseSpeed;
            CurrentMotion = MotionState.Move_02;
            if (AgentOnNavMesh) navMeshAgent.SetDestination(playerTransform.position);
        }
    }

    private void PickWanderDestination()
    {
        if (!AgentOnNavMesh) return;
        Vector2 r = Random.insideUnitCircle * S.WanderRadius;
        Vector3 target = transform.position + new Vector3(r.x, 0f, r.y);
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, S.WanderRadius, NavMesh.AllAreas))
            navMeshAgent.SetDestination(hit.position);
    }

    internal void OnAttackStarted()
    {
        faceTextureController?.TransitionFaceTexture(attackFaceIndex, faceTransitionDuration);
        PlayOneShotSE(S.AttackSE, S.AttackSEVolume);
    }

    internal void OnRushFinished()
    {
        pendingStun = true;
    }

    internal void OnAttackStunned()
    {
        currentState = State.Stan;
        faceTextureController?.TransitionFaceTexture(stanFaceIndex, faceTransitionDuration);
        SetLoopVFX(ref downVFXInstance, downVFXPrefab, downVFXTargetPoint, true);
    }

    internal void StartDashVFX()
        => SetLoopVFX(ref dashVFXInstance, dashVFXPrefab, dashVFXTargetPoint, true);

    internal void StopDashVFX()
        => SetLoopVFX(ref dashVFXInstance, dashVFXPrefab, dashVFXTargetPoint, false);

    internal void OnDownRecoverStarted()
    {
        SetLoopVFX(ref downVFXInstance, downVFXPrefab, downVFXTargetPoint, false);
        SpawnOneShotVFX(recoverDownVFXPrefab, recoverDownVFXTargetPoint, recoverDownVFXLifetime);
    }

    internal void OnChargeActionFinished()
    {
        StopDashVFX();
        SetLoopVFX(ref downVFXInstance, downVFXPrefab, downVFXTargetPoint, false);
        postChargeDisengageTimer = S.PostChargeDisengageDuration;
        isAlerting = false;
        currentState = State.Idle;
        idleWandering = true;
        idleTimer = S.IdleWanderDuration;
        CurrentMotion = MotionState.Move_01;
        faceTextureController?.TransitionFaceTexture(defaultFaceIndex, faceTransitionDuration);
        PickWanderDestination();
    }

    private void SetLoopVFX(ref GameObject instance, GameObject prefab, Transform targetPoint, bool active)
    {
        if (!active)
        {
            if (instance != null) Destroy(instance);
            instance = null;
            return;
        }

        if (prefab == null || instance != null) return;
        Transform point = targetPoint != null ? targetPoint : transform;
        instance = Instantiate(prefab, point);
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void OnDisable()
    {
        StopDashVFX();
        SetLoopVFX(ref downVFXInstance, downVFXPrefab, downVFXTargetPoint, false);
    }

    private void UpdateBlink()
    {
        blinkTimer -= Time.deltaTime;
        if (blinkTimer > 0f || blinkCoroutine != null)
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

        int revertIndex = currentState == State.Attack ? attackFaceIndex
            : currentState == State.Stan ? stanFaceIndex
            : defaultFaceIndex;
        faceTextureController?.TransitionFaceTexture(revertIndex, faceTransitionDuration);
        damageCoroutine = null;
    }

    protected override void OnDied()
    {
        CancelBlink();
        if (damageCoroutine != null) StopCoroutine(damageCoroutine);
        if (Animator != null) Animator.enabled = false;
        base.OnDied();
    }

    public override string DebugMotionName => CurrentMotion.ToString();

    protected override void DrawDebugRanges()
    {
        if (settings is not Enemy04Settings s) return;
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, s.ChargeTriggerRange);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, s.HitRadius);
    }
}
