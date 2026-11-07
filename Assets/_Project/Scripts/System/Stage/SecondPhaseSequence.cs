/**===========================================================================

 File   : セカンドフェーズ召喚シネマ [SecondPhaseSequence.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/16
 -----------------------------------------------------------------------------
 SecondPhase エリアへプレイヤーが入ると発動するウェーブ制の召喚演出。

 ■ 配置はシーン階層から取得する
   PhaseSpawnPoint
     ├ Phase1   ← 1 ウェーブ
     │   ├ "P0 - Enemy01"   末尾トークン EnemyNN が召喚する敵種
     │   ├ "P1 - Enemy04"
     │   └ ...
     ├ Phase2
     └ Phase3
   各 "Pn - EnemyNN" が召喚地点。名前末尾の EnemyNN を enemyTypes テーブルで
   prefab とシール絵に解決する。

 ■ ウェーブ制の流れ (1 ウェーブごとに)
   1. カメラを奪ってアリーナへ寄せる (FOV をゲーム標準へ強制 / 壁貫通しない)
   2. 点 P0→Pn を pointStagger ずつ時間差で:
        地面にシールがディゾルブ出現 → ペラッと剥がれて舞い上がる
        → 少し遅れて、その下からエネミーがポップで生まれる
      (ペラ・ペラ・ペラ … ぽ・ぽ・ぽ と鳴るカスケード)
   3. カメラと操作をプレイヤーへ返し、そのウェーブの敵 AI を起動
   4. そのウェーブの敵を全滅させるまで待つ
   → 全滅したら次のウェーブへ。全ウェーブ終了で終了。

 演出機構は StageIntroSequence の実績パターンを流用しつつ、トリガー起動・
 ウェーブ制のため独立実装している。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Collider))]
public class SecondPhaseSequence : MonoBehaviour {

    /// <summary>全フェーズ(全ウェーブ)の敵を殲滅し終えた瞬間に発火。OutGate 出現などに使う。</summary>
    public event System.Action AllCleared;
    /// <summary>全滅済みか (購読が間に合わなかった場合のフォールバック用)。</summary>
    public bool IsAllCleared { get; private set; }

    /// <summary>点名の EnemyNN → 召喚 prefab / 剥がすシール絵 の対応。</summary>
    [System.Serializable]
    public class EnemyType {
        [Tooltip("点名の末尾トークン (例 \"Enemy01\")。大小文字・空白は無視して照合")]
        public string key;
        [Tooltip("召喚するエネミー prefab")]
        public GameObject prefab;
        [Tooltip("地面に貼って剥がすシールの絵 (Sticker/World の _MainTex)。未設定は defaultStickerTexture")]
        public Texture stickerTexture;
    }

    /// <summary>階層が無い/使わない時のための旧式フェーズ定義 (後方互換)。</summary>
    [System.Serializable]
    public class PhaseDef {
        public string label;
        public Transform spawnPoint;
        public Texture stickerTexture;
        public GameObject enemyPrefab;
        public Transform cameraFocus;
        public GameObject summonVfx;
    }

    [Header("フェーズ配置 (シーン階層から取得)")]
    [Tooltip("PhaseSpawnPoint (子に Phase* を持つ)。未設定なら phaseSpawnRootName で自動検索")]
    [SerializeField] private Transform phaseSpawnRoot;
    [Tooltip("phaseSpawnRoot 未設定時に名前で探すルート")]
    [SerializeField] private string phaseSpawnRootName = "PhaseSpawnPoint";

    [Header("エネミー種別 (点名 EnemyNN → prefab / シール絵)")]
    [SerializeField] private List<EnemyType> enemyTypes = new List<EnemyType>();
    [Tooltip("種別にシール絵が無い時に使う既定のシール絵")]
    [SerializeField] private Texture defaultStickerTexture;

    [Header("旧式フェーズ (階層が無い時のみ使用: 後方互換)")]
    [SerializeField] private List<PhaseDef> phases = new List<PhaseDef>();

    [Header("起動 (このオブジェクトの Collider は IsTrigger 必須)")]
    [Tooltip("この tag のオブジェクトが入ると発動")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("一度きり (再入場では再生しない)")]
    [SerializeField] private bool oneShot = true;
    [Tooltip("ウェーブ制: Phase の敵を全滅させてから次の Phase を召喚する")]
    [SerializeField] private bool waveGated = true;

    [Header("カメラ (演出中は後追いカメラを止めて直接駆動)")]
    [Tooltip("アリーナ中心に対するカメラの寄り(手前・上)位置オフセット(ワールド基準)")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 11f, -8.5f);
    [Tooltip("寄り始め(引き)の追加オフセット。ここから cameraOffset へ押し込む")]
    [SerializeField] private Vector3 cameraWideExtra = new Vector3(0f, 3f, -5f);
    [Tooltip("注視点の高さオフセット(地面より少し上を見る)")]
    [SerializeField] private float lookAtHeight = 1.2f;
    [Tooltip("フェーズ頭のカメラ寄せ時間")]
    [SerializeField] private float cameraMoveTime = 1.3f;
    [Tooltip("演出終了時、後追いへ戻す時間")]
    [SerializeField] private float cameraReturnTime = 1.2f;

    [Header("カメラ壁当たり判定 (壁貫通防止)")]
    [Tooltip("カメラを遮る壁のレイヤー(Player/Weapon は自動除外、敵は無視)")]
    [SerializeField] private LayerMask cameraCollisionMask = ~0;
    [Tooltip("カメラが衝突する tag。これ以外 (Wall_Clear 等) は素通りさせる。空なら tag で絞らない")]
    [SerializeField] private string[] cameraCollisionTags = { "Wall", "Ground" };
    [Tooltip("当たり判定の球半径")]
    [SerializeField] private float cameraCollisionRadius = 0.35f;
    [Tooltip("壁にめり込まないための余白")]
    [SerializeField] private float cameraCollisionBuffer = 0.3f;
    [Tooltip("壁で寄せられても、これ以上は近づかない最小距離")]
    [SerializeField] private float minCameraDistance = 2.5f;

    [Header("カスケード (ぱらぱら)")]
    [Tooltip("各シールがディゾルブ出現する時間差(波状に出す)")]
    [SerializeField] private float appearStagger = 0.06f;
    [Tooltip("全シール出現後、剥がし始めるまで地面のシールを見せる間")]
    [SerializeField] private float allAppearHold = 0.25f;
    [Tooltip("シールが順番に剥がれる時間差(ぱらぱら。大きいほど1枚ずつ)")]
    [SerializeField] private float peelStagger = 0.16f;

    [Header("シール")]
    [Tooltip("World/Sticker を使うマテリアル。未設定なら Sticker/World シェーダーから生成")]
    [SerializeField] private Material stickerMaterialTemplate;
    [Tooltip("シール板ポリのサイズ (m)")]
    [SerializeField] private float stickerSize = 3f;
    [Tooltip("地面からの浮かせ量 (z-fight 回避)")]
    [SerializeField] private float stickerLift = 0.03f;
    [Tooltip("地面へディゾルブで現れる時間")]
    [SerializeField] private float dissolveInTime = 0.3f;
    [Tooltip("出現後、剥がし始めるまでの間")]
    [SerializeField] private float stickerSettle = 0.1f;
    [Tooltip("剥がれ上がる時間")]
    [SerializeField] private float peelTime = 0.45f;
    [Tooltip("舞い上がってディゾルブで消える時間")]
    [SerializeField] private float vanishTime = 0.7f;
    [Tooltip("剥がれる方向 X / Y (白兎と同じ既定 1,1)")]
    [SerializeField] private float peelDirX = 1f;
    [SerializeField] private float peelDirY = 1f;
    [Tooltip("シールの上下反転")]
    [SerializeField] private bool peelFlipY = false;

    [Header("召喚")]
    [Tooltip("エネミーがポップで出る時間")]
    [SerializeField] private float enemyPopTime = 0.28f;
    [Tooltip("召喚しきってから操作を返すまでの余韻")]
    [SerializeField] private float admireHold = 0.6f;
    [Tooltip("光る召喚エフェクト。未設定は Resources/VFX/WarpHole_Portal")]
    [SerializeField] private GameObject summonVfx;
    [Tooltip("召喚 VFX の自動破棄秒")]
    [SerializeField] private float summonVfxLifetime = 2.0f;
    [Tooltip("召喚 VFX の倍率")]
    [SerializeField] private float summonVfxScale = 1f;
    [Tooltip("ウェーブ全滅判定を始めるまでの猶予(出現直後に誤判定しないため)")]
    [SerializeField] private float waveClearGrace = 0.3f;

    // ---- 内部 ----
    private struct Pose { public Vector3 pos; public Quaternion rot; }

    private bool _triggered;
    private PlayerLifetimeScope _player;
    private TPSCameraController _cam;
    private Transform _camTf;
    private Camera _camComponent;
    private Behaviour _brain;                 // CinemachineBrain (あれば演出中は無効化)
    private GameObject _defaultVfx;
    private Dictionary<string, EnemyType> _typeLookup;
    private int _pointRoutinesRunning;
    private int _playerLayerBit;
    private int _weaponLayerBit;
    private bool _holdingCamera;
    private Pose _holdPose;
    private SphereCollider _probe;            // 壁押し出し用のカメラ球

    private void OnDestroy() {
        if (_probe != null) Destroy(_probe.gameObject);
    }

    private void Reset() {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other) {
        if (_triggered && oneShot) return;
        if (!other.CompareTag(playerTag)) return;
        _triggered = true;
        StartCoroutine(RunSequence());
    }

    // ======================================================================
    //  メインシーケンス
    // ======================================================================
    private IEnumerator RunSequence() {
        BuildTypeLookup();

        List<Transform> waves = ResolvePhaseTransforms();
        bool useHierarchy = waves.Count > 0;
        if (!useHierarchy && (phases == null || phases.Count == 0)) yield break;

        _player = FindObjectOfType<PlayerLifetimeScope>();
        _cam    = TPSCameraController.Instance;
        _camTf  = (_cam != null) ? _cam.transform : (Camera.main != null ? Camera.main.transform : null);
        _camComponent = (_camTf != null) ? _camTf.GetComponent<Camera>() : null;
        _brain  = FindBrain(_camTf);
        _playerLayerBit = LayerBit("Player");
        _weaponLayerBit = LayerBit("Weapon");

        if (useHierarchy) {
            foreach (Transform wave in waves) yield return RunWave(wave);
        } else {
            // 後方互換: 全フェーズを続けて召喚し、最後に AI 起動 (ゲーティング無し)
            TakeCamera();
            var legacySpawned = new List<GameObject>();
            foreach (PhaseDef p in phases) yield return RunLegacyPhase(p, legacySpawned);
            yield return ReleaseCamera();
            foreach (var e in legacySpawned) SetEnemyAI(e, true);
        }

        RestoreBrain();

        // 全ウェーブ殲滅完了 → クリア通知 (OutGate 出現などが購読)
        IsAllCleared = true;
        AllCleared?.Invoke();
    }

    // ======================================================================
    //  ウェーブ (階層駆動 + ゲーティング)
    // ======================================================================
    private IEnumerator RunWave(Transform wave) {
        List<Transform> points = new List<Transform>(wave.childCount);
        for (int i = 0; i < wave.childCount; i++) points.Add(wave.GetChild(i));
        if (points.Count == 0) yield break;

        // 種別を先に一度だけ解決 (未定義ログの二重化を防ぐ)
        var types = new List<EnemyType>(points.Count);
        foreach (Transform p in points) types.Add(ResolveType(p.name));

        // 波の中心
        Vector3 centroid = Vector3.zero;
        foreach (Transform p in points) centroid += p.position;
        centroid /= points.Count;

        // 1. カメラを奪ってアリーナへ寄せる
        TakeCamera();
        if (_camTf != null) {
            ApplyPose(MakeArenaPose(centroid, cameraWideExtra));   // 引きへカット
            Pose near = MakeArenaPose(centroid, Vector3.zero);
            yield return MoveCamera(near, cameraMoveTime);
            BeginHoldCamera(near);                                 // カスケード中は据え置き + FOV維持
        }

        // 2A. まず全シールがディゾルブで地面に出現する (波状に)
        var quads = new List<GameObject>(points.Count);
        var mats  = new List<Material>(points.Count);
        for (int i = 0; i < points.Count; i++) {
            Texture tex = (types[i] != null && types[i].stickerTexture != null)
                        ? types[i].stickerTexture : defaultStickerTexture;
            GameObject quad = CreateStickerQuad(points[i].position, tex, out Material mat);
            quads.Add(quad); mats.Add(mat);
            StartCoroutine(DissolveIn(mat, dissolveInTime));
            if (appearStagger > 0f) yield return Wait(appearStagger);
        }
        yield return Wait(dissolveInTime + allAppearHold);         // 出現しきる + 地面のシールを少し見せる

        // 2B. 順番にぱらぱら剥がれ、剥がれた瞬間にエネミーを召喚 (白兎同様)
        var waveEnemies = new List<GameObject>();
        var waveHealths = new List<EnemyHealth>();
        _pointRoutinesRunning = 0;
        for (int i = 0; i < points.Count; i++) {
            StartCoroutine(PeelAndSummon(quads[i], mats[i], points[i].position, types[i], waveEnemies, waveHealths));
            _pointRoutinesRunning++;
            if (peelStagger > 0f) yield return Wait(peelStagger);
        }
        while (_pointRoutinesRunning > 0) yield return null;       // 全部剥がれ+召喚しきるまで
        yield return Wait(admireHold);

        // 3. カメラ/操作を返し、このウェーブの敵 AI を起動
        EndHoldCamera();
        yield return ReleaseCamera();
        foreach (var e in waveEnemies) SetEnemyAI(e, true);

        // 4. ウェーブ全滅まで待つ (ゲーティング)
        if (waveGated) yield return WaitUntilWaveCleared(waveEnemies, waveHealths);
    }

    /// <summary>1 枚分: シールをペラッと剥がし、剥がれた瞬間にスモーク+エネミー召喚 (白兎同様)。</summary>
    private IEnumerator PeelAndSummon(GameObject quad, Material mat, Vector3 pos, EnemyType type,
                                      List<GameObject> waveEnemies, List<EnemyHealth> waveHealths) {
        yield return Peel(mat, peelTime);                          // ペラッと剥がれ上がる
        if (quad != null) StartCoroutine(VanishAndHide(quad.transform, mat, vanishTime));

        // シールが剥がれた瞬間 = 召喚 (スモーク + ポップ)
        SpawnSummonVfx(pos);
        GameObject enemy = SpawnEnemy(type, pos);
        if (enemy != null) {
            waveEnemies.Add(enemy);
            waveHealths.Add(enemy.GetComponentInChildren<EnemyHealth>(true));
        }

        _pointRoutinesRunning--;
    }

    // 全滅 = 全ての敵が破棄済み or IsDead
    private IEnumerator WaitUntilWaveCleared(List<GameObject> enemies, List<EnemyHealth> healths) {
        yield return Wait(waveClearGrace);
        while (true) {
            bool anyAlive = false;
            for (int i = 0; i < enemies.Count; i++) {
                if (enemies[i] == null) continue;                  // 破棄 = 撃破
                EnemyHealth h = healths[i];
                if (h != null && h.IsDead) continue;
                anyAlive = true;
                break;
            }
            if (!anyAlive) yield break;
            yield return null;
        }
    }

    // ======================================================================
    //  ウェーブ (旧式: Transform + 単一シール/敵)
    // ======================================================================
    private IEnumerator RunLegacyPhase(PhaseDef phase, List<GameObject> spawned) {
        if (phase == null) yield break;
        Vector3 focus = (phase.spawnPoint != null) ? phase.spawnPoint.position : transform.position;

        if (_camTf != null) {
            ApplyPose(MakeArenaPose(focus, cameraWideExtra));
            yield return MoveCamera(MakeArenaPose(focus, Vector3.zero), cameraMoveTime);
        }

        SpawnSummonVfx(focus);
        GameObject quad = CreateStickerQuad(focus, phase.stickerTexture, out Material mat);
        yield return DissolveIn(mat, dissolveInTime);
        yield return Wait(stickerSettle);
        yield return Peel(mat, peelTime);
        if (quad != null) StartCoroutine(VanishAndHide(quad.transform, mat, vanishTime));

        if (phase.enemyPrefab != null) {
            GameObject e = InstantiateEnemyAt(phase.enemyPrefab, focus);
            if (e != null) { spawned.Add(e); SetEnemyAI(e, false); PopIn(e); }
        }
        yield return Wait(admireHold);
    }

    // ======================================================================
    //  カメラの奪取 / 返却
    // ======================================================================
    private void TakeCamera() {
        if (_brain != null) _brain.enabled = false;           // Cinemachine の上書きを止める
        if (_cam != null) _cam.enabled = false;               // 後追いカメラを停止して直接駆動
        ForceGameFov();                                       // Brain が残した FOV(90 等)を標準へ
        if (_player != null) _player.SetUIMode(true);         // 入力・移動・攻撃を凍結
        Cursor.lockState = CursorLockMode.Locked;
    }

    private IEnumerator ReleaseCamera() {
        // 現在のカメラ姿勢から「通常追従カメラの定位置」へなめらかに戻す。
        if (_camTf != null && _player != null) {
            Pose finalPose;
            if (_cam != null) {
                _cam.GetHomeFollowPose(_player.transform.eulerAngles.y, out var hp, out var hr);
                finalPose = new Pose { pos = hp, rot = hr };
            } else {
                Vector3 back = -_player.transform.forward;
                Vector3 pos  = _player.transform.position + back * 6f + Vector3.up * 3f;
                finalPose = new Pose {
                    pos = pos,
                    rot = Quaternion.LookRotation((_player.transform.position + Vector3.up * 1.2f) - pos, Vector3.up)
                };
            }
            yield return MoveCamera(finalPose, cameraReturnTime);
        }

        if (_cam != null) {
            _cam.ResyncFromCurrentView();
            _cam.enabled = true;
        }
        if (_player != null) _player.SetUIMode(false);        // 操作を返す
    }

    private void ForceGameFov() {
        if (_camComponent != null && _cam != null) _camComponent.fieldOfView = _cam.BaseFov;
    }

    // ======================================================================
    //  カメラ姿勢 (アリーナ寄り + 壁貫通防止)
    // ======================================================================
    private Pose MakeArenaPose(Vector3 centroid, Vector3 extraOffset) {
        Vector3 lookAt  = centroid + Vector3.up * lookAtHeight;
        Vector3 desired = centroid + cameraOffset + extraOffset;
        desired = ResolveCameraCollision(lookAt, desired);
        Vector3 dir = lookAt - desired;
        Quaternion rot = (dir.sqrMagnitude > 1e-4f) ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;
        return new Pose { pos = desired, rot = rot };
    }

    // 注視点→希望カメラ位置を SphereCast して壁で手前へ引き、さらに壁に埋まって
    // いたら押し出す (地面/壁を貫通しない)。
    private Vector3 ResolveCameraCollision(Vector3 pivot, Vector3 desired) {
        int mask = cameraCollisionMask.value & ~_playerLayerBit & ~_weaponLayerBit;
        Vector3 pos = desired;

        // (1) 注視点→カメラ間の壁で手前へ引く (壁越しに見えないように)
        Vector3 delta = desired - pivot;
        float dist = delta.magnitude;
        if (dist > 1e-3f) {
            Vector3 dir = delta / dist;
            RaycastHit[] hits = Physics.SphereCastAll(pivot, cameraCollisionRadius, dir, dist,
                                                      mask, QueryTriggerInteraction.Ignore);
            float nearest = dist;
            foreach (var h in hits) {
                if (h.collider == null || h.distance <= 0f || IsIgnoredCollider(h.collider)) continue;
                if (h.distance < nearest) nearest = h.distance;
            }
            pos = pivot + dir * Mathf.Max(minCameraDistance, nearest - cameraCollisionBuffer);
        }

        // (2) それでも壁に埋まっていたら外へ押し出す (横/後ろからの貫通対策)
        return DepenetrateCamera(pos, mask);
    }

    // カメラ球が壁コライダーに埋まっていたら ComputePenetration で外へ押し出す。
    private Vector3 DepenetrateCamera(Vector3 pos, int mask) {
        SphereCollider probe = GetProbe();
        float r = probe.radius;
        for (int iter = 0; iter < 5; iter++) {
            Collider[] overlaps = Physics.OverlapSphere(pos, r, mask, QueryTriggerInteraction.Ignore);
            bool moved = false;
            foreach (var col in overlaps) {
                if (col == null || col == probe || IsIgnoredCollider(col)) continue;
                if (Physics.ComputePenetration(probe, pos, Quaternion.identity,
                                               col, col.transform.position, col.transform.rotation,
                                               out Vector3 pushDir, out float pushDist)) {
                    pos += pushDir * (pushDist + 0.01f);
                    moved = true;
                }
            }
            if (!moved) break;
        }
        return pos;
    }

    private SphereCollider GetProbe() {
        if (_probe == null) {
            // ComputePenetration は Play 中、非アクティブ collider だと効かないことがある。
            // アクティブ + トリガー + IgnoreRaycast + 遠方配置で、物理にもレイにも干渉させずに使う。
            var g = new GameObject("SecondPhase_CamProbe");
            g.hideFlags = HideFlags.HideAndDontSave;
            int ignoreRay = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRay >= 0) g.layer = ignoreRay;
            g.transform.position = new Vector3(0f, -100000f, 0f);
            _probe = g.AddComponent<SphereCollider>();
            _probe.isTrigger = true;
        }
        _probe.radius = cameraCollisionRadius + cameraCollisionBuffer;   // 余白ぶん外に保つ
        return _probe;
    }

    private bool IsIgnoredCollider(Collider c) {
        if (c == null) return true;
        if (c.GetComponentInParent<EnemyBase>() != null) return true;            // 敵は無視
        if (c.GetComponentInParent<PlayerLifetimeScope>() != null) return true;  // 自機は無視
        // tag 許可制: Wall / Ground だけ衝突。Wall_Clear 等はカメラを通す。
        if (cameraCollisionTags != null && cameraCollisionTags.Length > 0) {
            foreach (var tag in cameraCollisionTags)
                if (!string.IsNullOrEmpty(tag) && c.CompareTag(tag)) return false;
            return true;
        }
        return false;
    }

    private void ApplyPose(Pose p) {
        if (_camTf == null) return;
        _camTf.SetPositionAndRotation(p.pos, p.rot);
    }

    private IEnumerator MoveCamera(Pose target, float moveTime) {
        if (_camTf == null) yield break;
        Vector3 p0 = _camTf.position;
        Quaternion r0 = _camTf.rotation;
        float dur = Mathf.Max(0.01f, moveTime);
        float t = 0f;
        while (t < dur) {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dur);
            float e = x * x * (3f - 2f * x);                   // smoothstep
            _camTf.SetPositionAndRotation(
                Vector3.LerpUnclamped(p0, target.pos, e),
                Quaternion.SlerpUnclamped(r0, target.rot, e));
            ForceGameFov();
            yield return null;
        }
        _camTf.SetPositionAndRotation(target.pos, target.rot);
        ForceGameFov();
    }

    // カスケード中、カメラを据え置き + FOV を維持し続ける (Brain 等の上書き対策)。
    private void BeginHoldCamera(Pose pose) {
        _holdPose = pose;
        if (_holdingCamera) return;
        _holdingCamera = true;
        StartCoroutine(HoldCameraLoop());
    }
    private void EndHoldCamera() => _holdingCamera = false;

    private IEnumerator HoldCameraLoop() {
        while (_holdingCamera) {
            if (_camTf != null) _camTf.SetPositionAndRotation(_holdPose.pos, _holdPose.rot);
            ForceGameFov();
            yield return null;
        }
    }

    // ======================================================================
    //  シール (Sticker/World: _Dissolve 1.2→-0.2=出現 / _Peel 0→1=剥がれ)
    // ======================================================================
    private GameObject CreateStickerQuad(Vector3 pos, Texture tex, out Material mat) {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SecondPhase_Sticker";
        var col = quad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        quad.GetComponent<MeshFilter>().sharedMesh = StickerQuadMesh.Grid;   // カール用の分割グリッド

        Transform tf = quad.transform;
        // 実際の地面へスナップ (スポーン点が床から浮いていてもシールは地面に貼る)
        Vector3 groundPos = pos;
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 12f,
                            cameraCollisionMask, QueryTriggerInteraction.Ignore))
            groundPos.y = gh.point.y;
        tf.position   = groundPos + Vector3.up * stickerLift;
        tf.rotation   = Quaternion.Euler(90f, 0f, 0f);         // 地面に寝かせる
        tf.localScale = new Vector3(stickerSize, stickerSize, 1f);

        var mr = quad.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Shader sh = Shader.Find("Sticker/World");
        mat = (stickerMaterialTemplate != null) ? new Material(stickerMaterialTemplate)
            : (sh != null) ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
        if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        StickerPeelFX.PrepareMaterial(mat);                    // _Peel=0 + _CurlRadius(=0.24) 白兎と同じ立体カール
        if (mat.HasProperty("_DirX")) mat.SetFloat("_DirX", peelDirX);
        if (mat.HasProperty("_DirY")) mat.SetFloat("_DirY", peelDirY);
        if (mat.HasProperty("_FlipY")) mat.SetFloat("_FlipY", peelFlipY ? 1f : 0f);
        if (mat.HasProperty("_Dissolve")) mat.SetFloat("_Dissolve", 1.2f); // 完全に溶けた状態から出現
        mr.sharedMaterial = mat;
        return quad;
    }

    private IEnumerator DissolveIn(Material mat, float duration) {
        if (mat == null || !mat.HasProperty("_Dissolve")) { yield return Wait(duration); yield break; }
        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < dur) {
            t += Time.deltaTime;
            mat.SetFloat("_Dissolve", Mathf.Lerp(1.2f, -0.2f, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        mat.SetFloat("_Dissolve", -0.2f);
    }

    private IEnumerator Peel(Material mat, float duration) {
        if (mat == null || !mat.HasProperty("_Peel")) { yield return Wait(duration); yield break; }
        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < dur) {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dur);
            mat.SetFloat("_Peel", x * Mathf.Sqrt(x));           // イーズイン x^1.5
            yield return null;
        }
        mat.SetFloat("_Peel", 1f);
    }

    private IEnumerator VanishAndHide(Transform quadTf, Material mat, float duration) {
        yield return StickerPeelFX.FlyOffAndDissolve(quadTf, mat, duration);
        if (quadTf != null) quadTf.gameObject.SetActive(false);
    }

    // ======================================================================
    //  召喚
    // ======================================================================
    private void SpawnSummonVfx(Vector3 pos) {
        GameObject prefab = summonVfx != null ? summonVfx : GetDefaultVfx();
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, pos, Quaternion.identity);
        if (!Mathf.Approximately(summonVfxScale, 1f)) fx.transform.localScale *= summonVfxScale;
        Destroy(fx, Mathf.Max(0.1f, summonVfxLifetime));
    }

    private GameObject GetDefaultVfx() {
        if (_defaultVfx == null) _defaultVfx = Resources.Load<GameObject>("VFX/WarpHole_Portal");
        return _defaultVfx;
    }

    private GameObject SpawnEnemy(EnemyType type, Vector3 pos) {
        if (type == null || type.prefab == null) return null;
        GameObject enemy = InstantiateEnemyAt(type.prefab, pos);
        if (enemy == null) return null;
        SetEnemyAI(enemy, false);                               // 演出中は AI 停止
        PopIn(enemy);
        return enemy;
    }

    private GameObject InstantiateEnemyAt(GameObject prefab, Vector3 pos) {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;

        Quaternion rot = Quaternion.identity;
        if (_player != null) {                                  // プレイヤーの方を向いて出す
            Vector3 to = _player.transform.position - pos; to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(to, Vector3.up);
        }
        return Instantiate(prefab, pos, rot);
    }

    private void PopIn(GameObject enemy) {
        Vector3 baseScale = enemy.transform.localScale;
        enemy.transform.localScale = Vector3.zero;
        StartCoroutine(PopScale(enemy.transform, baseScale, enemyPopTime));
    }

    private IEnumerator PopScale(Transform tf, Vector3 target, float duration) {
        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < dur && tf != null) {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dur);
            const float s = 1.70158f;                           // easeOutBack
            float e = 1f + (s + 1f) * Mathf.Pow(x - 1f, 3f) + s * Mathf.Pow(x - 1f, 2f);
            tf.localScale = target * e;
            yield return null;
        }
        if (tf != null) tf.localScale = target;
    }

    private void SetEnemyAI(GameObject go, bool on) {
        if (go == null) return;
        var nav = go.GetComponentInChildren<NavMeshAgent>(true);
        if (nav != null) nav.enabled = on;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb is EnemyBase) mb.enabled = on;
    }

    // ======================================================================
    //  種別テーブル / 階層解決 / 雑多
    // ======================================================================
    private void BuildTypeLookup() {
        _typeLookup = new Dictionary<string, EnemyType>();
        if (enemyTypes == null) return;
        foreach (var t in enemyTypes) {
            if (t == null || string.IsNullOrEmpty(t.key)) continue;
            _typeLookup[Normalize(t.key)] = t;
        }
    }

    private EnemyType ResolveType(string pointName) {
        string key = ParseEnemyKey(pointName);
        if (_typeLookup != null && _typeLookup.TryGetValue(Normalize(key), out var t)) return t;
        Debug.LogWarning($"[SecondPhaseSequence] 点 '{pointName}' の種別 '{key}' が enemyTypes に無い。召喚をスキップします。");
        return null;
    }

    private static string ParseEnemyKey(string name) {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        int idx = name.IndexOf("Enemy", System.StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return name.Substring(idx).Trim();
        int dash = name.LastIndexOf('-');
        if (dash >= 0 && dash + 1 < name.Length) return name.Substring(dash + 1).Trim();
        return name.Trim();
    }

    private static string Normalize(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace(" ", string.Empty).ToLowerInvariant();

    private List<Transform> ResolvePhaseTransforms() {
        Transform root = phaseSpawnRoot;
        if (root == null && !string.IsNullOrEmpty(phaseSpawnRootName)) {
            GameObject go = GameObject.Find(phaseSpawnRootName);
            if (go != null) root = go.transform;
        }
        var list = new List<Transform>();
        if (root == null) return list;
        for (int i = 0; i < root.childCount; i++) {
            Transform child = root.GetChild(i);
            if (child.gameObject.activeInHierarchy) list.Add(child);
        }
        return list;
    }

    private static int LayerBit(string layerName) {
        int l = LayerMask.NameToLayer(layerName);
        return l >= 0 ? (1 << l) : 0;
    }

    // Main Camera 上の CinemachineBrain (CM2/CM3 いずれも型名で拾う)。
    private static Behaviour FindBrain(Transform camTf) {
        if (camTf == null) return null;
        foreach (var c in camTf.GetComponents<Behaviour>())
            if (c != null && c.GetType().Name == "CinemachineBrain") return c;
        return null;
    }

    private void RestoreBrain() {
        if (_brain != null) _brain.enabled = true;
    }

    private IEnumerator Wait(float sec) {
        float t = 0f;
        while (t < sec) { t += Time.deltaTime; yield return null; }
    }
}
