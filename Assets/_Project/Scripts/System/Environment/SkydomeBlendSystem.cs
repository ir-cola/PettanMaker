using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 2つのスカイドームをディゾルブ・クロスフェードで切り替えるシステムの抽象ベース。
//
// クロスフェード／Scrub／エディタプレビューといった共通機構をすべて持ち、
// 「どんな模様で溶かすか（フィールド関数）」だけを派生で切り替える。
//   ・SkydomeBlendSystem_Noise … 地平線→天頂へ有機ノイズで溶ける
//   ・SkydomeBlendSystem_Tile  … 格子状のタイルが順に切り替わる
//
// 溶け際には二色の炎・白熱コア・火の粉・ハローを HDR 加算で重ねる（Bloom で豪華）。
// _Dissolve は MaterialPropertyBlock 経由で Renderer 単位に書き込むため、マテリアルは
// 共有したまま制御でき、編集中にマテリアルインスタンスが漏れない。
//
// ボス非依存。シーンごとに派生を1つ配置し、現行/切り替え先ドームを割り当てて、
// 各ボスのトリガー（B1_BossRoomTrigger 等）から Blend() を呼ぶ。
//
// Debug = true のとき、[ExecuteAlways] + EditorApplication.update により編集中でも
//   ・コンテキストメニューでアニメ切り替え
//   ・Scrub バーで手動で遷移を進め/戻し
// ができる（属性は派生側に付与）。
public abstract class SkydomeBlendSystem : MonoBehaviour
{
    [Header("Skydomes")]
    [Tooltip("開始時に表示されているスカイドーム。")]
    [SerializeField] GameObject currentSkydome;

    [Tooltip("切り替え先のスカイドーム。開始時は非アクティブでも可（自動でアクティブ化）。")]
    [SerializeField] GameObject targetSkydome;

    [Header("Blend")]
    [Tooltip("クロスフェードにかける秒数。")]
    [SerializeField, Min(0.01f)] float duration = 2.0f;

    [Tooltip("ディゾルブ進行カーブ（0→1）。")]
    [SerializeField] AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Transition Style")]
    [Tooltip("炎本体・ハローの主色（HDR）。ボスごとにテーマを変えられる。")]
    [SerializeField, ColorUsage(true, true)] Color edgeColor = new Color(1.6f, 0.35f, 1.2f, 1f);

    [Tooltip("白熱コア・火の粉の副色（HDR）。主色とのコントラストで二色の炎になる。")]
    [SerializeField, ColorUsage(true, true)] Color emberColor = new Color(2.4f, 1.6f, 0.6f, 1f);

    [Tooltip("燃焼エッジ帯の幅。大きいほど光る帯が太い。")]
    [SerializeField, Range(0.01f, 0.4f)] float edgeWidth = 0.08f;

    [Tooltip("切り替えに同期して湧かせる光の粒子バースト（任意）。色は炎エッジ/白熱コアに自動で合う。")]
    [SerializeField] SkydomeTransitionParticles transitionParticles;

    [Header("Debug")]
    [Tooltip("ON のとき、編集中でも EditorApplication.update 駆動でアニメプレビュー／Scrub 操作が有効になる。")]
    [SerializeField] bool debug = false;

    [Tooltip("Scrub モード：下のバーを手で動かして遷移を進め/戻しする（Debug ON 時）。")]
    [SerializeField] bool scrub = false;

    [Tooltip("Scrub の進行度。0 = Current、1 = Target。バーをドラッグして手動制御。")]
    [SerializeField, Range(0f, 1f)] float scrubProgress = 0f;

    static readonly int DissolveId   = Shader.PropertyToID("_Dissolve");
    static readonly int EdgeColorId  = Shader.PropertyToID("_SkydomeDissolveColor");
    static readonly int EmberColorId = Shader.PropertyToID("_SkydomeDissolveColor2");
    static readonly int EdgeWidthId  = Shader.PropertyToID("_SkydomeDissolveEdge");
    static readonly int ModeId       = Shader.PropertyToID("_SkydomeDissolveMode");

    // 退出側ドームを手前に描くための renderQueue（Background 帯 1000〜1999 に収める）。
    // 進入側は既定の Background(≈1000) のままなので触らない。
    const int QueueFront = 1100;

    // ─ 遷移ペアの状態（from=退出側 / to=進入側） ─
    GameObject _from;
    GameObject _to;
    Renderer[] _fromRenderers;
    Renderer[] _toRenderers;
    int[] _fromQueues;              // 退出側 renderQueue の復元用
    bool _pairReady;

    // ─ 時間アニメーション（コンテキストメニュー／ゲームからの Blend） ─
    bool _timed;
    float _elapsed;
    float _blendDuration;

    // ─ Scrub モード ─
    bool _scrubActive;

    MaterialPropertyBlock _mpb;

    // デバッグ往復の基準となる初期ドーム（Blend で currentSkydome が上書きされるため保持）
    GameObject _initialSkydome;

#if UNITY_EDITOR
    double _lastTickTime;
#endif

    /// <summary>現在表示中のスカイドーム。</summary>
    public GameObject CurrentSkydome => currentSkydome;

    /// <summary>時間アニメーション中かどうか。</summary>
    public bool IsBlending => _timed;

    /// <summary>切り替え先ドーム（派生のパターン計算用）。</summary>
    protected GameObject TargetSkydome => targetSkydome;

    /// <summary>切り替え先ドームの代表マテリアル（無ければ null）。</summary>
    protected Material TargetMaterial
    {
        get
        {
            if (targetSkydome == null) return null;
            var r = targetSkydome.GetComponentInChildren<Renderer>(true);
            return r != null ? r.sharedMaterial : null;
        }
    }

    void Awake()
    {
        if (_initialSkydome == null) _initialSkydome = currentSkydome;
    }

    void OnEnable()
    {
        if (_initialSkydome == null) _initialSkydome = currentSkydome;
#if UNITY_EDITOR
        EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        // 中断時に退出側マテリアルの renderQueue を戻し損ねないよう保険
        if (_pairReady) RestoreQueues(_fromRenderers, _fromQueues);
    }

    // Play 中の駆動（[ExecuteAlways] のため編集中も呼ばれるので isPlaying でガード）
    void Update()
    {
        if (!Application.isPlaying) return;
        Step(Time.deltaTime);
    }

#if UNITY_EDITOR
    // 編集中（停止時）の駆動。Debug=true のときのみ。
    void EditorTick()
    {
        if (Application.isPlaying) return;
        // 動かす対象があるとき（Debug スクラブ中／後片付けが必要なとき）だけ処理。
        // Debug を切った直後でも _scrubActive/_timed が残っていれば1回走らせて片付ける。
        if (!_scrubActive && !_timed && !(debug && scrub)) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTickTime);
        _lastTickTime = now;

        Step(dt);

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate(); // Game ビューも更新
    }
#endif

    // フレーム毎の共通ディスパッチ
    void Step(float dt)
    {
        bool scrubOn = debug && scrub;

        if (scrubOn)
        {
            ScrubStep();
            return;
        }

        // Scrub が OFF になった直後：最寄りの端で確定
        if (_scrubActive)
        {
            EndPair(scrubProgress);
            _scrubActive = false;
            return;
        }

        if (_timed) TimedStep(dt);
    }

    // ───────────── Public API ─────────────

    /// <summary>Inspector で設定した targetSkydome へクロスフェードする。</summary>
    public void Blend()
    {
        BlendTo(targetSkydome, duration);
    }

    /// <summary>切り替え先ドームへ既定秒数でクロスフェードする。</summary>
    public void BlendTo(GameObject next)
    {
        BlendTo(next, duration);
    }

    /// <summary>切り替え先ドームへ指定秒数でクロスフェードする。</summary>
    public void BlendTo(GameObject next, float blendDuration)
    {
        if (next == null)
        {
            Debug.LogWarning("[SkydomeBlendSystem] 切り替え先ドームが未設定です。", this);
            return;
        }
        if (next == currentSkydome)
        {
            return; // すでに表示中
        }

        // Scrub 中なら一旦確定してから
        if (_scrubActive) { EndPair(scrubProgress); _scrubActive = false; scrub = false; }

        ApplyTransitionStyle();
        SetupPair(currentSkydome, next);

        _elapsed = 0f;
        _blendDuration = Mathf.Max(0.0001f, blendDuration);
        _timed = true;
#if UNITY_EDITOR
        _lastTickTime = EditorApplication.timeSinceStartup;
#endif

        // 切り替えの立ち上がりに合わせて光の粒子バーストを発火（ゲーム中のみ）。
        // 色は炎エッジ(edgeColor)/白熱コア(emberColor)へ揃え、密度は _blendDuration の中央でピーク。
        if (Application.isPlaying && transitionParticles != null)
            transitionParticles.Play(_blendDuration, edgeColor, emberColor);

        // 編集中で Debug OFF のときは駆動源が無いので即座に完了させる
        if (!Application.isPlaying && !debug)
        {
            TimedStep(_blendDuration);
        }
    }

    // ───────────── 内部：時間アニメーション ─────────────

    void TimedStep(float dt)
    {
        _elapsed += dt;
        float t = Mathf.Clamp01(_elapsed / _blendDuration);
        ApplyProgress(t);

        if (t >= 1f)
        {
            EndPair(1f);
            _timed = false;
        }
    }

    // ───────────── 内部：Scrub ─────────────

    void ScrubStep()
    {
        if (targetSkydome == null) return;

        GameObject from = _initialSkydome != null ? _initialSkydome : currentSkydome;
        if (from == null || from == targetSkydome) return;

        _timed = false;
        _scrubActive = true;

        ApplyTransitionStyle();
        SetupPair(from, targetSkydome);
        ApplyProgress(scrubProgress);
    }

    // ───────────── 内部：ペア制御 ─────────────

    // 退出(from)/進入(to) のペアを準備する（同じペアなら何もしない）。
    void SetupPair(GameObject from, GameObject to)
    {
        if (_pairReady && _from == from && _to == to) return;

        // 既存ペアの renderQueue だけ戻しておく
        if (_pairReady) RestoreQueues(_fromRenderers, _fromQueues);

        _to = to;
        _toRenderers = GetRenderers(to);
        if (to != null) to.SetActive(true);
        SetDissolve(_toRenderers, 0f);

        if (from != null && from != to)
        {
            _from = from;
            _fromRenderers = GetRenderers(from);
            _fromQueues = GetQueues(_fromRenderers);
            SetQueue(_fromRenderers, QueueFront);
            SetDissolve(_fromRenderers, 0f);
        }
        else
        {
            _from = null;
            _fromRenderers = null;
            _fromQueues = null;
        }

        _pairReady = true;
    }

    // 進行度 t（0=from表示, 1=to表示）を適用。
    void ApplyProgress(float t)
    {
        float k = curve != null ? curve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);

        if (_fromRenderers != null)
        {
            SetDissolve(_fromRenderers, k);          // from を溶かしていく
        }
        else
        {
            SetDissolve(_toRenderers, 1f - k);       // from が無ければ to を出していく
        }
    }

    // ペアを終了して端（finalT<0.5=from / >=0.5=to）で確定する。
    void EndPair(float finalT)
    {
        if (!_pairReady) return;

        RestoreQueues(_fromRenderers, _fromQueues);
        SetDissolve(_fromRenderers, 0f);
        SetDissolve(_toRenderers, 0f);

        bool toWins = finalT >= 0.5f;
        if (toWins)
        {
            if (_from != null) _from.SetActive(false);
            if (_to != null) currentSkydome = _to;
        }
        else
        {
            if (_to != null && _to != _from) _to.SetActive(false);
            if (_from != null) currentSkydome = _from;
        }

        _pairReady = false;
        _from = _to = null;
        _fromRenderers = _toRenderers = null;
        _fromQueues = null;
    }

    // 燃焼エッジのテーマ（共通）＋パターン種別＋派生固有パラメータをグローバルへ反映する。
    protected virtual void ApplyTransitionStyle()
    {
        Shader.SetGlobalColor(EdgeColorId, edgeColor);
        Shader.SetGlobalColor(EmberColorId, emberColor);
        Shader.SetGlobalFloat(EdgeWidthId, edgeWidth);
        Shader.SetGlobalFloat(ModeId, DissolveMode);
        ApplyPatternGlobals();
    }

    /// <summary>0 = Noise, 1 = Tile。派生でパターン種別を返す。</summary>
    protected abstract float DissolveMode { get; }

    /// <summary>派生固有のパターンパラメータをグローバルへ設定する。</summary>
    protected abstract void ApplyPatternGlobals();

    // ───────────── Renderer ヘルパー ─────────────

    static Renderer[] GetRenderers(GameObject go)
    {
        return go != null ? go.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
    }

    // _Dissolve は MaterialPropertyBlock で書く（マテリアルを共有したまま／インスタンスを漏らさない）。
    void SetDissolve(Renderer[] renderers, float value)
    {
        if (renderers == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DissolveId, value);
            r.SetPropertyBlock(_mpb);
        }
    }

    static int[] GetQueues(Renderer[] renderers)
    {
        var queues = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i] != null ? renderers[i].sharedMaterial : null;
            queues[i] = m != null ? m.renderQueue : -1;
        }
        return queues;
    }

    static void SetQueue(Renderer[] renderers, int queue)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r != null && r.sharedMaterial != null) r.sharedMaterial.renderQueue = queue;
        }
    }

    static void RestoreQueues(Renderer[] renderers, int[] queues)
    {
        if (renderers == null || queues == null) return;
        for (int i = 0; i < renderers.Length && i < queues.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sharedMaterial != null)
            {
                renderers[i].sharedMaterial.renderQueue = queues[i];
            }
        }
    }

    void Reset()
    {
        // 自身がスカイドームなら現行として自動設定
        if (currentSkydome == null && GetComponent<Renderer>() != null)
        {
            currentSkydome = gameObject;
        }
    }

    // ─────────────────────────────────────────────
    // Debug（Inspector でコンポーネントを右クリック → コンテキストメニュー）
    //   Play 中          … 実際のディゾルブ・クロスフェードで確認
    //   編集中 + Debug ON … EditorApplication.update 駆動でアニメプレビュー
    //   編集中 + Debug OFF … 即時に切り替え
    // ※ 手でバー操作したいときは Debug ON + Scrub ON にして Scrub Progress をドラッグ。
    // ─────────────────────────────────────────────

    [ContextMenu("Debug/▶ Blend To Target")]
    void DebugBlendToTarget()
    {
        BlendTo(targetSkydome, duration);
    }

    [ContextMenu("Debug/◀ Blend To Current")]
    void DebugBlendToCurrent()
    {
        BlendTo(_initialSkydome != null ? _initialSkydome : currentSkydome, duration);
    }

    [ContextMenu("Debug/⇄ Toggle")]
    void DebugToggle()
    {
        GameObject initial = _initialSkydome != null ? _initialSkydome : currentSkydome;
        GameObject next = (currentSkydome == targetSkydome) ? initial : targetSkydome;
        BlendTo(next, duration);
    }

    [ContextMenu("Debug/⟲ Reset (stop and clear)")]
    void DebugReset()
    {
        scrub = false;
        _scrubActive = false;
        _timed = false;

        // 進行中なら片付ける（初期ドームで確定）
        GameObject initial = _initialSkydome != null ? _initialSkydome : currentSkydome;
        if (_pairReady)
        {
            EndPair(_from == initial ? 0f : 1f);
        }

        // 初期ドームだけを表示
        if (currentSkydome != null)
        {
            currentSkydome.SetActive(currentSkydome == initial);
            SetDissolve(GetRenderers(currentSkydome), 0f);
        }
        if (targetSkydome != null)
        {
            targetSkydome.SetActive(targetSkydome == initial);
            SetDissolve(GetRenderers(targetSkydome), 0f);
        }
        currentSkydome = initial;
    }
}
