using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent を使ったボスの移動制御。
/// MoveMode に応じて速度・目的地を切り替える。
/// </summary>
public class B1_Locomotion : MonoBehaviour
{
    public enum MoveMode { Chase, Walk, Run, Charge, Strafe, Jump, BackStep, Hover, Search, Stop }

    private NavMeshAgent      agent;
    private IBlackboard       bb;
    private Transform         playerTransform;
    private B1_Settings       settings;
    private B1_AnimatorBridge animBridge;

    private MoveMode currentMode = MoveMode.Stop;
    private int      strafeDir   = 1;  // 旋回方向（+1 右, -1 左）
    private Vector3  chargeTarget;
    private float    chargeSpeed;
    private float    _smoothedAnimSpeed = 0f;
    private float    _chargeElapsed     = 0f;
    private float    _turnAnimCooldown  = 0f;  // ターンアニメの多重発火を防ぐ残りクールダウン

    // Player死亡後の探索歩行
    private Vector3 searchOrigin;
    private float   searchWaitTimer;
    private const float SearchRadius = 6f;
    private const float SearchArriveDistance = 0.5f;

    // BackStep（攻撃後の後退）
    private Vector3 backStepTarget;
    private float   backStepSpeed;
    /// <summary>BackStep が目標地点に到達したとき true になる。</summary>
    public bool     IsBackStepComplete { get; private set; }

    // Jump
    private Vector3 jumpStart;
    private Vector3 jumpTarget;
    private float   jumpDuration;
    private float   jumpHeight;
    private float   jumpElapsed;

    // Hover（ダウン中の浮遊）: その場で Y 軸だけ上昇 → 保持 → 降下する
    private enum HoverPhase { Rise, Hold, Descend }
    private HoverPhase hoverPhase;
    private float      hoverBaseY;       // 浮遊開始時の地面高さ（着地で戻る高さ）
    private float      hoverHeight;
    private float      hoverRiseDuration;
    private float      hoverDescendDuration;
    private float      hoverElapsed;
    private float      hoverOffsetAtDescend; // 降下開始時点の浮遊オフセット（途中解除でも滑らかに降ろす）
    /// <summary>Hover の降下が完了して着地したとき true になる。</summary>
    public bool        IsHoverLanded { get; private set; }

    /// <summary>Charge が目的地に到達したとき true になる。</summary>
    public bool     IsHopComplete  { get; private set; }
    /// <summary>Charge 中に壁へ衝突して停止したとき true になる。</summary>
    public bool     IsWallHit      { get; private set; }
    /// <summary>Jump が着地したとき true になる。</summary>
    public bool     IsJumpComplete { get; private set; }
    /// <summary>true の間、プレイヤーへの自動回転 (FacePlayer / FaceWhenTurning) を止める。死亡演出中などに使う。</summary>
    public bool     FacingLocked   { get; set; }
    public MoveMode CurrentMode    => currentMode;

    public void Initialize(IBlackboard bb, Transform player, B1_Settings settings, B1_AnimatorBridge animBridge)
    {
        this.bb              = bb;
        this.playerTransform = player;
        this.settings        = settings;
        this.animBridge      = animBridge;

        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        agent.updateRotation = false; // 回転は FacePlayer で制御
        agent.angularSpeed   = 0f;   // 回転は FacePlayer で制御
        agent.speed          = settings.ChaseSpeed;
        agent.acceleration   = settings.NavAcceleration;
    }

    public void SetMode(MoveMode mode)
    {
        if (currentMode == mode)
        {
            if (mode == MoveMode.Stop)
                StopAgentAtCurrentPosition();
            return;
        }
        if (currentMode == MoveMode.Charge)
            agent.acceleration = settings.NavAcceleration; // Rush 加速度を戻す
        if ((currentMode == MoveMode.Jump || currentMode == MoveMode.Hover) && !agent.enabled)
        {
            agent.enabled = true;
            // NavMesh 上に再スナップ。着地点がわずかにメッシュ外だと Warp が失敗して
            // エージェントが NavMesh 外に取り残され、地面へ埋まる原因になるため、
            // 失敗時は近傍の NavMesh 点を探して確実にスナップする。
            if (!agent.Warp(transform.position)
                && NavMesh.SamplePosition(transform.position, out var snapHit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(snapHit.position);
                transform.position = snapHit.position;
            }
        }
        currentMode    = mode;
        IsHopComplete  = false;
        IsWallHit      = false;
        IsJumpComplete = false;
        IsHoverLanded  = false;
        _chargeElapsed = 0f;

        if (mode == MoveMode.Stop)
        {
            StopAgentAtCurrentPosition();
        }
        else if (mode == MoveMode.Strafe)
        {
            agent.isStopped = false;
            strafeDir = UnityEngine.Random.value > 0.5f ? 1 : -1;
        }
        else if (mode == MoveMode.Search)
        {
            agent.isStopped = false;
            agent.ResetPath();
            searchOrigin = transform.position;
            searchWaitTimer = 0f;
        }
        else
        {
            agent.isStopped = false;
        }
    }

    private void Update()
    {
        if (settings == null) return;
        if (playerTransform == null && currentMode != MoveMode.Search) return;

        switch (currentMode)
        {
            case MoveMode.Chase:   UpdateChase();    break;
            case MoveMode.Walk:    UpdateWalk();     break;
            case MoveMode.Run:     UpdateRun();      break;
            case MoveMode.Charge:  UpdateCharge();   break;
            case MoveMode.Strafe:  UpdateStrafe();   break;
            case MoveMode.Jump:     UpdateJump();     break;
            case MoveMode.BackStep: UpdateBackStep(); break;
            case MoveMode.Hover:    UpdateHover();    break;
            case MoveMode.Search:   UpdateSearch();   break;
            case MoveMode.Stop:     SetAnimSpeed(0f); break;
        }

        // Charge（Rush）・Jump 中は旋回しない。FacingLocked 中 (死亡演出など) も回さない。
        // 移動中は常時スムーズに向き直る。待機中は大きく背後を取られた時だけ振り向く。
        _turnAnimCooldown -= Time.deltaTime;
        if (FacingLocked)
            return;
        if (currentMode == MoveMode.Search)
            FaceMoveDirection();
        else if (currentMode == MoveMode.Stop)
            FaceWhenTurning();
        else if (currentMode != MoveMode.Charge && currentMode != MoveMode.Jump
              && currentMode != MoveMode.Hover)
            FacePlayer();
    }

    private void LateUpdate()
    {
        KeepInsideNavMeshEdge();
    }

    /// <summary>ボスの中心が NavMesh の端へ寄りすぎた場合、設定した余白まで内側へ戻す。</summary>
    private void KeepInsideNavMeshEdge()
    {
        if (settings == null || agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        float margin = Mathf.Max(0f, settings.NavMeshEdgeMargin);
        if (margin <= 0f
            || !NavMesh.FindClosestEdge(transform.position, out NavMeshHit edgeHit, agent.areaMask))
            return;

        Vector3 inward = transform.position - edgeHit.position;
        inward.y = 0f;
        float edgeDistance = inward.magnitude;
        if (edgeDistance >= margin)
            return;

        if (edgeDistance > 0.001f)
        {
            inward /= edgeDistance;
        }
        else
        {
            inward = edgeHit.normal;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f)
                return;
            inward.Normalize();
        }

        Vector3 safePosition = edgeHit.position + inward * margin;
        if (NavMesh.SamplePosition(safePosition, out NavMeshHit safeHit,
                margin + 0.1f, agent.areaMask))
        {
            agent.Warp(safeHit.position);
        }
    }

    // ---- 各 MoveMode の更新 ----

    private void UpdateChase()
    {
        agent.speed = settings.ChaseSpeed * SpeedMult();
        agent.SetDestination(ArcAdjustedDestination());
        SetAnimSpeed(agent.velocity.magnitude);
    }

    private void UpdateWalk()
    {
        agent.speed = settings.Walk.Speed * SpeedMult();
        agent.SetDestination(ArcAdjustedDestination());
        SetAnimSpeed(agent.velocity.magnitude);
    }

    private void UpdateRun()
    {
        agent.speed = settings.Run.Speed * SpeedMult();
        agent.SetDestination(ArcAdjustedDestination());
        SetAnimSpeed(agent.velocity.magnitude);
    }

    private void UpdateSearch()
    {
        if (!agent.enabled || !agent.isOnNavMesh)
        {
            SetAnimSpeed(0f);
            return;
        }

        agent.speed = settings.Walk.Speed;
        bool arrived = !agent.pathPending
                    && (!agent.hasPath || agent.remainingDistance <= SearchArriveDistance);
        if (arrived)
        {
            agent.ResetPath();
            searchWaitTimer -= Time.deltaTime;
            if (searchWaitTimer <= 0f)
            {
                searchWaitTimer = UnityEngine.Random.Range(1.0f, 2.2f);
                Vector2 offset = UnityEngine.Random.insideUnitCircle * SearchRadius;
                Vector3 candidate = searchOrigin + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SearchRadius, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
            }
        }

        SetAnimSpeed(agent.velocity.magnitude);
    }

    private void UpdateCharge()
    {
        agent.speed = chargeSpeed * SpeedMult(); // 激昂倍率を毎フレーム反映
        _chargeElapsed += Time.deltaTime;
        SetAnimSpeed(agent.velocity.magnitude);

        if (!IsHopComplete && !agent.pathPending && agent.remainingDistance < 0.3f)
            IsHopComplete = true;

        // 加速しきった後に速度がほぼゼロ → 壁衝突と判定
        const float kGrace    = 0.2f;
        const float kVelMin   = 1.0f;
        const float kDistMin  = 1.5f;
        if (!IsWallHit && _chargeElapsed > kGrace
            && agent.velocity.magnitude < kVelMin
            && agent.remainingDistance  > kDistMin)
        {
            IsWallHit = true;
        }
    }

    // TailAttackAction から呼ぶ。NavMeshAgent を無効にして放物線ジャンプを開始する。
    public void BeginJump(Vector3 target, float duration, float height)
    {
        jumpStart      = transform.position;
        jumpTarget     = target;
        jumpDuration   = Mathf.Max(0.1f, duration);
        jumpHeight     = height;
        jumpElapsed    = 0f;
        SetMode(MoveMode.Jump); // 前のモード離脱処理を先に行う
        agent.enabled  = false; // NavMeshAgent をここで無効化
    }

    private void UpdateJump()
    {
        jumpElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(jumpElapsed / jumpDuration);

        // 水平はリニア補間、垂直はsinカーブ。終端では必ずtarget.yへ戻る。
        Vector3 flatPos = Vector3.Lerp(jumpStart, jumpTarget, t);
        float y = flatPos.y + Mathf.Sin(t * Mathf.PI) * jumpHeight;
        transform.position = new Vector3(flatPos.x, y, flatPos.z);

        SetAnimSpeed(0f); // ジャンプ中は移動アニメを止める

        if (t >= 1f && !IsJumpComplete)
            IsJumpComplete = true;
    }

    // ---- Hover（B1_DownState から呼ぶ。その場で Y 軸だけ上昇して浮遊する） ----

    /// <summary>その場で浮遊を開始する。NavMeshAgent を無効にして Y 軸だけ上昇 → 保持する。</summary>
    public void BeginHover(float height, float riseDuration)
    {
        hoverBaseY           = transform.position.y;
        hoverHeight          = Mathf.Max(0f, height);
        hoverRiseDuration    = Mathf.Max(0.05f, riseDuration);
        hoverElapsed         = 0f;
        hoverPhase           = HoverPhase.Rise;
        SetMode(MoveMode.Hover); // 前のモード離脱処理を先に行う
        agent.enabled        = false; // Jump と同じく NavMeshAgent を切って Transform を直接動かす
    }

    /// <summary>浮遊を解除して降下を開始する。着地は <see cref="IsHoverLanded"/> で待つ。</summary>
    public void EndHover(float descendDuration)
    {
        if (currentMode != MoveMode.Hover || hoverPhase == HoverPhase.Descend) return;
        hoverDescendDuration = Mathf.Max(0.05f, descendDuration);
        hoverOffsetAtDescend = transform.position.y - hoverBaseY; // 上昇途中の解除でも現在高さから降ろす
        hoverElapsed         = 0f;
        hoverPhase           = HoverPhase.Descend;
    }

    /// <summary>浮遊を即座に打ち切って着地させる（ダウン中に死亡した場合など）。</summary>
    public void CancelHover()
    {
        if (currentMode != MoveMode.Hover) return;
        transform.position = new Vector3(transform.position.x, hoverBaseY, transform.position.z);
        SetMode(MoveMode.Stop); // Hover 離脱で NavMeshAgent が再有効化・再スナップされる
    }

    private void UpdateHover()
    {
        SetAnimSpeed(0f); // 浮遊中は移動アニメを止める（Down クリップを優先させる）

        hoverElapsed += Time.deltaTime;
        float y;
        switch (hoverPhase)
        {
            case HoverPhase.Rise:
            {
                float t = Mathf.Clamp01(hoverElapsed / hoverRiseDuration);
                y = hoverBaseY + Mathf.SmoothStep(0f, hoverHeight, t);
                if (t >= 1f) hoverPhase = HoverPhase.Hold;
                break;
            }
            case HoverPhase.Hold:
                y = hoverBaseY + hoverHeight;
                break;
            default: // Descend
            {
                float t = Mathf.Clamp01(hoverElapsed / hoverDescendDuration);
                y = hoverBaseY + Mathf.SmoothStep(hoverOffsetAtDescend, 0f, t);
                if (t >= 1f && !IsHoverLanded) IsHoverLanded = true;
                break;
            }
        }
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    // RushAction から呼ぶ。目標位置を固定してから Charge モードへ切り替える。
    public void BeginCharge(Vector3 target, float speed)
    {
        chargeTarget   = target;
        chargeSpeed    = speed;
        IsHopComplete  = false;
        IsWallHit      = false;
        _chargeElapsed = 0f;
        agent.acceleration = 999f; // 瞬時に最高速へ
        agent.speed        = speed * SpeedMult();
        agent.SetDestination(target);
        SetMode(MoveMode.Charge);
    }

    // B1_BackStepAction から呼ぶ。プレイヤーと反対方向へ distance ぶん後退する。
    public void BeginBackStep(float distance, float speed)
    {
        Vector3 away = transform.position - playerTransform.position;
        away.y = 0f;
        away = away.sqrMagnitude < 0.0001f ? -transform.forward : away.normalized;

        Vector3 desired = transform.position + away * distance;
        // NavMesh 上の有効点へ丸めて壁の外へ抜けないようにする。
        backStepTarget = NavMesh.SamplePosition(desired, out var hit, distance, NavMesh.AllAreas)
            ? hit.position
            : desired;

        backStepSpeed      = speed;
        IsBackStepComplete = false;
        SetMode(MoveMode.BackStep);
        agent.SetDestination(backStepTarget);
    }

    private void UpdateBackStep()
    {
        agent.speed = backStepSpeed * SpeedMult();
        agent.SetDestination(backStepTarget);
        // 後退中は移動ブレンド（Walk/Run）を出さず、BackStep クリップを優先させる。
        SetAnimSpeed(0f);

        if (!IsBackStepComplete && !agent.pathPending && agent.remainingDistance < 0.3f)
            IsBackStepComplete = true;
    }

    private void UpdateStrafe()
    {
        Vector3 toPlayer = (playerTransform.position - transform.position);
        toPlayer.y = 0f;
        float angle = Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);

        Vector3 target;
        if (Mathf.Abs(angle) > settings.TurnArcThreshold)
        {
            // 大きく向きがずれているときは弧を描いて旋回
            target = ArcAdjustedDestination();
        }
        else
        {
            // 正面寄りのとき通常の横移動
            Vector3 right = Vector3.Cross(Vector3.up, toPlayer.normalized);
            target = transform.position + right * strafeDir * settings.ChaseSpeed * Time.deltaTime * 2f;
        }

        agent.speed = settings.ChaseSpeed;
        agent.SetDestination(target);
        SetAnimSpeed(agent.velocity.magnitude);
    }

    // ---- ユーティリティ ----

    private void SetAnimSpeed(float rawSpeed)
    {
        _smoothedAnimSpeed = Mathf.Lerp(_smoothedAnimSpeed, rawSpeed,
            settings.AnimSpeedSmoothing * Time.deltaTime);
        animBridge.SetMoveSpeed(_smoothedAnimSpeed);
    }

    // 旋回角度に応じて NavMesh 目標地点を横にオフセットし、弧を描いてターンさせる。
    // 角度が TurnArcThreshold 以下 → プレイヤー直進。超えると徐々に大回りになる。
    private Vector3 ArcAdjustedDestination()
    {
        Vector3 toPlayer = playerTransform.position - transform.position;
        toPlayer.y = 0f;
        float angle = Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);
        float absAngle = Mathf.Abs(angle);

        if (absAngle <= settings.TurnArcThreshold)
            return playerTransform.position;

        float arcFactor = Mathf.InverseLerp(settings.TurnArcThreshold, 180f, absAngle);
        Vector3 lateral = transform.right * Mathf.Sign(angle) * arcFactor * settings.TurnArcStrength;
        return playerTransform.position + lateral;
    }

    public void StepForward(float distance)
    {
        if (distance <= 0f) return;

        Vector3 target = transform.position + transform.forward * distance;
        if (agent.enabled && agent.isOnNavMesh)
        {
            // Move だけでは内部 nextPosition が旧位置に残り、次フレームに戻る場合がある。
            // Warp で Transform と NavMeshAgent の位置を同時に確定する。
            agent.Warp(target);
        }
        else
        {
            transform.position = target;
        }
    }

    /// <summary>攻撃前の狙い構え中、移動せずプレイヤーへ素早く正対する。</summary>
    public void AimAtPlayer(float turnSpeedMultiplier)
    {
        if (playerTransform == null || settings == null) return;

        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target,
            settings.TurnSpeed * Mathf.Max(0f, turnSpeedMultiplier) * Time.deltaTime);
    }

    private void StopAgentAtCurrentPosition()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;

        agent.ResetPath();
        agent.isStopped = true;
        agent.nextPosition = transform.position;
    }

    private float SpeedMult() => bb != null && bb.IsEnraged ? settings.EnragedSpeedMult : 1f;

    private void FacePlayer()
    {
        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        TryPlayTurnAnim(Vector3.SignedAngle(transform.forward, dir, Vector3.up));

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, settings.TurnSpeed * Time.deltaTime);
    }

    private void FaceMoveDirection()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        Vector3 dir = agent.desiredVelocity;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion target = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, settings.TurnSpeed * Time.deltaTime);
    }

    // 待機中は普段は向きを固定し、プレイヤーが大きく背後へ回った時だけ振り向く。
    private void FaceWhenTurning()
    {
        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        float angle = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
        if (Mathf.Abs(angle) < settings.TurnAnimAngle) return; // しきい値未満は固定

        TryPlayTurnAnim(angle);

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, settings.TurnSpeed * Time.deltaTime);
    }

    // 向きのずれが大きければ 90 度ターンアニメを再生する（回転自体は RotateTowards が担う）。
    private void TryPlayTurnAnim(float signedAngle)
    {
        if (_turnAnimCooldown > 0f) return;
        if (Mathf.Abs(signedAngle) < settings.TurnAnimAngle) return;

        animBridge.PlayTurn(left: signedAngle < 0f);
        _turnAnimCooldown = settings.TurnAnimCooldown;
    }
}
