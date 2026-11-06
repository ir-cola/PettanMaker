using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Enemy02 : EnemyBase
{
    private Enemy02Settings S => (Enemy02Settings)settings;

    [Header("Face")]
    [SerializeField] private ToonFaceTextureController faceTextureController;
    [SerializeField] private int fleeFaceIndex = 1;
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

    [Header("VFX - 逃走")]
    [Tooltip("VFX_Enemy02_Escaping をアタッチする。")]
    [SerializeField] private GameObject escapingVFXPrefab;
    [Tooltip("逃走VFXの追従位置。未設定なら敵本体。")]
    [SerializeField] private Transform escapingVFXTargetPoint;

    private float fleeRepathTimer = 0f;
    private float baseSpeed;
    private float idleTimer = 0f;
    private int idleVariant = 0;
    private bool isFleeRunningFace = false;
    private float blinkTimer = 0f;
    private Coroutine blinkCoroutine;
    private Coroutine damageCoroutine;
    private GameObject escapingVFXInstance;
    private float walkSETimer;

    protected override void Start()
    {
        base.Start();
        baseSpeed = navMeshAgent.speed;
        navMeshAgent.stoppingDistance = 0f;
        if (Animator != null) UpdateAnimator(0f, false);
        idleTimer = S.IdleSwapInterval;
        ApplyIdleAnimatorParameter();
        blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
        enemyHealth.onHPChanged.AddListener(OnDamaged);
    }

    protected override void Update()
    {
        base.Update();
        AudioClip walkClip = S.WalkSE;
        if (S.WalkSEVariations != null && S.WalkSEVariations.Length > 0) {
            AudioClip picked = S.WalkSEVariations[Random.Range(0, S.WalkSEVariations.Length)];
            if (picked != null) walkClip = picked;
        }
        UpdateMovementSE(
            ref walkSETimer,
            currentState != State.Dead
                && navMeshAgent != null
                && navMeshAgent.enabled
                && navMeshAgent.velocity.sqrMagnitude > 0.01f,
            walkClip,
            S.WalkSEVolume,
            S.WalkSEInterval,
            S.WalkSEPlaybackDuration);
    }

    protected override void UpdateState(float distance)
    {
        State previous = currentState;
        currentState = distance <= DetectionRange ? State.Flee : State.Idle;
        if (previous == State.Idle && currentState == State.Flee)
        {
            OnPlayerDetected();
            CancelBlink();
        }
    }

    protected override void OnPlayerDetected()
    {
        base.OnPlayerDetected();
    }

    protected override void ExecuteState(float distance)
    {
        switch (currentState)
        {
            case State.Idle: ExecuteIdle(); break;
            case State.Flee: ExecuteFlee(); break;
        }
    }

    private void ExecuteIdle()
    {
        SetFleeRunningFace(false);
        UpdateBlink();

        if (ExecutePreAlertWander(baseSpeed))
            return;

        navMeshAgent.ResetPath();
        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            idleVariant = Random.Range(0, 2);
            idleTimer = Random.Range(S.IdleSwapInterval * 0.75f, S.IdleSwapInterval * 1.25f);
        }
        ApplyIdleAnimatorParameter();
    }

    private void ApplyIdleAnimatorParameter()
    {
        animatorBridge?.SetInteger(S.ParamIdleIndex, idleVariant);
    }

    private void ExecuteFlee()
    {
        if (alertTimer > 0f)
        {
            alertTimer -= Time.deltaTime;
            navMeshAgent.ResetPath();
            FacePlayer();
            isAlerting = true;
            SetFleeRunningFace(false);
            return;
        }
        isAlerting = false;
        Flee();
        FaceMoveDirection();
        SetFleeRunningFace(true);
    }

    private void SetFleeRunningFace(bool running)
    {
        if (isFleeRunningFace == running) return;
        isFleeRunningFace = running;
        SetEscapingVFX(running);

        // ダメージ表情の保持中は書き込まない。保持終了時に DamageFaceRoutine が
        // isFleeRunningFace を見て正しい表情へ戻す。
        if (damageCoroutine != null) return;

        faceTextureController?.TransitionFaceTexture(running ? fleeFaceIndex : defaultFaceIndex, faceTransitionDuration);
    }

    private void SetEscapingVFX(bool active)
    {
        if (!active)
        {
            if (escapingVFXInstance != null) Destroy(escapingVFXInstance);
            escapingVFXInstance = null;
            return;
        }

        if (escapingVFXPrefab == null || escapingVFXInstance != null) return;
        Transform point = escapingVFXTargetPoint != null ? escapingVFXTargetPoint : transform;
        escapingVFXInstance = Instantiate(escapingVFXPrefab, point);
        escapingVFXInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void OnDisable() => SetEscapingVFX(false);

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
        faceTextureController?.TransitionFaceTexture(isFleeRunningFace ? fleeFaceIndex : defaultFaceIndex, faceTransitionDuration);
        damageCoroutine = null;
    }

    private void Flee()
    {
        navMeshAgent.speed = baseSpeed * S.FleeSpeedMultiplier;
        fleeRepathTimer -= Time.deltaTime;

        bool reached = !navMeshAgent.pathPending && navMeshAgent.remainingDistance <= 0.3f;
        if (fleeRepathTimer <= 0f || reached || !navMeshAgent.hasPath)
        {
            PickFleeDestination();
            fleeRepathTimer = S.FleeRepathInterval;
        }
    }

    private void PickFleeDestination()
    {
        if (playerTransform == null) return;

        Vector3 away = transform.position - playerTransform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        away.Normalize();

        float angle = Random.Range(-S.FleeWanderAngle, S.FleeWanderAngle);
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
        Vector3 target = transform.position + dir * S.FleeStep;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, S.FleeStep, NavMesh.AllAreas))
        { navMeshAgent.SetDestination(hit.position); return; }

        Vector3 side = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * away;
        if (NavMesh.SamplePosition(transform.position + side * S.FleeStep, out NavMeshHit hit2, S.FleeStep, NavMesh.AllAreas))
        { navMeshAgent.SetDestination(hit2.position); return; }

        for (int i = 0; i < 6; i++)
        {
            Vector3 rndDir = Quaternion.Euler(0f, Random.Range(-180f, 180f), 0f) * Vector3.forward;
            if (NavMesh.SamplePosition(transform.position + rndDir * (S.FleeStep * 0.6f), out NavMeshHit rndHit, S.FleeStep * 0.6f, NavMesh.AllAreas))
            { navMeshAgent.SetDestination(rndHit.position); return; }
        }

        navMeshAgent.ResetPath();
    }

    protected override void OnDied()
    {
        CancelBlink();
        if (damageCoroutine != null) StopCoroutine(damageCoroutine);
        currentState = State.Dead;
        if (navMeshAgent.isOnNavMesh) navMeshAgent.ResetPath();
        navMeshAgent.enabled = false;
        if (Animator != null) Animator.enabled = false;
    }

    protected override void DrawDebugRanges()
    {
        if (settings is not Enemy02Settings s) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, s.FleeStep);
    }
}
