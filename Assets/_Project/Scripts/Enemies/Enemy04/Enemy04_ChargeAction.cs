using UnityEngine;

public class Enemy04_ChargeAction : EnemyAction, IDebugDrawable
{
    private enum Phase { Windup, Dashing, Recovering }

    private readonly Enemy04           enemy;
    private readonly EnemyAttackHitbox hitbox;
    private readonly float windupDuration;
    private readonly float dashSpeed;
    private readonly float dashDistance;
    private readonly float hitRadius;
    private readonly float recoverDuration;
    private readonly float attackCooldown;
    private readonly string triggerAttack;
    private readonly float windupPullback;
    private readonly float windupSquash;
    private readonly float dashEase;

    private Phase   phase;
    private float   timer;
    private float   traveled;
    private bool    canMoveOnNav;
    private Vector3 dashDir;

    // アンチシペーション（タメ）・イーズアウト用の状態
    private Vector3 originalScale;
    private Vector3 windupBackDir;
    private float   appliedPullback;
    private float   dashElapsed;
    private float   dashDuration;

    public Enemy04_ChargeAction(
        Enemy04 enemy, EnemyAttackHitbox hitbox,
        float windupDuration, float dashSpeed, float dashDistance, float hitRadius,
        float recoverDuration, float attackCooldown,
        string triggerAttack,
        float windupPullback = 0f, float windupSquash = 0f, float dashEase = 1f)
        : base(enemy)
    {
        this.enemy           = enemy;
        this.hitbox          = hitbox;
        this.windupDuration  = windupDuration;
        this.dashSpeed       = dashSpeed;
        this.dashDistance    = dashDistance;
        this.hitRadius       = hitRadius;
        this.recoverDuration = recoverDuration;
        this.attackCooldown  = attackCooldown;
        this.triggerAttack   = triggerAttack;
        this.windupPullback  = Mathf.Max(0f, windupPullback);
        this.windupSquash    = Mathf.Clamp(windupSquash, 0f, 0.5f);
        this.dashEase        = Mathf.Max(1f, dashEase);
    }

    public override void Enter()
    {
        phase = Phase.Windup;
        timer = windupDuration;
        owner.Agent.ResetPath();
        enemy.CurrentMotion = Enemy04.MotionState.Attack_01;
        owner.AnimatorBridge?.SetTrigger(triggerAttack);
        enemy.OnAttackStarted();

        // タメの基準：現在スケールと「引く方向（プレイヤーと反対）」を確保。
        originalScale   = owner.transform.localScale;
        appliedPullback = 0f;
        // NavMeshAgent は updatePosition で transform を上書きするため、
        // タメの引き戻しも Agent.Move 経由で動かす（transform 直書きだと打ち消される）。
        canMoveOnNav    = owner.Agent.isOnNavMesh && owner.Agent.enabled;
        windupBackDir   = -owner.transform.forward;
        if (owner.PlayerTransform != null)
        {
            Vector3 toPlayer = owner.PlayerTransform.position - owner.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
                windupBackDir = -toPlayer.normalized;
        }
    }

    public override void Update()
    {
        switch (phase)
        {
            case Phase.Windup:
                ExecuteWindup();
                break;

            case Phase.Dashing:
                ExecuteDash();
                break;

            case Phase.Recovering:
                timer -= Time.deltaTime;
                if (timer <= 0f) IsFinished = true;
                break;
        }
    }

    public override void Exit()
    {
        base.Exit();
        hitbox?.EndAttack();
        enemy.StopDashVFX();
        // タメ/ダッシュ中に中断されてもスケールを取り残さない。
        owner.transform.localScale = originalScale;
        owner.SetCooldown(attackCooldown);
        if (IsFinished)
            enemy.OnRushFinished();
    }

    // タメ：プレイヤーを見据えつつ、後ろへ引き（アンチシペーション）ながら横に潰れて力を溜める。
    private void ExecuteWindup()
    {
        owner.FacePlayer();
        timer -= Time.deltaTime;

        float progress = windupDuration > 0.0001f
            ? Mathf.Clamp01(1f - timer / windupDuration)
            : 1f;
        float eased = 1f - (1f - progress) * (1f - progress); // ease-out（素早く溜めて保持）

        // 後ろへ引く（前フレームとの差分だけ動かして累積を防ぐ）
        if (windupPullback > 0f)
        {
            float desired = windupPullback * eased;
            float delta = desired - appliedPullback;
            MoveEnemy(windupBackDir * delta);
            appliedPullback = desired;
        }

        // 横に膨らみ縦に縮む「タメ潰し」
        if (windupSquash > 0f)
        {
            Vector3 squashed = new Vector3(
                originalScale.x * (1f + windupSquash),
                originalScale.y * (1f - windupSquash),
                originalScale.z * (1f + windupSquash));
            owner.transform.localScale = Vector3.Lerp(originalScale, squashed, eased);
        }

        if (timer <= 0f) StartDash();
    }

    private void StartDash()
    {
        phase        = Phase.Dashing;
        traveled     = 0f;
        dashElapsed  = 0f;
        // イーズアウトでも平均速度が dashSpeed になるよう総時間を距離÷速度に合わせる。
        dashDuration = dashSpeed > 0.0001f ? dashDistance / dashSpeed : 0.0001f;
        canMoveOnNav = owner.Agent.isOnNavMesh && owner.Agent.enabled;

        dashDir = owner.transform.forward;
        if (owner.PlayerTransform != null)
        {
            Vector3 toPlayer = owner.PlayerTransform.position - owner.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                dashDir = toPlayer.normalized;
                owner.FacePlayer();
            }
        }

        // 発射の瞬間は逆に縦へ伸びる（スクワッシュ＆ストレッチ）。
        if (windupSquash > 0f)
        {
            owner.transform.localScale = new Vector3(
                originalScale.x * (1f - windupSquash * 0.6f),
                originalScale.y * (1f + windupSquash * 0.8f),
                originalScale.z * (1f - windupSquash * 0.6f));
        }

        hitbox?.BeginAttack(owner.AttackDamage, owner.AttackDamageType);
        enemy.StartDashVFX();
        enemy.CurrentMotion = Enemy04.MotionState.Attack_02;
    }

    // ダッシュ：出だしが鋭く後半で減速するイーズアウトで距離を進める。
    private void ExecuteDash()
    {
        dashElapsed += Time.deltaTime;
        float u = Mathf.Clamp01(dashElapsed / dashDuration);
        float easedU = 1f - Mathf.Pow(1f - u, dashEase); // ease-out

        float newTraveled = dashDistance * easedU;
        float step = newTraveled - traveled;
        MoveEnemy(dashDir * step);
        traveled = newTraveled;

        // ストレッチを元に戻していく（着地に向けてスケール正常化）。
        owner.transform.localScale = Vector3.Lerp(owner.transform.localScale, originalScale, u);

        if (u >= 1f)
        {
            owner.transform.localScale = originalScale;
            hitbox?.EndAttack();
            enemy.StopDashVFX();
            phase = Phase.Recovering;
            timer = recoverDuration;
            enemy.CurrentMotion = Enemy04.MotionState.Attack_03;
        }
    }

    private void MoveEnemy(Vector3 delta)
    {
        if (canMoveOnNav)
            owner.Agent.Move(delta);
        else
            owner.transform.position += delta;
    }

    // ---- IDebugDrawable ----
    public void DrawDebugGizmos()
    {
        Gizmos.color = phase == Phase.Dashing
            ? new Color(1f, 0f, 0f, 0.85f)
            : new Color(1f, 0.5f, 0f, 0.35f);
        Gizmos.DrawWireSphere(owner.transform.position, hitRadius);

        if (phase == Phase.Dashing)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(
                owner.transform.position,
                owner.transform.position + dashDir * (dashDistance - traveled));
        }
    }
}
