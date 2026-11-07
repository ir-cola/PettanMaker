using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 後退ジャンプ。プレイヤーと反対方向へ放物線ジャンプして距離を取る。
/// 円形マップ対応: 真後ろが場外になる壁際では、マップ中心方向へ飛んで場内に戻るため、
/// 端に追い詰められても必ず本来の距離を飛べる。
/// マップ情報は B1_Boss.MapCenter + Config.MapRadius から取得 (未設定なら単純後退)。
/// アニメは BackStep_01(飛び上がり) → BackStep_02(空中Idle) → BackStep_03(着地) の順。
/// 01→02 は Animator の自動遷移に任せ、滞空が長引いた場合は 02 を保持、
/// スクリプトのジャンプ着地の瞬間に 03 を強制再生して見た目と移動を同期させる。
/// </summary>
public class B1_BackStepAction : B1_Action
{
    private const float MaxTurnDuration = 0.6f; // 振り向きに使う最大秒数 (超えたら向きが甘くても跳ぶ)
    private const float TurnDoneAngle   = 5f;   // この角度以内まで向いたら振り向き完了

    private BackStepActionConfig cfg;
    private float   elapsed;
    private bool    jumping;     // 振り向きが終わってジャンプ開始済みか
    private bool    landed;
    private float   landTimer;
    private Vector3 jumpTarget;
    private Vector3 faceDir;     // 跳ぶ前に向く方向 (進行方向の反対)

    public B1_BackStepAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        cfg     = boss.Settings.BackStep;
        elapsed = 0f;
        jumping = false;
        landed  = false;

        // プレイヤーと反対方向 (壁際なら中心方向) の着地点を決め、NavMesh 上の有効点へ丸める。
        Vector3 pos  = boss.transform.position;
        Vector3 away = boss.PlayerTransform != null
            ? pos - boss.PlayerTransform.position
            : -boss.transform.forward;
        away.y = 0f;
        away   = away.sqrMagnitude < 0.0001f ? -boss.transform.forward : away.normalized;

        // 着地点は「現在地と地続きの NavMesh 上」に限定する。SamplePosition を広半径で使うと
        // 壁際でアリーナ外・床下の別サーフェスを最近傍として拾い、着地後に埋まったまま
        // 復帰できなくなるため、NavMesh.Raycast で連結領域の縁までに丸める。
        Vector3 desired = ResolveTarget(pos, away);
        jumpTarget = ClampToConnectedNavMesh(pos, desired);

        // 横向きのまま跳ぶと不自然なので、まず進行方向の反対 (後退の姿勢) を向く。
        Vector3 moveDir = jumpTarget - pos;
        moveDir.y = 0f;
        faceDir = moveDir.sqrMagnitude > 0.0001f ? -moveDir.normalized : boss.transform.forward;

        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
    }

    // 進行方向の反対へ振り向く。向ききった (または時間切れ) らジャンプを開始する。
    private void UpdateTurn()
    {
        Quaternion look = Quaternion.LookRotation(faceDir, Vector3.up);
        boss.transform.rotation = Quaternion.RotateTowards(
            boss.transform.rotation, look, Mathf.Max(90f, cfg.TurnSpeed) * Time.deltaTime);

        bool facing  = Quaternion.Angle(boss.transform.rotation, look) <= TurnDoneAngle;
        bool timeout = elapsed >= MaxTurnDuration;
        if (!facing && !timeout) return;

        jumping = true;
        float duration = cfg.Speed > 0.01f ? cfg.Distance / cfg.Speed : 0.6f;
        boss.AnimatorBridge.PlayBackStepJump();
        boss.Locomotion.BeginJump(jumpTarget, duration, cfg.JumpHeight);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        if (!jumping)
        {
            UpdateTurn();
        }
        else if (!landed)
        {
            // Animator の ExitTime 遷移がジャンプ尺より先に進んでしまった場合、空中Idle へ戻す。
            boss.AnimatorBridge.HoldBackStepAir();

            if (boss.Locomotion.IsJumpComplete)
            {
                landed    = true;
                landTimer = Mathf.Max(0f, cfg.LandRecovery);
                boss.AnimatorBridge.PlayBackStepLand();
                boss.PlayLandingSquash(); // 着地の Squash & Stretch で重量感を出す
            }
        }
        else
        {
            landTimer -= Time.deltaTime;
            if (landTimer <= 0f) IsFinished = true;
        }

        // 安全弁: 想定外に長引いたら打ち切る (振り向きに使った時間ぶんは上乗せする)。
        if (elapsed >= Mathf.Max(0.5f, cfg.MaxDuration) + MaxTurnDuration) IsFinished = true;
    }

    // ---- 着地点の決定 (円形マップ境界の考慮) ----

    // 着地点を「現在地から地続きで歩ける NavMesh 上」に収める。
    // 途中に NavMesh の切れ目 (壁・段差) があれば、その手前に少し余裕をとって着地する。
    private Vector3 ClampToConnectedNavMesh(Vector3 pos, Vector3 desired)
    {
        const float SnapRadius = 2f;   // 足元 / 目標点を NavMesh へ吸着させる探索半径
        const float EdgeBack   = 0.5f; // NavMesh の縁ぴったりを避けて手前に寄せる距離

        // Raycast の起点は NavMesh 上の点である必要があるため、まず足元を吸着する。
        if (!NavMesh.SamplePosition(pos, out var startHit, SnapRadius, NavMesh.AllAreas))
            return pos; // 足元に NavMesh が無い異常時はその場ジャンプ (埋まるよりマシ)

        desired.y = startHit.position.y;
        if (NavMesh.Raycast(startHit.position, desired, out var rayHit, NavMesh.AllAreas))
        {
            // 縁に当たった → 縁のわずか手前まで飛ぶ。
            Vector3 back = startHit.position - rayHit.position;
            back.y = 0f;
            if (back.sqrMagnitude > EdgeBack * EdgeBack)
                back = back.normalized * EdgeBack;
            return rayHit.position + back;
        }

        // 遮られなければ目標点付近の NavMesh 上へ小半径で吸着する。
        return NavMesh.SamplePosition(desired, out var endHit, SnapRadius, NavMesh.AllAreas)
            ? endHit.position
            : rayHit.position; // Raycast 通過時の hit は終点の NavMesh 投影点
    }

    private Vector3 ResolveTarget(Vector3 pos, Vector3 away)
    {
        Vector3 desired = pos + away * cfg.Distance;

        // マップ情報が無ければ従来どおりの単純後退。
        if (cfg.MapRadius <= 0.01f || boss.MapCenter == null) return desired;

        Vector3 center = boss.MapCenter.position;
        if (InsideMap(desired, center)) return desired;

        // 真後ろが場外 (壁際) → マップ中心方向へ飛んで場内に戻る。
        Vector3 toCenter = center - pos;
        toCenter.y = 0f;
        Vector3 dir = toCenter.sqrMagnitude > 0.0001f
            ? toCenter.normalized
            : -away; // ほぼ中心に居る場合のフォールバック (通常起きない)
        return ClampToMap(pos + dir * cfg.Distance, center);
    }

    private bool InsideMap(Vector3 p, Vector3 center)
    {
        float limit = Mathf.Max(0.5f, cfg.MapRadius - cfg.EdgeMargin);
        Vector3 flat = p - center;
        flat.y = 0f;
        return flat.sqrMagnitude <= limit * limit;
    }

    private Vector3 ClampToMap(Vector3 p, Vector3 center)
    {
        float limit = Mathf.Max(0.5f, cfg.MapRadius - cfg.EdgeMargin);
        Vector3 flat = p - center;
        float y = p.y;
        flat.y = 0f;
        if (flat.sqrMagnitude <= limit * limit) return p;
        Vector3 clamped = center + flat.normalized * limit;
        clamped.y = y;
        return clamped;
    }
}
