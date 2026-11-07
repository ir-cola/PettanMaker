using System.Collections;
using UnityEngine;

public class Enemy03 : EnemyBase
{
    private Enemy03Settings S => (Enemy03Settings)settings;

    [SerializeField] private EnemyAttackHitbox attackHitbox;

    [Header("VFX - 突き")]
    [Tooltip("VFX_Enemy03_AttackHit をアタッチする。")]
    [SerializeField] private GameObject attackHitVFXPrefab;
    [SerializeField] private Transform attackHitVFXTargetPoint;
    [Tooltip("VFX_Enemy03_AttackMiss をアタッチする。")]
    [SerializeField] private GameObject attackMissVFXPrefab;
    [SerializeField] private Transform attackMissVFXTargetPoint;
    [SerializeField, Min(0f)] private float attackVFXDelay;
    [SerializeField, Min(0f)] private float attackVFXLifetime = 3f;
    private Enemy03_ThrustAction currentThrustAction;
    private float walkSETimer;

    protected override void Start()
    {
        base.Start();
        navMeshAgent.speed = S.WalkSpeed;
    }

    protected override void Update()
    {
        base.Update();
        UpdateMovementSE(
            ref walkSETimer,
            currentState != State.Dead
                && navMeshAgent != null
                && navMeshAgent.enabled
                && navMeshAgent.velocity.sqrMagnitude > 0.01f,
            S.WalkSE,
            S.WalkSEVolume,
            S.WalkSEInterval,
            S.WalkSEPlaybackDuration);
    }

    protected override void UpdateState(float distance)
    {
        State previous = currentState;

        if (distance <= AttackRange)
            currentState = State.Attack;
        else if (distance <= DetectionRange)
            currentState = State.Chase;
        else
            currentState = State.Idle;

        if (previous == State.Idle && currentState != State.Idle)
            OnPlayerDetected();
    }

    protected override void ExecuteState(float distance)
    {
        if (HandleAlertTimer()) return;

        switch (currentState)
        {
            case State.Idle:
                if (ExecutePreAlertWander(S.WalkSpeed))
                    break;

                navMeshAgent.ResetPath();
                navMeshAgent.speed = S.WalkSpeed;
                break;

            case State.Chase:
                navMeshAgent.speed = S.RunSpeed;
                if (playerTransform != null)
                    navMeshAgent.SetDestination(playerTransform.position);
                FaceMoveDirection();
                break;

            case State.Attack:
                navMeshAgent.ResetPath();
                FacePlayer();
                attackTimer += Time.deltaTime;
                if (attackTimer >= AttackInterval)
                {
                    attackTimer = 0f;
                    PerformAttack();
                }
                break;
        }
    }

    protected override void PerformAttack()
    {
        currentThrustAction = new Enemy03_ThrustAction(
            this, attackHitbox,
            S.ThrustDelay, S.HitboxEnableDelay, S.HitboxActiveDuration,
            S.EndHitboxOnImpactEvent,
            S.TriggerAttackPrep, S.TriggerAttackThrust);
        actionController.SetAction(currentThrustAction);
    }

    public void OnAttackImpact()
    {
        currentThrustAction?.NotifyImpact();
    }

    internal void PlayAttackSE()
    {
        PlayOneShotSE(S.AttackSE, S.AttackSEVolume);
    }

    internal void OnThrustResolved(bool hit)
    {
        if (attackVFXDelay <= 0f)
        {
            SpawnAttackVFX(hit);
            return;
        }

        StartCoroutine(SpawnAttackVFXAfterDelay(hit));
    }

    private IEnumerator SpawnAttackVFXAfterDelay(bool hit)
    {
        yield return new WaitForSeconds(attackVFXDelay);
        SpawnAttackVFX(hit);
    }

    private void SpawnAttackVFX(bool hit)
    {
        SpawnOneShotVFX(
            hit ? attackHitVFXPrefab : attackMissVFXPrefab,
            hit ? attackHitVFXTargetPoint : attackMissVFXTargetPoint,
            attackVFXLifetime);
    }
}
