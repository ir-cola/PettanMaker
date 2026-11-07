/**===========================================================================

 File   : 白兎(Enemy02) ルート逃走 [Enemy02_RouteEscape.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/4
 -----------------------------------------------------------------------------
 スポーン後の白兎の逃走挙動。2フェーズ構成。

 [Hop]  routeRoot の子 (順番) を放物線ジャンプで跳んでいく (段差を飛び越える)。
        P0→P1→...→終点。各ホップは 水平Lerp + 山なりの放物線 (apex = jumpHeight)。
 [Flee] 終点到達後は広場を「ダッシュ → 立ち止まる (0.9〜1.5秒) → ダッシュ」の
        リズムで駆け回りながらプレイヤーから逃げる。立ち止まりが攻撃 (スタンプ)
        を当てるチャンス。休憩中もプレイヤーが至近 (PanicRadius) まで来たら逃げ出す。
        行き先は広場内のランダム地点のうちプレイヤーから最も遠い候補。
        広場の範囲は 'EscapeRoom' のコライダー (areaCollider) で定義する。
        コライダーが無い場合の代替: ルート最後の区間 (P3→P4) の向きを境界とし、
        「終点より手前へ戻らない」半径 roamRadius の円内を駆け回る。

 ・NavMesh 不要 (Stage_1 は未ベイク)。Transform 移動 + 下向きレイキャストで接地。
 ・プレイヤーが boostRadius 内に近づくと加速。
 ・前方に障害物 / 地面が無い (崖) 場合は行き先を選び直す。
 ・loop=true ならルートのホップを周回し続ける (Flee へは移行しない)。
 ・StageIntroSequence.FinishIntro から AddComponent + Init で装着される。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public class Enemy02_RouteEscape : MonoBehaviour {

    private enum Phase { Hop, Flee }

    private readonly List<Vector3> _points = new();
    private Transform _playerTf;
    private EnemyBase _owner;
    private EnemyHealth _health;

    private float _runSpeed = 5f;
    private float _boostMultiplier = 1.6f;
    private float _boostRadius = 6f;
    private float _jumpHeight = 1.5f;
    private float _roamRadius = 10f;
    private bool _loop;
    private Collider _area;                      // 逃走エリア (EscapeRoom)。null なら境界平面+半径で代替

    private Phase _phase = Phase.Hop;
    private int _index;
    private int _groundMask = ~0;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    // --- Hop 状態 ---
    private bool _hopping;
    private Vector3 _hopStart, _hopEnd;
    private float _hopT, _hopDur;
    private float _pauseTimer;                   // 着地後の小休止 (ウサギらしい「タメ」)

    // --- Flee 状態 ---
    private Vector3 _roamCenter;
    private Vector3 _roamTarget;
    private float _roamTimer;
    private float _restTimer;                    // >0 の間はその場で休憩 (攻撃を当てるチャンス)
    private Vector3 _boundaryOrigin;             // 終点 (エリア未指定時のみ使用)
    private Vector3 _boundaryForward;            // 最後の区間の向き。この平面よりルート側へ戻らない

    private const float HopPause = 0.12f;        // 着地〜次のジャンプまでの間
    private const float HopMinDur = 0.30f;       // ホップ時間の下限 (近距離でも一瞬にしない)
    private const float HopMaxDur = 1.20f;       // ホップ時間の上限 (遠距離でも間延びさせない)
    private const float TurnSpeed = 720f;        // 回頭速度 (度/秒)
    private const float RoamRepickTime = 3.0f;   // 1回のダッシュの最長時間 (到達しなくても休憩へ)
    private const float RoamArriveDist = 0.6f;   // 行き先到達とみなす水平距離
    private const float ObstacleCheckDist = 0.8f;
    private const float RestMin = 0.9f;          // ダッシュ後の休憩時間 (この間に攻撃を当てられる)
    private const float RestMax = 1.5f;
    private const float PanicRadius = 3.0f;      // 休憩中でもこの距離まで近づかれたら逃げ出す

    public void Init(Transform routeRoot, Transform player,
                     float runSpeed, float boostMultiplier, float boostRadius,
                     bool loop, float jumpHeight, float roamRadius, Collider areaCollider) {
        _playerTf = player;
        _runSpeed = runSpeed;
        _boostMultiplier = boostMultiplier;
        _boostRadius = boostRadius;
        _loop = loop;
        _jumpHeight = Mathf.Max(0.2f, jumpHeight);
        _roamRadius = Mathf.Max(2f, roamRadius);
        _area = areaCollider;

        _owner = GetComponent<EnemyBase>();
        _health = GetComponent<EnemyHealth>();

        int playerLayer = LayerMask.NameToLayer("Player");
        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (playerLayer >= 0) _groundMask &= ~(1 << playerLayer);
        if (weaponLayer >= 0) _groundMask &= ~(1 << weaponLayer);

        // エリアコライダーは範囲定義用。solid のままだと見えない壁になって
        // プレイヤーも入れないので、トリガーへ切り替える (MeshCollider 非convex は不可)
        if (_area != null && !_area.isTrigger) {
            if (_area is MeshCollider mc && !mc.convex) {
                Debug.LogWarning("[Enemy02_RouteEscape] EscapeRoom の MeshCollider は isTrigger にできません。" +
                                 "見えない壁になるため IsTrigger を ON にするか Box/Sphere コライダーにしてください。");
            }
            else {
                _area.isTrigger = true;
                Debug.Log("[Enemy02_RouteEscape] EscapeRoom のコライダーを範囲定義用に isTrigger=true へ変更しました。");
            }
        }

        _points.Clear();
        if (routeRoot != null) {
            for (int i = 0; i < routeRoot.childCount; i++) {
                var p = routeRoot.GetChild(i);
                if (p != null && p.gameObject.activeInHierarchy) _points.Add(p.position);
            }
        }
        if (_points.Count == 0) { BeginFlee(); return; }

        // ルート走行中は通常 AI を止めておく (Init は AI 無効状態で呼ばれる想定だが念のため)
        SetAIEnabled(false);
        _pauseTimer = HopPause;   // 一拍おいて最初のジャンプ
    }

    void Update() {
        if (_health != null && _health.IsDead) {
            _owner?.UpdateAnimator(0f, false);
            enabled = false;
            return;
        }

        if (_phase == Phase.Hop) UpdateHop();
        else UpdateFlee();
    }

    /**=======================================================================
      Hop: ポイント間を放物線ジャンプで跳んでいく
    =======================================================================**/
    private void UpdateHop() {
        if (_index >= _points.Count) { BeginFlee(); return; }

        if (!_hopping) {
            // 着地後の小休止。次のポイントの方を向いて待つ
            Vector3 face = _points[_index] - transform.position;
            FaceDirection(face);
            _owner?.UpdateAnimator(0f, false);

            _pauseTimer -= Time.deltaTime;
            if (_pauseTimer > 0f) return;

            // 次のホップを開始。着地点はポイント XZ の地面高さへスナップ
            _hopStart = transform.position;
            _hopEnd = _points[_index];
            _hopEnd.y = SampleGroundY(_hopEnd, _hopEnd.y);

            Vector3 flat = _hopEnd - _hopStart; flat.y = 0f;
            _hopDur = Mathf.Clamp(flat.magnitude / Mathf.Max(0.5f, _runSpeed), HopMinDur, HopMaxDur);
            _hopT = 0f;
            _hopping = true;
            return;
        }

        _hopT += Time.deltaTime / _hopDur;
        float t = Mathf.Clamp01(_hopT);

        // 水平 Lerp + 山なりの放物線 (直線補間の上に apex=_jumpHeight の弧を乗せる)
        Vector3 pos = Vector3.Lerp(_hopStart, _hopEnd, t);
        pos.y = Mathf.Lerp(_hopStart.y, _hopEnd.y, t) + _jumpHeight * 4f * t * (1f - t);
        transform.position = pos;

        FaceDirection(_hopEnd - _hopStart);
        _owner?.UpdateAnimator(_runSpeed, false);   // 跳躍中は走りモーション

        if (_hopT >= 1f) {
            transform.position = _hopEnd;
            _hopping = false;
            _pauseTimer = HopPause;
            _index++;
            if (_index >= _points.Count) {
                if (_loop) _index = 0;              // 周回モード: 最初のポイントへ跳び続ける
                else BeginFlee();
            }
        }
    }

    /**=======================================================================
      Flee: 広場 (EscapeRoom コライダー or 終点先の半径) を駆け回って逃げる
    =======================================================================**/
    private void BeginFlee() {
        _phase = Phase.Flee;

        // 代替境界: 最後の2点の向き。ルートが1点以下なら現在の前方を使う
        int n = _points.Count;
        Vector3 fwd = (n >= 2) ? _points[n - 1] - _points[n - 2] : transform.forward;
        fwd.y = 0f;
        _boundaryForward = (fwd.sqrMagnitude > 1e-4f) ? fwd.normalized : Vector3.forward;
        _boundaryOrigin = (n >= 1) ? _points[n - 1] : transform.position;

        _roamCenter = (_area != null)
            ? _area.bounds.center
            : _boundaryOrigin + _boundaryForward * (_roamRadius * 0.5f);

        PickRoamTarget();
    }

    private void UpdateFlee() {
        Vector3 pos = transform.position;

        // 休憩中: その場に立ち止まってプレイヤーの方を向く (スタンプを当てるチャンス)。
        // ただし至近距離まで詰められたら休憩を切り上げて逃げ出す。
        if (_restTimer > 0f) {
            _restTimer -= Time.deltaTime;
            if (_playerTf != null) {
                FaceDirection(_playerTf.position - pos);
                if (Vector3.Distance(pos, _playerTf.position) < PanicRadius) _restTimer = 0f;
            }
            _owner?.UpdateAnimator(0f, false);
            if (_restTimer <= 0f) PickRoamTarget();   // 休憩明けに次のダッシュ先を決める
            return;
        }

        _roamTimer -= Time.deltaTime;
        Vector3 to = _roamTarget - pos;
        to.y = 0f;
        if (_roamTimer <= 0f || to.sqrMagnitude <= RoamArriveDist * RoamArriveDist) {
            BeginRest();                              // ダッシュ1本ごとに立ち止まる
            return;
        }

        Vector3 dir = to.normalized;
        if (IsBlocked(pos, dir)) { PickRoamTarget(); return; }   // 壁/崖は休まず行き先だけ変更

        float speed = _runSpeed;
        if (_playerTf != null && Vector3.Distance(pos, _playerTf.position) < _boostRadius)
            speed *= _boostMultiplier;

        Vector3 next = pos + dir * (speed * Time.deltaTime);
        next = ClampToArea(next, pos);
        next.y = SampleGroundY(next, pos.y);
        transform.position = next;

        FaceDirection(dir);
        _owner?.UpdateAnimator(speed, false);
    }

    private void BeginRest() {
        _restTimer = Random.Range(RestMin, RestMax);
        _owner?.UpdateAnimator(0f, false);
    }

    // 広場内のランダム地点を数個試し、プレイヤーから最も遠い候補を行き先にする
    private void PickRoamTarget() {
        _roamTimer = RoamRepickTime;

        Vector3 best = _roamCenter;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < 12; i++) {
            Vector3 cand;
            if (_area != null) {
                Bounds b = _area.bounds;
                cand = new Vector3(Random.Range(b.min.x, b.max.x), b.center.y, Random.Range(b.min.z, b.max.z));
                cand = _area.ClosestPoint(cand);            // 回転したコライダーでも内側へ寄せる
            }
            else {
                Vector2 rc = Random.insideUnitCircle;
                cand = _roamCenter + new Vector3(rc.x, 0f, rc.y) * _roamRadius;
                // 終点より手前 (ルート側) は候補にしない
                if (Vector3.Dot(cand - _boundaryOrigin, _boundaryForward) < 0.5f) continue;
            }

            if (!HasGround(cand)) continue;

            float score = (_playerTf != null)
                ? Vector3.Distance(cand, _playerTf.position)
                : Random.value;
            if (Vector3.Distance(cand, transform.position) < 2f) score *= 0.3f;  // 近すぎる行き先は避ける

            if (score > bestScore) { bestScore = score; best = cand; }
        }

        _roamTarget = best;
    }

    // エリア外に出ないよう位置をクランプする
    private Vector3 ClampToArea(Vector3 next, Vector3 current) {
        if (_area != null) {
            Vector3 probe = next;
            probe.y = _area.bounds.center.y;                // 高さはコライダー中心で判定 (XZ を拘束したい)
            Vector3 cp = _area.ClosestPoint(probe);
            next.x = cp.x;
            next.z = cp.z;
            return next;
        }

        // 代替: 終点より手前へ戻らない + 半径内
        float d = Vector3.Dot(next - _boundaryOrigin, _boundaryForward);
        if (d < 0f) next -= _boundaryForward * d;

        Vector3 fromC = next - _roamCenter;
        fromC.y = 0f;
        if (fromC.magnitude > _roamRadius)
            next = new Vector3(_roamCenter.x, next.y, _roamCenter.z) + fromC.normalized * _roamRadius;
        return next;
    }

    // 前方が壁 (コライダー) or 足場なし (崖) かどうか
    private bool IsBlocked(Vector3 pos, Vector3 dir) {
        Vector3 origin = pos + Vector3.up * 0.4f;
        int n = Physics.RaycastNonAlloc(origin, dir, _hits, ObstacleCheckDist, _groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) {
            if (IsValidHit(_hits[i])) return true;
        }

        // 一歩先に地面が無ければ崖とみなす
        return !HasGround(pos + dir * ObstacleCheckDist);
    }

    private bool HasGround(Vector3 at) {
        Vector3 origin = at + Vector3.up * 1.5f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, 6f, _groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) {
            if (IsValidHit(_hits[i])) return true;
        }
        return false;
    }

    // 足元へレイキャストして地面の高さに沿わせる (自分自身/エリアコライダーは無視)
    private float SampleGroundY(Vector3 at, float fallbackY) {
        Vector3 origin = at + Vector3.up * 1.5f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, 6f, _groundMask, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < n; i++) {
            if (!IsValidHit(_hits[i])) continue;
            if (_hits[i].point.y > best) { best = _hits[i].point.y; found = true; }   // 最も高い=一番手前の地面
        }
        return found ? best : fallbackY;
    }

    // レイキャスト結果のうち、自分自身とエリアコライダーは地形とみなさない
    private bool IsValidHit(RaycastHit h) {
        if (h.transform == null) return false;
        if (h.transform.IsChildOf(transform)) return false;
        if (_area != null && h.collider == _area) return false;
        return true;
    }

    private void FaceDirection(Vector3 dir) {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * Time.deltaTime);
    }

    private void SetAIEnabled(bool on) {
        var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = on;
        foreach (var mb in GetComponents<MonoBehaviour>()) {
            if (mb is EnemyBase) mb.enabled = on;
        }
    }
}
