using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// テールレイン。見下ろし固定カメラへ切り替え、ボスがマップ上の複数地点へ連続でジャンプ叩きつけを行い、
/// プレイヤーはそれをひたすら避ける回避演出。カービィディスカバリーのボスのような
/// 「視点が変わって避けに徹する」シーンを狙う。
///
/// TailAttack（単発）とは独立した行動で、既存の B1_TailAttackAction / B1_TailChargeAction は変更しない。
/// ジャンプ移動は B1_Locomotion.BeginJump、着弾ダメージは B1_DamageWave を流用する。
///
/// フロー: Intro（カメラ切替＋溜め） → [ Telegraph（着弾予告サークル表示）→ Jump（跳躍）→
///         Slam（着地＝ドーナッツ波） → Recover ] × StrikeCount → Outro（カメラ復帰）。
///
/// 着弾予告は TelegraphAheadCount 個先まで先読みでキューに積んで同時表示し、
/// 各サークルは自分の着弾が近づくにつれ赤が濃く・明滅が速くなる（B1_TelegraphCircle.SetUrgency）。
///
/// 演出中もプレイヤー入力は凍結しない（避けるゲームプレイ自体が見せ場のため）。
/// </summary>
public class B1_TailRainAction : B1_Action
{
    private enum Phase { Intro, Telegraph, Jumping, Recover, Outro, Done }

    // 予告済みの着弾予定1件。initialEta は予告を出した時点の「着弾までの推定秒数」で、
    // 接近度（urgency）を 0→1 に正規化する分母として使う。
    private struct QueuedStrike
    {
        public Vector3            target;
        public B1_TelegraphCircle circle;
        public float              initialEta;
    }

    private TailRainActionConfig cfg;
    private float   _speedMult = 1f; // 怒りモード（激昂）中の攻撃速度倍率。連撃テンポを短縮する
    private Phase   _phase;
    private int     _strikesDone;
    private int     _strikesQueued;   // 予告済み（実行済み含む）の総数。StrikeCount で打ち止め
    private float   _timer;
    private float   _jumpElapsed;
    private int     _lastTargetIndex = -1;
    private bool    _cameraOn;
    private readonly List<QueuedStrike> _queue = new();

    public B1_TailRainAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        cfg            = boss.Settings.TailRain;
        _speedMult     = boss.AttackSpeedMult;
        _strikesDone   = 0;
        _strikesQueued = 0;
        _cameraOn      = false;
        _queue.Clear();

        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled)
            BGMManager.SetVolumeScale(bgm.TailRainBedVolume, bgm.TailRainBedFadeDuration);

        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);

        // 見下ろし固定カメラへ（入力凍結はしない）。vcam 未設定なら false で、カメラは通常のまま進行する。
        if (boss.CameraDirector != null)
        {
            _cameraOn = boss.CameraDirector.BeginTailRainCamera();
            // 固定カメラへ切り替わる時だけ、移動基準を「今映っている画面」へ。
            // (TPS が止まるので、プレイヤーの向きに依存せず見下ろし画面基準で避けられる)
            if (_cameraOn) boss.CameraDirector.SetPlayerFixedCameraMove(true);
        }

        _phase = Phase.Intro;
        _timer = Mathf.Max(0f, cfg.IntroDelay);

        FillTelegraphQueue(); // Intro の溜め中から先の着弾地点を予告しておく
    }

    public override void Update()
    {
        UpdateTelegraphUrgency();

        switch (_phase)
        {
            case Phase.Intro:
                _timer -= Time.deltaTime;
                if (_timer <= 0f) StartTelegraph();
                break;

            case Phase.Telegraph:
                _timer -= Time.deltaTime;
                if (_timer <= 0f) StartJump();
                break;

            case Phase.Jumping:
                _jumpElapsed += Time.deltaTime;
                if (boss.Locomotion.IsJumpComplete) Slam();
                break;

            case Phase.Recover:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    if (_strikesDone >= Mathf.Max(1, cfg.StrikeCount)) StartOutro();
                    else                                               StartTelegraph();
                }
                break;

            case Phase.Outro:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    EndCamera();
                    _phase     = Phase.Done;
                    IsFinished = true;
                }
                break;
        }
    }

    // ---- 各フェーズ遷移 ----

    private void StartTelegraph()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop); // 跳ぶ前に接地させ、次の BeginJump で確実にフラグをリセットさせる
        _phase = Phase.Telegraph;
        _timer = Mathf.Max(0f, cfg.TelegraphLead) / _speedMult;

        FillTelegraphQueue(); // 通常は Slam 後に補充済み。空になっている場合の保険
    }

    private void StartJump()
    {
        boss.AnimatorBridge.ResumeAnimator(); // 外部でヒットストップ等が速度を触っていた場合の保険
        boss.AnimatorBridge.PlayTailAttackJumpDirect(); // 跳び上がりモーション。しっぽ立ち(TailAttack)は着地の瞬間に入る
        boss.PlayTailJumpSound();
        boss.StartTrail();
        boss.Locomotion.BeginJump(NextTarget(), cfg.JumpDuration / _speedMult, cfg.JumpHeight);
        _jumpElapsed = 0f;
        _phase = Phase.Jumping;
    }

    private void Slam()
    {
        Vector3 center = boss.transform.position;
        bool finisher = _strikesDone + 1 >= Mathf.Max(1, cfg.StrikeCount);

        boss.PlayTailRainImpactSound(center, finisher);
        DetonateFrontTelegraph(); // いま着弾したぶんの予告を完了フラッシュさせる（→自壊）
        boss.StopTrail();
        boss.PlayExplosionVFX(center);
        boss.PlayLandingImpact(center);
        B1_AttackBounceObject.LaunchInRadius(center, cfg.AoeRadius);
        SpawnDamageWave(center);

        // 新TailAttackはしっぽで立つループモーション。衝撃波が出るこの瞬間に再生開始し、
        // 次の StartJump / StartOutro までしっぽ立ちのままループ再生させる。
        boss.AnimatorBridge.PlayTailAttackImmediate();

        _strikesDone++;
        _phase = Phase.Recover;
        // リズムランダム: 基本間隔にジッターを加算し、「読めそうで読めない」連撃のリズムを作る。
        float jitter = Random.Range(Mathf.Min(cfg.RecoverDelayJitter.x, cfg.RecoverDelayJitter.y),
                                    Mathf.Max(cfg.RecoverDelayJitter.x, cfg.RecoverDelayJitter.y));
        _timer = Mathf.Max(0f, cfg.RecoverDelay + jitter) / _speedMult;

        FillTelegraphQueue(); // 1つ消化したので、その先の着弾地点を新たに予告する
    }

    private void StartOutro()
    {
        // 最後の Slam のしっぽ立ちループを終了し、TailAttackLand で通常姿勢へ降りる。
        boss.AnimatorBridge.ResumeAnimator();
        boss.AnimatorBridge.PlayTailAttackLand();
        _phase = Phase.Outro;
        _timer = Mathf.Max(0f, cfg.OutroDelay); // カメラは見下ろしのまま少し余韻を残してから戻す
    }

    // ---- 着弾予告（先読みキュー） ----

    private Vector3 NextTarget()
        => _queue.Count > 0 ? _queue[0].target : boss.transform.position; // 空は想定外だが着地不能にはしない

    // TelegraphAheadCount 個先（残り回数が上限）まで着弾地点を先に決めて予告サークルを出す。
    private void FillTelegraphQueue()
    {
        int ahead = Mathf.Max(1, cfg.TelegraphAheadCount);
        int total = Mathf.Max(1, cfg.StrikeCount);

        while (_queue.Count < ahead && _strikesQueued < total)
        {
            Vector3 target = PickTarget(firstStrike: _strikesQueued == 0);
            var circle = B1_TelegraphCircle.Spawn(target, cfg.TelegraphRadius, cfg.TelegraphColor, cfg.TelegraphMaterial);
            circle.SetUrgencyStyle(cfg.TelegraphFarAlpha, cfg.TelegraphPulseBoost); // 事前表示と最終表示の値差は Settings で調整

            _queue.Add(new QueuedStrike
            {
                target     = target,
                circle     = circle,
                initialEta = Mathf.Max(0.05f, EstimateTimeToLand(_queue.Count)),
            });
            _strikesQueued++;
        }
    }

    // キュー slot 番目（0=次に着弾）の着弾までの推定秒数。
    // ジャンプの実時間は物理依存だが、視覚演出用なので JumpDuration ベースの見積もりで十分。
    private float EstimateTimeToLand(int slot)
    {
        float lead    = Mathf.Max(0f, cfg.TelegraphLead) / _speedMult;
        float jump    = Mathf.Max(0f, cfg.JumpDuration) / _speedMult;
        float recover = Mathf.Max(0f, cfg.RecoverDelay) / _speedMult;
        float cycle   = lead + jump + recover;

        float first;
        switch (_phase)
        {
            case Phase.Intro:     first = Mathf.Max(0f, _timer) + lead + jump;         break; // Intro → Telegraph → Jump
            case Phase.Telegraph: first = Mathf.Max(0f, _timer) + jump;                break;
            case Phase.Jumping:   first = Mathf.Max(0f, jump - _jumpElapsed);          break;
            case Phase.Recover:   first = Mathf.Max(0f, _timer) + lead + jump;         break; // Recover → Telegraph → Jump
            default:              first = 0f;                                          break;
        }
        return first + slot * cycle;
    }

    // 各予告サークルへ接近度 0..1 を毎フレーム反映する（0=まだ先で薄い、1=着弾直前で濃い）。
    private void UpdateTelegraphUrgency()
    {
        for (int i = 0; i < _queue.Count; i++)
        {
            QueuedStrike q = _queue[i];
            if (q.circle == null) continue;

            float remaining = EstimateTimeToLand(i);
            q.circle.SetUrgency(1f - Mathf.Clamp01(remaining / q.initialEta));
        }
    }

    // 着弾したぶんの予告を完了フラッシュさせてキューから外す。
    // 以後はサークル自身がフラッシュ→自壊するので、こちらは参照を手放すだけ。
    private void DetonateFrontTelegraph()
    {
        if (_queue.Count == 0) return;

        if (_queue[0].circle != null)
            _queue[0].circle.Detonate();
        _queue.RemoveAt(0);
    }

    private void DestroyAllTelegraphs()
    {
        foreach (QueuedStrike q in _queue)
            if (q.circle != null)
                q.circle.Dismiss(); // シールを剥がすように消す（自壊まで自走する）
        _queue.Clear();
    }

    // ---- ダメージ・ターゲット ----

    // TailAttack と同じドーナッツ波。中心の穴(WaveInnerRadius)は安全地帯、波面が通過した対象へ一度だけ当たる。
    // 着弾ごとに WaveScaleRange から倍率を抽選し、半径・帯幅・時間を同倍率で伸縮する
    // (時間も掛けることで波の広がり速度は一定に保つ)。
    private void SpawnDamageWave(Vector3 center)
    {
        float k = Random.Range(Mathf.Min(cfg.WaveScaleRange.x, cfg.WaveScaleRange.y),
                               Mathf.Max(cfg.WaveScaleRange.x, cfg.WaveScaleRange.y));
        k = Mathf.Max(0.1f, k);

        B1_DamageWave.Spawn(center, new B1_DamageWave.Params
        {
            duration    = cfg.WaveDuration * k,
            innerRadius = cfg.WaveInnerRadius,
            maxRadius   = cfg.AoeRadius * k,
            thickness   = cfg.WaveThickness * k,
            damage      = cfg.Damage,
            effects     = cfg.Effects.WithPlayerDamageType(cfg.DamageType),
            color       = cfg.WaveColor,
            material    = cfg.WaveMaterial,
            spikeDepth  = cfg.WaveSpikeDepth, // 0 = 完全な真円
            jumpDodgeHeight = cfg.JumpDodgeHeight, // これ以上跳んでいれば波が下を通過＝ジャンプ回避
            height          = cfg.JumpDodgeHeight, // 見た目もこの高さで山なりに立ち上げ、回避ラインを画で示す
        }, boss.Health, boss);
    }

    // 着弾地点：マップに置いた候補地点(boss.TailRainTargets)からランダムに選ぶ（直前と同じ地点は避ける）。
    // 候補が未設定のときだけ、フォールバックとしてプレイヤー周辺を狙う従来動作にする。
    // ジャンプ後に NavMeshAgent を復帰させるので、地面(NavMesh)上へスナップして着地不能地点を避ける。
    private Vector3 PickTarget(bool firstStrike)
    {
        Transform[] targets = boss.TailRainTargets;
        Vector3 target;

        if (targets != null && targets.Length > 0)
        {
            int idx = PickNextTargetIndex(targets.Length);
            Transform t = targets[idx];
            target = t != null ? t.position : boss.transform.position; // 未割当スロットの保険
        }
        else
        {
            // フォールバック：候補地点が無ければプレイヤー周辺を狙う。
            Vector3 basePos = boss.PlayerTransform != null
                ? boss.PlayerTransform.position
                : boss.transform.position;

            if (firstStrike && cfg.FirstStrikeOnPlayer)
            {
                target = basePos;
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * Mathf.Max(0f, cfg.TargetSpread);
                target = basePos + new Vector3(r.x, 0f, r.y);
            }
        }

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, Mathf.Max(2f, cfg.TargetSpread + 2f), NavMesh.AllAreas))
            target = hit.position;

        return target;
    }

    // 候補地点のインデックスをランダムに選ぶ。連続で同じ地点を選ばない（ボスが動いて見えるように）。
    private int PickNextTargetIndex(int count)
    {
        if (count <= 1) return 0;

        int idx;
        if (_lastTargetIndex < 0)
        {
            idx = Random.Range(0, count);
        }
        else
        {
            // 直前の1個を除いた count-1 個から一様に選ぶ（必ず前回と異なる）。
            idx = Random.Range(0, count - 1);
            if (idx >= _lastTargetIndex) idx++;
        }

        _lastTargetIndex = idx;
        return idx;
    }

    // ---- 後片付け ----

    private void EndCamera()
    {
        if (_cameraOn && boss.CameraDirector != null)
        {
            boss.CameraDirector.SetPlayerFixedCameraMove(false); // 移動基準を通常(TPS)へ戻す
            boss.CameraDirector.EndTailRainCamera();
        }
        _cameraOn = false;
    }

    public override void Exit()
    {
        // 途中で割り込まれても（死亡・Phase 遷移など）確実に元へ戻す。
        DestroyAllTelegraphs();
        EndCamera();
        boss.StopTrail();
        boss.AnimatorBridge.ResumeAnimator();
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.TailRainBedFadeDuration);
        base.Exit();
    }
}
