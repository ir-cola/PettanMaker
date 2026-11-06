using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ボスのエフェクト全般を一元管理する。
/// VFX コンポーネント・マテリアル差し替え・スケールパルスを担う。
/// アクション側は B1_Boss のファサードを経由して呼ぶ。
/// </summary>
public class B1_EffectManager : MonoBehaviour
{
    [Header("エフェクト設定")]
    [SerializeField] private B1_EffectSettings effectSettings;

    [Header("透明化マテリアル（突進 SetInvisible 用）")]
    [Tooltip("突進(InvisibleCharge)の透明化に使うマテリアル。登場クロークが未設定のときはこれもフォールバックに使う。")]
    [SerializeField] private Material invisibleMaterial;

    [Header("怒りモード（激昂）の見た目")]
    [Tooltip("激昂（Phase2）中に見た目を変更するレンダラー。空ならボス配下の全レンダラーが対象。")]
    [SerializeField] private Renderer[] enrageTintRenderers;
    [Tooltip("指定すると、激昂中は対象レンダラーのマテリアルをこれに差し替える。未指定なら従来どおり _BaseColor を赤く染める。")]
    [SerializeField] private Material enrageOverrideMaterial;
    [Tooltip("激昂中に _BaseColor へ乗算する色。白(1,1,1)で変化なし。赤系にすると体色が怒りの赤に染まる。")]
    [SerializeField] private Color enrageTintColor = new Color(1f, 0.3f, 0.3f, 1f);
    [Tooltip("赤色化のフェード時間（秒）。0 で即時に切り替わる。")]
    [SerializeField] private float enrageTintFadeDuration = 0.8f;
    [Tooltip("激昂中にボスへ重ねるループオーラ VFX（B1_EnrageAuraVFX）。null ならオーラなし（赤色化のみ）。")]
    [SerializeField] private GameObject enrageAuraVFXPrefab;
    [Tooltip("オーラの発生位置（足元推奨）。指定した Transform の子として追従する。null ならボス本体基準。")]
    [SerializeField] private Transform enrageAuraAttachPoint;
    [Tooltip("炎・火の粉・煙を法線方向へ吹き出させる体表メッシュ。null ならボス配下の SkinnedMeshRenderer を自動検出。")]
    [SerializeField] private SkinnedMeshRenderer enrageAuraSkinnedMesh;
    [Tooltip("オーラ解除時、放出停止後に残粒子の消滅を待って破棄するまでの秒数。")]
    [SerializeField] private float enrageAuraLinger = 2f;

    // 登場ディゾルブ/煙の設定は B1_CameraDirector の Entrance ScriptableObject(RevealSettings) に集約。
    // PlayEntranceReveal(r) で受け取り、下記 transient に確定してからゴースト/煙を駆動する。
    private Material revealCloakMaterial;
    private float    revealNoiseScale = 3f;
    private int      revealGhostQueueOffset = 50;

    [Header("残像トレイル")]
    [SerializeField] private SkinnedMeshTrailRenderer trailRenderer;

    [Header("激昂中の目Trail")]
    [Tooltip("ON で激昂（Phase2）中ずっと、enrageTintRenderers に指定した目から通常の TrailRenderer を出す。")]
    [SerializeField] private bool enrageTrailEnabled = true;
    [Tooltip("左右の目Trailの始点。enrageTintRenderers と同じ順番で指定する。空の要素だけ目メッシュ中心へフォールバックする。")]
    [SerializeField] private Transform[] enrageEyeTrailPoints;

    [Header("オーサリング VFX プレハブ（Boss01/VFXPrefab）")]
    [Tooltip("ひっかき攻撃（VFX_Cat_Slash）。各振りごとに spawn する。null なら何もしない。")]
    [SerializeField] private GameObject slashVFXPrefab;
    [Tooltip("突進タメ時に目の前へ呼び出す（VFX_Cat_Dash_Star）。タメ中ループ。")]
    [SerializeField] private GameObject dashStarVFXPrefab;
    [Tooltip("突進時の土煙（VFX_Cat_Dash_Smoke）。突進中ループ。")]
    [SerializeField] private GameObject dashSmokeVFXPrefab;
    [Tooltip("しっぽ叩きつけの着地爆発（VFX_Cat_Explosion）。着地時 spawn。")]
    [SerializeField] private GameObject explosionVFXPrefab;
    [Tooltip("ダウンの Down_Idle（着地して殴れる時間）中にボスへ重ねるループ VFX（VFX_Enemy04_Down など）。")]
    [SerializeField] private GameObject downVFXPrefab;

    [Header("VFX アタッチ位置（null ならボス本体基準）")]
    [Tooltip("ひっかき VFX の発生位置・向き。")]
    [SerializeField] private Transform slashSpawnPoint;
    [Tooltip("突進タメ VFX を呼び出す位置（ボスの目の前）。複数登録すると各位置に同時生成（例：左右の目の前）。空ならボス本体に1つ。")]
    [SerializeField] private Transform[] dashStarSpawnPoints;
    [Tooltip("突進土煙 VFX の発生位置（足元など）。")]
    [SerializeField] private Transform dashSmokeSpawnPoint;
    [Tooltip("ダウン VFX の発生位置・向き。指定した Target Point の子として追従する。null ならボス本体基準。")]
    [SerializeField] private Transform downVFXTargetPoint;
    [Tooltip("突進発射の3D衝撃波を出す位置。null なら B1_EffectSettings の positionOffset（ボス基準）を使う。")]
    [SerializeField] private Transform chargeLaunchShockwavePoint;
    [Tooltip("テール着地の3D衝撃波を出す位置。null なら B1_EffectSettings の positionOffset（ボス基準）を使う。")]
    [FormerlySerializedAs("tailJumpShockwavePoint")]
    [SerializeField] private Transform tailLandShockwavePoint;
    [Tooltip("ひっかき3段目の3D衝撃波を出す位置。null なら B1_EffectSettings の positionOffset（ボス基準）を使う。")]
    [SerializeField] private Transform scratchFinisherShockwavePoint;

    [Header("VFX スケール")]
    [Tooltip("ひっかき VFX の大きさ倍率。全 PS が scalingMode=Hierarchy なのでルートスケールで一括拡大できる。")]
    [SerializeField] private float slashVFXScale = 1.5f;

    [Header("VFX 自動破棄時間（秒）")]
    [SerializeField] private float slashLifetime     = 2f;
    [SerializeField] private float explosionLifetime = 3f;
    [Tooltip("突進直前に出す Star の表示時間。")]
    [SerializeField] private float dashStarLifetime  = 1.5f;
    [Tooltip("土煙 VFX を Stop した後、残煙の消滅待ちを打ち切って強制破棄するまでの上限秒数。")]
    [SerializeField] private float dashSmokeMaxLinger = 8f;

    // Auto-resolve in Initialize()
    private B1_ChargeVFX       chargeVFX;
    private B1_ChargeVFX       tailChargeVFX;
    private B1_LandingImpactVFX landingImpactVFX;
    private B1_EyeRushVFX[]    eyeRushVFXs;
    private B1_RushWindVFX     rushWindVFX;
    private B1_EntranceSmokeVFX entranceSmoke;
    private Renderer[]         modelRenderers;

    // 生成済みループ VFX インスタンス
    private GameObject dashSmokeInstance;
    private GameObject downVFXInstance;

    /// <summary>Down VFX に残り得るパーティクルの最長寿命（実時間・秒）。</summary>
    public float DownVFXMaxParticleLifetime
    {
        get
        {
            GameObject source = downVFXInstance != null ? downVFXInstance : downVFXPrefab;
            if (source == null) return 0f;

            float maxLifetime = 0f;
            foreach (var ps in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                float simulationSpeed = Mathf.Max(0.0001f, main.simulationSpeed);
                maxLifetime = Mathf.Max(maxLifetime, main.startLifetime.constantMax / simulationSpeed);
            }
            return maxLifetime;
        }
    }

    private Material[][] originalMaterials;
    private Coroutine    scalePulseCoroutine;
    private Transform    aimPoseTarget;
    private Vector3      aimPoseBaseScale = Vector3.one;
    private bool         aimCrouchActive;

    // Down 中のモデルシェイク。ボスルートは動かさず、Animator の見た目だけに加算する。
    private Transform downShakeTarget;
    private Vector3   downShakeBaseLocalPosition;
    private Vector3   downShakeOffset;
    private float     downShakeAmplitude;
    private float     downShakeFrequency;
    private float     downShakeNextSampleTime;
    private bool      downShakeActive;

    // Down モーション中のモデルマテリアル白赤点滅（B1_Toon の MPB を駆動）。
    private MaterialPropertyBlock downFlashMpb;
    private Color downFlashWhite;
    private Color downFlashRed;
    private float downFlashAmount;
    private float downFlashInterval;
    private float downFlashTotalDuration;
    private float downFlashStartTime;
    private bool  downMaterialFlashActive;

    // クリティカルヒット点滅（Down 点滅と同じ _FlashColor/_FlashAmount を使うが状態は独立）
    private Color critBlinkColor;
    private float critBlinkAmount;
    private float critBlinkInterval;
    private float critBlinkDuration;
    private float critBlinkStartTime;
    private bool  critBlinkActive;

    // 登場ディゾルブ用ゴースト（InvisibleMaterial を着せた実メッシュの複製オーバーレイ）
    private readonly System.Collections.Generic.List<Renderer> ghostRenderers = new();
    private Material  ghostMaterialInstance;
    private Coroutine appearDissolveCoroutine;
    private Coroutine rushMaterialBlendCoroutine;
    private MaterialPropertyBlock realMpb;   // 実ボスの _Dissolve を相補駆動する
    private static readonly int DissolveId       = Shader.PropertyToID("_Dissolve");
    private static readonly int DissolveInvertId = Shader.PropertyToID("_DissolveInvert");
    private static readonly int DissolveScaleId  = Shader.PropertyToID("_DissolveScale");
    private static readonly int FlashColorId     = Shader.PropertyToID("_FlashColor");
    private static readonly int FlashAmountId    = Shader.PropertyToID("_FlashAmount");
    // スケールパルスの基準スケール（ボス本来の localScale）。1.0 決め打ちにしない。
    private Vector3      baseScale = Vector3.one;

    public B1_ChargeVFX ChargeVFX     => chargeVFX;
    public B1_ChargeVFX TailChargeVFX => tailChargeVFX;

    public void Initialize()
    {
        baseScale = transform.localScale;
        aimPoseTarget = GetComponentInChildren<Animator>(true)?.transform ?? transform;
        aimPoseBaseScale = aimPoseTarget.localScale;

        if (chargeVFX == null)
            chargeVFX = GetComponentInChildren<B1_ChargeVFX>(true)
                     ?? gameObject.AddComponent<B1_ChargeVFX>();

        if (tailChargeVFX == null)
        {
            var go = transform.Find("TailChargeVFX")?.gameObject ?? new GameObject("TailChargeVFX");
            go.transform.SetParent(transform, false);
            tailChargeVFX = go.GetComponent<B1_ChargeVFX>() ?? go.AddComponent<B1_ChargeVFX>();
        }

        if (landingImpactVFX == null)
        {
            var go = transform.Find("LandingImpactVFX")?.gameObject ?? new GameObject("LandingImpactVFX");
            go.transform.SetParent(transform, false);
            landingImpactVFX = go.GetComponent<B1_LandingImpactVFX>() ?? go.AddComponent<B1_LandingImpactVFX>();
        }

        EnsureEyeTrailVFXs();
        if (rushWindVFX == null)
            rushWindVFX = GetComponentInChildren<B1_RushWindVFX>(true)
                       ?? gameObject.AddComponent<B1_RushWindVFX>();

        if (effectSettings != null)
        {
            chargeVFX.Configure(effectSettings.Charge);
            tailChargeVFX.Configure(effectSettings.TailCharge);
            landingImpactVFX.Configure(effectSettings.LandingImpact);
            rushWindVFX.Configure(effectSettings.RushWind);
            if (eyeRushVFXs != null)
                foreach (var v in eyeRushVFXs) v?.Configure(effectSettings.EyeRush);
        }

        if (trailRenderer == null)
            trailRenderer = GetComponentInChildren<SkinnedMeshTrailRenderer>(true);

        EnsureEntranceSmoke();

        EnsureModelRenderers();
    }

    /// <summary>
    /// Down 中、Animator のモデルだけを震わせる。ルート・Collider・カメラには影響しない。
    /// </summary>
    public void StartDownShake(float amplitude, float frequency)
    {
        StopDownShake();
        if (amplitude <= 0f) return;

        downShakeTarget = GetComponentInChildren<Animator>(true)?.transform ?? transform;
        downShakeBaseLocalPosition = downShakeTarget.localPosition;
        downShakeAmplitude = Mathf.Max(0f, amplitude);
        downShakeFrequency = Mathf.Max(1f, frequency);
        downShakeNextSampleTime = 0f;
        downShakeActive = true;
    }

    public void StopDownShake()
    {
        if (downShakeTarget != null)
            downShakeTarget.localPosition = downShakeBaseLocalPosition;

        downShakeOffset = Vector3.zero;
        downShakeTarget = null;
        downShakeActive = false;
    }

    public void StartDownMaterialFlash(float interval, float totalDuration, float amount, Color white, Color red)
    {
        StopDownMaterialFlash();
        if (interval <= 0f || totalDuration <= 0f || amount <= 0f) return;

        EnsureModelRenderers();
        downFlashInterval = Mathf.Max(0.001f, interval);
        downFlashTotalDuration = Mathf.Max(0f, totalDuration);
        downFlashAmount = Mathf.Clamp01(amount);
        downFlashWhite = white;
        downFlashRed = red;
        downFlashStartTime = Time.unscaledTime;
        downMaterialFlashActive = true;
        ApplyDownMaterialFlash(downFlashWhite);
    }

    public void StopDownMaterialFlash()
    {
        if (modelRenderers != null)
        {
            foreach (var r in modelRenderers)
            {
                if (r == null) continue;
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!MaterialSupportsFlash(materials[i])) continue;
                    if (downFlashMpb == null) downFlashMpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(downFlashMpb, i);
                    downFlashMpb.SetFloat(FlashAmountId, 0f);
                    r.SetPropertyBlock(downFlashMpb, i);
                }
            }
        }

        downMaterialFlashActive = false;
    }

    // ---- クリティカルヒット点滅 (被弾クリティカル時にモデル全体を発光色で明滅させる) ----

    /// <summary>
    /// クリティカルヒットの瞬間、モデルのマテリアルを点滅させる（点灯 interval 秒 → 消灯 interval 秒 × count 回）。
    /// Down の白赤点滅と同じ _FlashColor/_FlashAmount を使うが状態は独立しており、
    /// 両方アクティブな場合はクリティカル点滅が優先して上書きする。unscaled 時間駆動なのでヒットストップ中も進む。
    /// </summary>
    public void PlayCriticalBlink(float interval, int count, float amount, Color color)
    {
        if (interval <= 0f || count < 1 || amount <= 0f) return;

        EnsureModelRenderers();
        critBlinkInterval  = Mathf.Max(0.001f, interval);
        critBlinkDuration  = critBlinkInterval * 2f * count;
        critBlinkAmount    = Mathf.Clamp01(amount);
        critBlinkColor     = color;
        critBlinkStartTime = Time.unscaledTime;
        critBlinkActive    = true;
        ApplyMaterialFlash(critBlinkColor, critBlinkAmount);
    }

    public void StopCriticalBlink()
    {
        if (!critBlinkActive) return;
        critBlinkActive = false;
        // Down 点滅が生きていれば LateUpdate で再適用されるので、ここでは消すだけでよい。
        ApplyMaterialFlash(critBlinkColor, 0f);
    }

    private void LateUpdate()
    {
        UpdateEnrageTint();

        if (downShakeActive && downShakeTarget != null)
        {
            if (Time.unscaledTime >= downShakeNextSampleTime)
            {
                Vector2 sample = Random.insideUnitCircle;
                downShakeOffset = new Vector3(sample.x, sample.y * 0.6f, 0f) * downShakeAmplitude;
                downShakeNextSampleTime = Time.unscaledTime + 1f / downShakeFrequency;
            }

            downShakeTarget.localPosition = downShakeBaseLocalPosition + downShakeOffset;
        }

        if (downMaterialFlashActive)
        {
            float elapsed = Time.unscaledTime - downFlashStartTime;
            if (elapsed >= downFlashTotalDuration)
            {
                StopDownMaterialFlash();
            }
            else
            {
                int step = Mathf.FloorToInt(elapsed / downFlashInterval);
                ApplyDownMaterialFlash((step & 1) == 0 ? downFlashWhite : downFlashRed);
            }
        }

        // クリティカル点滅は Down 点滅より後に適用して優先させる (同フレームで両方書いてもこちらが勝つ)
        if (critBlinkActive)
        {
            float critElapsed = Time.unscaledTime - critBlinkStartTime;
            if (critElapsed >= critBlinkDuration)
            {
                StopCriticalBlink();
            }
            else
            {
                int step = Mathf.FloorToInt(critElapsed / critBlinkInterval);
                bool lit = (step & 1) == 0;
                ApplyMaterialFlash(critBlinkColor, lit ? critBlinkAmount : 0f);
            }
        }
    }

    private void ApplyDownMaterialFlash(Color color) => ApplyMaterialFlash(color, downFlashAmount);

    private void ApplyMaterialFlash(Color color, float amount)
    {
        if (modelRenderers == null) return;
        if (downFlashMpb == null) downFlashMpb = new MaterialPropertyBlock();

        foreach (var r in modelRenderers)
        {
            if (r == null) continue;
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!MaterialSupportsFlash(materials[i])) continue;
                r.GetPropertyBlock(downFlashMpb, i);
                downFlashMpb.SetColor(FlashColorId, color);
                downFlashMpb.SetFloat(FlashAmountId, amount);
                r.SetPropertyBlock(downFlashMpb, i);
            }
        }
    }

    private static bool MaterialSupportsFlash(Material material) =>
        material != null
        && material.HasProperty(FlashColorId)
        && material.HasProperty(FlashAmountId);

    private void OnDisable()
    {
        StopDownShake();
        StopDownMaterialFlash();
        StopCriticalBlink();
        StopEnrageTrail();
        StopEnrageAura(immediate: true);
        RestoreEnrageMaterials();
        enrageActive = false;
        enrageTintWeight = 0f;
        ApplyEnrageTint(0f);
    }

    // --- 怒りモード（激昂）の赤色化 ---
    // Inspector で選んだレンダラー（未指定なら全モデルレンダラー）の _BaseColor を
    // MPB で「元色 × EnrageTintColor」へフェードさせる。マテリアル実体は書き換えない。

    private struct EnrageTintEntry
    {
        public Renderer renderer;
        public int      index;
        public Color    baseColor;
    }

    private struct EnrageMaterialEntry
    {
        public Renderer   renderer;
        public Material[] originalMaterials;
    }

    private readonly System.Collections.Generic.List<EnrageTintEntry> enrageTintEntries = new();
    private readonly System.Collections.Generic.List<EnrageMaterialEntry> enrageMaterialEntries = new();
    private MaterialPropertyBlock enrageMpb;
    private float enrageTintWeight;
    private bool  enrageActive;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>怒りモードの赤色化を開始/解除する（B1_Boss が IsEnraged の縁で呼ぶ）。フェードは LateUpdate が進める。</summary>
    public void SetEnraged(bool enraged)
    {
        if (enraged == enrageActive) return;
        enrageActive = enraged;
        // 目が赤い間（激昂中）はずっと残像トレイル（光の線）を出す。
        if (enraged) StartEnrageTrail();
        else         StopEnrageTrail();
        if (enraged)
        {
            SpawnEnrageAura();
            if (enrageOverrideMaterial != null)
            {
                enrageTintWeight = 0f;
                ApplyEnrageTint(0f);
                ApplyEnrageMaterials();
            }
            else if (enrageTintEntries.Count == 0)
            {
                BuildEnrageTintEntries();
            }
        }
        else
        {
            StopEnrageAura();
            RestoreEnrageMaterials();
        }
    }

    // 生成済みオーラのインスタンス。激昂中のみ存在する。
    private GameObject enrageAuraInstance;

    private void SpawnEnrageAura()
    {
        if (enrageAuraVFXPrefab == null || enrageAuraInstance != null) return;
        Transform parent = enrageAuraAttachPoint != null ? enrageAuraAttachPoint : transform;
        enrageAuraInstance = Instantiate(enrageAuraVFXPrefab, parent.position, parent.rotation, parent);
        BindEnrageAuraSkinnedMesh();
    }

    // プレハブにはシーンのレンダラー参照を焼けないため、体表放出レイヤー
    // （shapeType=SkinnedMeshRenderer で作られている）へスポーン時に割り当てる。
    private void BindEnrageAuraSkinnedMesh()
    {
        SkinnedMeshRenderer smr = enrageAuraSkinnedMesh != null
            ? enrageAuraSkinnedMesh
            : GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null)
        {
            Debug.LogWarning("[B1_EffectManager] SkinnedMeshRenderer が見つからないため、体表放出レイヤーは発生しません。", this);
            return;
        }

        foreach (var ps in enrageAuraInstance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var shape = ps.shape;
            if (shape.shapeType != ParticleSystemShapeType.SkinnedMeshRenderer) continue;
            shape.skinnedMeshRenderer = smr;
            // 割り当て前に走った prewarm は空シェイプで空振りしているので、正しい発生源でやり直す
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }
    }

    private void StopEnrageAura(bool immediate = false)
    {
        if (enrageAuraInstance == null) return;
        GameObject aura = enrageAuraInstance;
        enrageAuraInstance = null;

        if (immediate)
        {
            Destroy(aura);
            return;
        }

        // 放出だけ止めて残粒子は寿命で消し、linger 後に破棄する
        foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(aura, enrageAuraLinger);
    }

    private Renderer[] GetEnrageTargets()
    {
        if (enrageTintRenderers != null && enrageTintRenderers.Length > 0)
            return enrageTintRenderers;

        EnsureModelRenderers();
        return modelRenderers;
    }

    private void BuildEnrageTintEntries()
    {
        enrageTintEntries.Clear();
        foreach (var r in GetEnrageTargets())
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].HasProperty(BaseColorId)) continue;
                enrageTintEntries.Add(new EnrageTintEntry
                {
                    renderer  = r,
                    index     = i,
                    baseColor = mats[i].GetColor(BaseColorId),
                });
            }
        }
    }

    private void ApplyEnrageMaterials()
    {
        RestoreEnrageMaterials();
        bool hasExplicitTargets = enrageTintRenderers != null && enrageTintRenderers.Length > 0;
        var processed = new System.Collections.Generic.HashSet<Renderer>();

        foreach (var r in GetEnrageTargets())
        {
            if (r == null || !processed.Add(r)) continue;

            var original = r.sharedMaterials;
            var replacement = (Material[])original.Clone();
            bool replaced = false;
            for (int i = 0; i < replacement.Length; i++)
            {
                if (!hasExplicitTargets
                    && (original[i] == null || !original[i].HasProperty(BaseColorId))) continue;

                replacement[i] = enrageOverrideMaterial;
                replaced = true;
            }

            if (!replaced) continue;
            enrageMaterialEntries.Add(new EnrageMaterialEntry
            {
                renderer = r,
                originalMaterials = original,
            });
            r.sharedMaterials = replacement;
        }
    }

    private void RestoreEnrageMaterials()
    {
        foreach (var e in enrageMaterialEntries)
        {
            if (e.renderer != null)
                e.renderer.sharedMaterials = e.originalMaterials;
        }
        enrageMaterialEntries.Clear();
    }

    private void UpdateEnrageTint()
    {
        if (enrageMaterialEntries.Count > 0) return;

        float target = enrageActive ? 1f : 0f;
        if (Mathf.Approximately(enrageTintWeight, target)) return;

        enrageTintWeight = enrageTintFadeDuration <= 0f
            ? target
            : Mathf.MoveTowards(enrageTintWeight, target, Time.unscaledDeltaTime / enrageTintFadeDuration);
        ApplyEnrageTint(enrageTintWeight);
    }

    private void ApplyEnrageTint(float weight)
    {
        if (enrageTintEntries.Count == 0) return;
        if (enrageMpb == null) enrageMpb = new MaterialPropertyBlock();

        foreach (var e in enrageTintEntries)
        {
            if (e.renderer == null) continue;
            Color tinted = Color.Lerp(e.baseColor, e.baseColor * enrageTintColor, weight);
            e.renderer.GetPropertyBlock(enrageMpb, e.index);
            enrageMpb.SetColor(BaseColorId, tinted);
            e.renderer.SetPropertyBlock(enrageMpb, e.index);
        }
    }

    private void EnsureEntranceSmoke()
    {
        if (entranceSmoke == null)
            entranceSmoke = GetComponentInChildren<B1_EntranceSmokeVFX>(true)
                         ?? gameObject.AddComponent<B1_EntranceSmokeVFX>();
        // 煙の設定(Settings)は RevealSettings 側から PlayEntranceReveal 内で Configure する。
    }

    /// <summary>登場の煙をボスに追従させて出し続ける（煙の中から出現用）。</summary>
    public void PlayEntranceSmoke()
    {
        EnsureEntranceSmoke();
        entranceSmoke.Play();
    }

    /// <summary>登場の煙の放出を止める（残りは寿命でフェードアウト）。</summary>
    public void StopEntranceSmoke()
    {
        if (entranceSmoke != null) entranceSmoke.Stop();
    }

    // --- VFX ---
    public void PlayEyeRushVFX()             { if (eyeRushVFXs != null) foreach (var v in eyeRushVFXs) v?.Play(); }
    public void StopEyeRushVFX()             { if (eyeRushVFXs != null) foreach (var v in eyeRushVFXs) v?.Stop(); }
    public void PlayRushWindVFX(Vector3 dir) => rushWindVFX?.Play(dir);
    public void StopRushWindVFX()            => rushWindVFX?.Stop();
    public void PlayLandingImpact(Vector3 pos)
    {
        landingImpactVFX?.Play(pos);
        // 着地の瞬間だけ地面とボスを照らすフラッシュ (自壊するので管理不要)。
        B1_ImpactLight.Spawn(pos, new Color(1f, 0.85f, 0.6f), intensity: 5f, range: 12f, duration: 0.2f);
    }

    // --- 戦闘の3D衝撃波（設定は B1_EffectSettings に集約） ---
    public enum CombatShockwave { ChargeLaunch, TailLand, ScratchFinisher }

    // effectSettings 未割り当て時のフォールバック（クラス既定値で1波出す）。
    private static readonly B1_CombatShockwaveSettings s_fallbackShockwave = new();

    private B1_CombatShockwaveSettings ShockwaveFor(CombatShockwave kind)
    {
        if (effectSettings == null) return s_fallbackShockwave;
        return kind switch
        {
            CombatShockwave.ChargeLaunch    => effectSettings.ChargeLaunchShockwave,
            CombatShockwave.TailLand        => effectSettings.TailLandShockwave,
            CombatShockwave.ScratchFinisher => effectSettings.ScratchFinisherShockwave,
            _                               => s_fallbackShockwave,
        };
    }

    /// <summary>ダメージ波がプレイヤーを通過したときに出す衝撃波の設定。</summary>
    public B1_CombatShockwaveSettings WavePassShockwave =>
        effectSettings != null ? effectSettings.WavePassShockwave : s_fallbackShockwave;

    // effectSettings 未割り当て時のフォールバック（クラス既定値＝ExplosionVFX代用・跡なし・無音）。
    private static readonly ManaBurstVFXSettings s_fallbackManaBurst = new();

    /// <summary>マナバーストクロス（TailAttack後の十字地面爆発）の見た目・音の設定。</summary>
    public ManaBurstVFXSettings ManaBurst =>
        effectSettings != null ? effectSettings.ManaBurst : s_fallbackManaBurst;

    // スポーン位置 Transform（Inspector で割り当て）。null なら Settings の positionOffset を origin 基準で解決する。
    private Transform SpawnPointFor(CombatShockwave kind) => kind switch
    {
        CombatShockwave.ChargeLaunch    => chargeLaunchShockwavePoint,
        CombatShockwave.TailLand        => tailLandShockwavePoint,
        CombatShockwave.ScratchFinisher => scratchFinisherShockwavePoint,
        _                                => null,
    };

    /// <summary>戦闘アクションの3D衝撃波を1波出す。
    /// 対応するスポーン位置 Transform が割り当てられていればその位置を使い、
    /// 未割り当てなら設定の positionOffset を origin のローカル軸で解決する。
    /// プリセットが無効なら何もしない。</summary>
    public void PlayShockwave(CombatShockwave kind, Transform origin)
    {
        var s = ShockwaveFor(kind);
        if (s == null || !s.enabled) return;

        Transform point = SpawnPointFor(kind);
        Vector3 pos = point != null ? point.position : s.ResolvePosition(origin);
        B1_ShockwaveDistortion.Spawn(pos, s.ToParams());
        FlashFor(kind)?.Play(pos); // 衝撃の瞬間だけ周囲を照らすフラッシュ光(設定OFFなら何もしない)
    }

    /// <summary>
    /// 戦闘アクションの3D衝撃波を指定ワールド座標へ出す。
    /// ボス本体が空中にいても、地面の着弾予定地点へ確実に配置したい場合に使う。
    /// </summary>
    public void PlayShockwaveAt(CombatShockwave kind, Vector3 position)
    {
        var s = ShockwaveFor(kind);
        if (s == null || !s.enabled) return;
        B1_ShockwaveDistortion.Spawn(position, s.ToParams());
        FlashFor(kind)?.Play(position);
    }

    // 衝撃波の種類ごとのフラッシュ光設定（B1_EffectSettings に集約。未割り当てなら光らせない）。
    private ImpactFlashSettings FlashFor(CombatShockwave kind)
    {
        if (effectSettings == null) return null;
        return kind switch
        {
            CombatShockwave.ChargeLaunch    => effectSettings.ChargeLaunchFlash,
            CombatShockwave.TailLand        => effectSettings.TailLandFlash,
            CombatShockwave.ScratchFinisher => effectSettings.ScratchFinisherFlash,
            _                               => null,
        };
    }

    // --- オーサリング VFX プレハブ ---
    public void PlaySlashVFX()
        => SpawnOneShot(slashVFXPrefab, slashSpawnPoint, slashLifetime, slashVFXScale);

    public void PlayExplosionVFX(Vector3 pos)
    {
        SpawnOneShotAt(explosionVFXPrefab, pos, explosionLifetime);
        // 爆発は着地より強く・長めに光らせる。
        B1_ImpactLight.Spawn(pos, new Color(1f, 0.7f, 0.35f), intensity: 7f, range: 14f, duration: 0.3f);
    }

    // 突進直前に目の前へ一瞬呼び出す（複数ポイント可）。lifetime で自動消滅。
    public void PlayDashStarVFX()  => SpawnOneShotMulti(dashStarVFXPrefab, dashStarSpawnPoints, dashStarLifetime);

    // 突進（RushState）中の土煙。Enter で Play、Exit で Stop する。
    // アンカー（ボス配下）に親付けして突進中も追従させる。
    public void PlayDashSmokeVFX()
    {
        StopDashSmokeVFX();
        if (dashSmokeVFXPrefab == null) return;
        Transform anchor = dashSmokeSpawnPoint != null ? dashSmokeSpawnPoint : transform;
        dashSmokeInstance = Instantiate(dashSmokeVFXPrefab, anchor, false);
        dashSmokeInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        PlaySpawnedVFX(dashSmokeInstance);
    }

    public void StopDashSmokeVFX()
    {
        if (dashSmokeInstance == null) return;
        // 切り離して放出だけ止め、残煙は停止位置で寿命まで漂わせてから破棄する。
        dashSmokeInstance.transform.SetParent(null, true);
        foreach (var ps in dashSmokeInstance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        // 土煙プレハブは VFX Graph (VisualEffect) 製。Stop() しないと放出が止まらず、
        // Destroy で漂っている煙ごとポップ消えして不自然になる。
        foreach (var vfx in dashSmokeInstance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            vfx.Stop();
        StartCoroutine(DestroyWhenSmokeSettled(dashSmokeInstance));
        dashSmokeInstance = null;
    }

    // --- ダウン（体力半分 → Phase2 移行前）の VFX と赤白点滅 ---

    /// <summary>Down_Idle が終わるまでループ VFX をボスに重ねて再生する。</summary>
    public void PlayDownVFX()
    {
        StopDownVFX();
        if (downVFXPrefab == null) return;
        Transform anchor = downVFXTargetPoint != null ? downVFXTargetPoint : transform;
        downVFXInstance = Instantiate(downVFXPrefab, anchor, false);
        downVFXInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        // VFX_Enemy04_Down は元プレハブでは1秒の非ループ設定。
        // Boss01では生成したインスタンスだけループ化し、DownState.Exit の StopDownVFX まで放出を維持する。
        foreach (var ps in downVFXInstance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = true;
        }

        PlaySpawnedVFX(downVFXInstance);
    }

    /// <summary>ダウン VFX の放出を止める。残りパーティクルは寿命まで漂わせてから破棄する。</summary>
    public void StopDownVFX()
    {
        if (downVFXInstance == null) return;
        downVFXInstance.transform.SetParent(null, true);
        foreach (var ps in downVFXInstance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var vfx in downVFXInstance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            vfx.Stop();
        StartCoroutine(DestroyWhenSmokeSettled(downVFXInstance));
        downVFXInstance = null;
    }

    // 残煙が自然に消えきるのを待ってから破棄する (固定時間だと生存中の煙がポップ消えする)。
    private System.Collections.IEnumerator DestroyWhenSmokeSettled(GameObject instance)
    {
        float deadline = Time.time + dashSmokeMaxLinger;
        var wait = new WaitForSeconds(0.25f);
        while (instance != null && Time.time < deadline)
        {
            yield return wait;
            if (instance == null) yield break;

            bool alive = false;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.IsAlive(true)) { alive = true; break; }
            if (!alive)
                foreach (var vfx in instance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                    if (vfx.aliveParticleCount > 0) { alive = true; break; }

            if (!alive) break;
        }
        if (instance != null) Destroy(instance);
    }

    // アンカー基準で配置。worldPositionStays=false でプレハブ authored の向き・スケールを保持する。
    // scale はプレハブ authored のスケールに乗算する倍率。
    private void SpawnOneShot(GameObject prefab, Transform at, float lifetime, float scale = 1f)
    {
        if (prefab == null) return;
        Transform anchor = at != null ? at : transform;
        var inst = Instantiate(prefab, anchor, false);
        if (!Mathf.Approximately(scale, 1f))
            inst.transform.localScale *= scale;
        PlaySpawnedVFX(inst);
        if (lifetime > 0f) Destroy(inst, lifetime);
    }

    // 指定ワールド座標へ配置。回転・スケールはプレハブ authored を維持する。
    private void SpawnOneShotAt(GameObject prefab, Vector3 pos, float lifetime)
    {
        if (prefab == null) return;
        var inst = Instantiate(prefab);
        inst.transform.position = pos;
        PlaySpawnedVFX(inst);
        if (lifetime > 0f) Destroy(inst, lifetime);
    }

    private void PlaySpawnedVFX(GameObject instance)
    {
        if (instance == null) return;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play(true);
        foreach (var vfx in instance.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            vfx.Play();
    }

    // 複数のスポーンポイントへ同時に単発生成する（DashStar を左右など複数並べる用）。
    private void SpawnOneShotMulti(GameObject prefab, Transform[] points, float lifetime)
    {
        if (prefab == null) return;
        if (points == null || points.Length == 0)
        {
            SpawnOneShot(prefab, null, lifetime);
            return;
        }
        foreach (var p in points)
            SpawnOneShot(prefab, p, lifetime);
    }

    // --- 残像トレイル ---
    public void StartTrail() { if (trailRenderer != null) trailRenderer.IsEmitting = true; }
    public void StopTrail()  { if (trailRenderer != null) trailRenderer.IsEmitting = false; }

    // 激昂中は左右の目に付けた通常の TrailRenderer を連続発生させる。
    private void StartEnrageTrail()
    {
        if (!enrageTrailEnabled) return;
        EnsureEyeTrailVFXs();
        if (eyeRushVFXs == null || eyeRushVFXs.Length == 0)
        {
            Debug.LogWarning("[B1_EffectManager] 激昂中の目Trailを出せません。enrageTintRenderers に左右の目Rendererを設定してください。", this);
            return;
        }

        foreach (var v in eyeRushVFXs) v?.SetEnrageTrail(true);
    }

    private void StopEnrageTrail()
    {
        if (eyeRushVFXs == null) return;
        foreach (var v in eyeRushVFXs) v?.SetEnrageTrail(false);
    }

    // 目オブジェクトに B1_EyeRushVFX が無いプレハブも、激昂対象Rendererから自動構築する。
    // enrageTintRenderers が空のときは全身へ誤ってTrailを付けない。
    private void EnsureEyeTrailVFXs()
    {
        // Renderer とポイントの対応順を常に維持するため、明示された激昂対象から配列を組み直す。
        if (enrageTintRenderers != null && enrageTintRenderers.Length > 0)
        {
            var list = new System.Collections.Generic.List<B1_EyeRushVFX>();
            foreach (var renderer in enrageTintRenderers)
            {
                if (renderer == null) continue;
                var vfx = renderer.GetComponent<B1_EyeRushVFX>() ?? renderer.gameObject.AddComponent<B1_EyeRushVFX>();
                list.Add(vfx);
            }
            eyeRushVFXs = list.ToArray();
        }
        else
        {
            eyeRushVFXs = GetComponentsInChildren<B1_EyeRushVFX>(true);
        }

        for (int i = 0; i < eyeRushVFXs.Length; i++)
        {
            var vfx = eyeRushVFXs[i];
            if (vfx == null) continue;
            if (effectSettings != null) vfx.Configure(effectSettings.EyeRush);
            Transform point = enrageEyeTrailPoints != null && i < enrageEyeTrailPoints.Length
                ? enrageEyeTrailPoints[i]
                : null;
            vfx.SetTrailTarget(point);
        }
    }

    // --- スケールパルス ---
    public void StartScalePulse(float duration = 4f, float peakScale = 1.35f,
                                float frequencyStart = 2.5f, float frequencyEnd = 8f,
                                float crouchAmount = 0.2f, float crouchDuration = 0.2f)
    {
        // パルス/着地スカッシュ中でなければ現在の localScale を基準として取り込む（多重呼び出しでも本来値を維持）。
        if (scalePulseCoroutine == null && landingSquashCoroutine == null) baseScale = transform.localScale;
        StopScalePulse();
        scalePulseCoroutine = StartCoroutine(ScalePulseRoutine(
            duration, peakScale, frequencyStart, frequencyEnd, crouchAmount, crouchDuration));
    }

    public void StopScalePulse()
    {
        if (scalePulseCoroutine != null)
        {
            StopCoroutine(scalePulseCoroutine);
            scalePulseCoroutine = null;
        }
        transform.localScale = baseScale;
    }

    private IEnumerator ScalePulseRoutine(float duration, float peakScale,
                                          float frequencyStart, float frequencyEnd,
                                          float crouchAmount, float crouchDuration)
    {
        duration = Mathf.Max(0.0001f, duration);
        peakScale = Mathf.Max(0f, peakScale);
        frequencyStart = Mathf.Max(0f, frequencyStart);
        frequencyEnd = Mathf.Max(0f, frequencyEnd);
        crouchAmount = Mathf.Clamp(crouchAmount, 0f, 0.5f);

        float t = 0f;
        float phase = 0f;
        while (t < duration)
        {
            float progress = Mathf.Clamp01(t / duration);
            float frequency = Mathf.Lerp(frequencyStart, frequencyEnd, progress * progress);
            phase += frequency * Mathf.PI * 2f * Time.deltaTime;

            float pulse = 1f + (peakScale - 1f) * Mathf.Abs(Mathf.Sin(phase));
            float crouchProgress = crouchDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / crouchDuration));
            float crouchY = 1f - crouchAmount * crouchProgress;

            transform.localScale = Vector3.Scale(baseScale, new Vector3(pulse, pulse * crouchY, pulse));
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = baseScale;
        scalePulseCoroutine = null;
    }

    // --- 着地の Squash & Stretch（TailAttack。設定は B1_EffectSettings に集約） ---
    private Coroutine landingSquashCoroutine;

    /// <summary>着地の瞬間に潰し、減衰振動で弾ませながら基準スケールへ戻す。</summary>
    public void PlayLandingSquash()
    {
        var s = effectSettings != null ? effectSettings.LandingSquash : null;
        if (s == null || !s.Enabled) return;

        // ScalePulse と同じ流儀：演出中でなければ現在の localScale を基準として取り込む。
        if (scalePulseCoroutine == null && landingSquashCoroutine == null)
            baseScale = transform.localScale;
        if (landingSquashCoroutine != null) StopCoroutine(landingSquashCoroutine);
        landingSquashCoroutine = StartCoroutine(LandingSquashRoutine(s));
    }

    public void StopLandingSquash()
    {
        if (landingSquashCoroutine == null) return;
        StopCoroutine(landingSquashCoroutine);
        landingSquashCoroutine = null;
        transform.localScale = baseScale;
    }

    // 潰れ量を (1-k)^2 の包絡線 × cos の減衰振動で駆動する。
    // t=0 で SquashScale ちょうど、半周期ごとに 潰れ⇔伸び を反転しながら基準へ収束する。
    private IEnumerator LandingSquashRoutine(LandingSquashSettings s)
    {
        float duration = Mathf.Max(0.05f, s.Duration);
        Vector3 amp = s.SquashScale - Vector3.one; // 潰れ方向の振幅（Y負・XZ正）

        float t = 0f;
        while (t < duration)
        {
            float k = Mathf.Clamp01(t / duration);
            float envelope = (1f - k) * (1f - k);
            float wave = Mathf.Cos(k * Mathf.PI * s.ReboundCycles); // 1 → -1 → … 潰れと伸びを往復
            transform.localScale = Vector3.Scale(baseScale, Vector3.one + amp * (envelope * wave));
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = baseScale;
        landingSquashCoroutine = null;
    }

    // --- 攻撃前の猫らしい狙い構え（モデルだけを潰し、Collider/NavMeshには影響させない） ---
    public void BeginAimCrouch()
    {
        if (aimPoseTarget == null)
            aimPoseTarget = GetComponentInChildren<Animator>(true)?.transform ?? transform;
        if (aimCrouchActive) EndAimCrouch();
        aimPoseBaseScale = aimPoseTarget.localScale;
        aimCrouchActive = true;
    }

    public void SetAimCrouch(float amount, float crouchAmount, float widthScale)
    {
        if (!aimCrouchActive || aimPoseTarget == null) return;

        amount = Mathf.Clamp01(amount);
        float y = 1f - Mathf.Clamp(crouchAmount, 0f, 0.5f) * amount;
        float xz = Mathf.Lerp(1f, Mathf.Max(1f, widthScale), amount);
        aimPoseTarget.localScale = Vector3.Scale(aimPoseBaseScale, new Vector3(xz, y, xz));
    }

    public void EndAimCrouch()
    {
        if (aimPoseTarget != null) aimPoseTarget.localScale = aimPoseBaseScale;
        aimCrouchActive = false;
    }

    // --- 透明化 ---
    public void BlendRushMaterialToTransparent(float duration)
    {
        if (invisibleMaterial == null) return;
        if (appearDissolveCoroutine != null) return;
        if (rushMaterialBlendCoroutine != null) StopCoroutine(rushMaterialBlendCoroutine);
        rushMaterialBlendCoroutine = StartCoroutine(RushMaterialBlendRoutine(duration, toTransparent: true));
    }

    public void BlendRushMaterialToNormal(float duration)
    {
        if (invisibleMaterial == null) return;
        if (appearDissolveCoroutine != null) return;
        if (rushMaterialBlendCoroutine != null) StopCoroutine(rushMaterialBlendCoroutine);
        rushMaterialBlendCoroutine = StartCoroutine(RushMaterialBlendRoutine(duration, toTransparent: false));
    }

    public void CompleteRushMaterialBlendToNormal()
    {
        if (rushMaterialBlendCoroutine != null)
        {
            StopCoroutine(rushMaterialBlendCoroutine);
            rushMaterialBlendCoroutine = null;
        }
        SetRealDissolve(0f, invert: false);
        SetGhostDissolve(1f);
        DestroyGhostOverlay();
        ClearRealDissolve();
    }

    private IEnumerator RushMaterialBlendRoutine(float duration, bool toTransparent)
    {
        revealCloakMaterial = invisibleMaterial;
        revealNoiseScale = 3f;

        if (ghostMaterialInstance == null)
        {
            BuildGhostOverlay();
            SetGhostActive(true);
        }

        float d = Mathf.Max(0.0001f, duration);
        float t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / d);
            if (toTransparent)
            {
                SetGhostDissolve(1f - k);
                SetRealDissolve(k, invert: false);
            }
            else
            {
                SetGhostDissolve(k);
                SetRealDissolve(1f - k, invert: false);
            }
            yield return null;
        }

        if (toTransparent)
        {
            SetGhostDissolve(0f);
            SetRealDissolve(1f, invert: false);
        }
        else
        {
            SetRealDissolve(0f, invert: false);
            SetGhostDissolve(1f);
            DestroyGhostOverlay();
            ClearRealDissolve();
            rushMaterialBlendCoroutine = null;
            yield break;
        }

        rushMaterialBlendCoroutine = null;
    }

    public void SetInvisible(bool invisible)
    {
        EnsureModelRenderers();
        if (invisible)
        {
            if (invisibleMaterial == null) return;
            foreach (var r in modelRenderers)
            {
                if (r == null) continue;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = invisibleMaterial;
                r.materials = mats;
            }
        }
        else
        {
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                if (modelRenderers[i] == null || originalMaterials == null || i >= originalMaterials.Length) continue;
                modelRenderers[i].materials = originalMaterials[i];
            }

            if (enrageActive && enrageOverrideMaterial != null)
                ApplyEnrageMaterials();
        }
    }

    private void EnsureModelRenderers()
    {
        if (modelRenderers == null || modelRenderers.Length == 0)
            modelRenderers = GetComponentsInChildren<Renderer>(true);
        if (originalMaterials != null && originalMaterials.Length == modelRenderers.Length) return;
        originalMaterials = new Material[modelRenderers.Length][];
        for (int i = 0; i < modelRenderers.Length; i++)
            originalMaterials[i] = modelRenderers[i] != null ? modelRenderers[i].materials : new Material[0];
    }

    // --- 出現（隠し → 表示）。透明化(SetInvisible)とは別で、レンダラーを完全に ON/OFF する ---
    public bool Hidden { get; private set; }

    /// <summary>ボスモデルを完全に隠す/表示する。エントランスの「煙の中から出現」用。</summary>
    public void SetHidden(bool hidden)
    {
        EnsureModelRenderers();
        Hidden = hidden;
        foreach (var r in modelRenderers)
            if (r != null) r.enabled = !hidden;
    }

    // --- 登場ディゾルブ（Skydome 方式）------------------------------------
    //  実メッシュの複製ゴーストに InvisibleMaterial を着せて手前に重ね、
    //  ゴースト側の _Dissolve を 0→1 へ動かしてディザ・クリップで溶かし、
    //  背面の実マテリアルを露わにする（＝ InvisibleMaterial → 実マテリアルのクロスフェード）。
    //  ・_Dissolve は Skydome と同じスクリーンスペース・ディザ（Custom/Toon に実装）。
    //  ・InvisibleMaterial のシェーダーが _Dissolve を持つ必要がある（Custom/Toon 等）。

    /// <summary>
    /// 登場フル演出：煙(smokeLead 秒) → 透明(クローク)状態で出現 → B1_Toon へ相補ディゾルブ。
    /// クローク(消える)と実ボス(現れる)を同じノイズ場で相補クリップするため、
    /// どのピクセルも常に片方だけが見え「B1_Toon と透明の2メッシュが同時に出る」ことがない。
    /// 実ボスのマテリアルは _DissolveInvert を持つ Custom/B1_Toon であること。
    /// </summary>
    /// <summary>登場出現(煙〜ディゾルブ)を再生中かどうか。CinematicState が戦闘開始を待つのに使う。</summary>
    public bool IsEntranceRevealing => appearDissolveCoroutine != null;

    public void PlayEntranceReveal(B1_CameraSceneSettings.RevealSettings r)
    {
        if (r == null) return;

        // 設定を transient に確定（ゴースト/煙/MPB がこれを参照）。
        revealCloakMaterial    = r.cloakMaterial != null ? r.cloakMaterial : invisibleMaterial;
        revealNoiseScale       = r.dissolveNoiseScale;
        revealGhostQueueOffset = r.ghostQueueOffset;
        EnsureEntranceSmoke();
        entranceSmoke.Configure(r.smoke);

        var s = r.sequence;
        if (appearDissolveCoroutine != null) StopCoroutine(appearDissolveCoroutine);
        appearDissolveCoroutine = StartCoroutine(EntranceRevealRoutine(
            Mathf.Max(0f, s.smoke), Mathf.Max(0f, s.cloakFadeIn),
            Mathf.Max(0f, s.transparentHold), Mathf.Max(0.0001f, s.dissolve), s.dissolveCurve));
    }

    private IEnumerator EntranceRevealRoutine(float smokeLead, float cloakFadeIn, float transparentHold,
                                              float dissolveDuration, AnimationCurve curve)
    {
        // 1) 煙だけ（ボスは完全に隠す）。煙が smokeLead 秒かけて出現位置に立ち上る。
        SetHidden(true);
        PlayEntranceSmoke();
        yield return WaitForSecondsRoutine(smokeLead);

        // クローク素材が無ければ、煙のあとにそのまま実ボスを表示して終了（フォールバック）。
        if (revealCloakMaterial == null)
        {
            SetHidden(false);
            StopEntranceSmoke();
            appearDissolveCoroutine = null;
            yield break;
        }

        // 2) 完全に不可視の状態から開始 → 透明（クローク）を有機ノイズでフェードイン。
        //    クローク _Dissolve=1（消えた状態）＋ 実ボス invert amount=0（隠れ）＝ 何も見えない。
        BuildGhostOverlay();
        SetGhostActive(true);
        SetHidden(false);
        SetRealDissolve(0f);          // invert=1, amount=0 → 実ボスは全ピクセル clip（見えない）
        SetGhostDissolve(1f);         // クロークも消えた状態から開始 → 完全 Invisible
        yield return null;            // 完全不可視で1フレーム確定（青フラッシュ防止）

        float fin = Mathf.Max(0f, cloakFadeIn);
        float tf = 0f;
        while (tf < fin)
        {
            tf += Time.deltaTime;
            float kf = curve != null ? curve.Evaluate(Mathf.Clamp01(tf / fin)) : Mathf.Clamp01(tf / fin);
            SetGhostDissolve(1f - kf); // 1→0 でクロークが有機ノイズで出現
            yield return null;
        }
        SetGhostDissolve(0f);         // クローク完全表示（透明状態）
        yield return WaitForSecondsRoutine(transparentHold);

        // 3) クローク→B1_Toon の相補クロスフェード。
        float d = Mathf.Max(0.0001f, dissolveDuration);
        float t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;
            float k = curve != null ? curve.Evaluate(Mathf.Clamp01(t / d)) : Mathf.Clamp01(t / d);
            SetGhostDissolve(k);      // クローク: amount 0→1 で消える
            SetRealDissolve(k);       // 実ボス: invert で amount 0→1 で現れる（相補）
            yield return null;
        }

        // 4) 完了：煙を止め、ゴースト破棄、実ボスを通常表示（MPB クリア）に戻す。
        StopEntranceSmoke();
        SetGhostDissolve(1f);
        DestroyGhostOverlay();
        ClearRealDissolve();
        appearDissolveCoroutine = null;
    }

    private static IEnumerator WaitForSecondsRoutine(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.deltaTime; yield return null; }
    }

    // 実ボスの各レンダラーに _Dissolve / _DissolveInvert / _DissolveScale を MPB で流し込む（現れる側）。
    private void SetRealDissolve(float amount)
    {
        SetRealDissolve(amount, invert: true);
    }

    private void SetRealDissolve(float amount, bool invert)
    {
        EnsureModelRenderers();
        if (realMpb == null) realMpb = new MaterialPropertyBlock();
        foreach (var r in modelRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(realMpb);
            realMpb.SetFloat(DissolveId, amount);
            realMpb.SetFloat(DissolveInvertId, invert ? 1f : 0f);
            realMpb.SetFloat(DissolveScaleId, revealNoiseScale);
            r.SetPropertyBlock(realMpb);
        }
    }

    private void ClearRealDissolve()
    {
        EnsureModelRenderers();
        if (realMpb == null) realMpb = new MaterialPropertyBlock();
        foreach (var r in modelRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(realMpb);
            realMpb.SetFloat(DissolveId, 0f);
            realMpb.SetFloat(DissolveInvertId, 0f);
            r.SetPropertyBlock(realMpb);
        }
    }

    private void BuildGhostOverlay()
    {
        DestroyGhostOverlay();
        if (revealCloakMaterial == null) return;
        EnsureModelRenderers();

        ghostMaterialInstance = new Material(revealCloakMaterial);
        if (!ghostMaterialInstance.HasProperty(DissolveId))
            Debug.LogWarning("[B1_EffectManager] クローク用マテリアルのシェーダーに _Dissolve がありません。" +
                             "Custom/B1_Toon や InvisibleDistortion など _Dissolve を持つシェーダーを使うとクロスフェードできます。", this);
        // 実ボスと同じノイズ場になるよう粗さを合わせる（相補クリップの整合）。
        if (ghostMaterialInstance.HasProperty(DissolveScaleId))
            ghostMaterialInstance.SetFloat(DissolveScaleId, revealNoiseScale);
        // 描画順：
        //  ・不透明ゴースト → 実メッシュより手前に寄せて覆う（溶けた穴から背面の実マテリアルが見える）
        //  ・透明ゴースト(InvisibleDistortion 等) → 本来の Transparent キューを維持する
        //    （下げると SampleSceneColor が実ボスを含まなくなりクロークが破綻する）
        int naturalQueue = ghostMaterialInstance.renderQueue;   // コピーが持つ解決済みキュー
        int bumpedQueue  = MaxOriginalRenderQueue() + Mathf.Max(1, revealGhostQueueOffset);
        ghostMaterialInstance.renderQueue = Mathf.Max(naturalQueue, bumpedQueue);

        foreach (var src in modelRenderers)
        {
            if (src == null) continue;
            var ghost = CreateGhostFor(src, ghostMaterialInstance);
            if (ghost != null) ghostRenderers.Add(ghost);
        }
    }

    private int MaxOriginalRenderQueue()
    {
        int q = 2000; // Geometry
        foreach (var r in modelRenderers)
        {
            if (r == null) continue;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.renderQueue >= 0) q = Mathf.Max(q, m.renderQueue);
        }
        return q;
    }

    private static Renderer CreateGhostFor(Renderer src, Material mat)
    {
        int subCount = Mathf.Max(1, src.sharedMaterials.Length);
        var mats = new Material[subCount];
        for (int i = 0; i < subCount; i++) mats[i] = mat;

        var go = new GameObject(src.name + "_DissolveGhost");
        go.transform.SetParent(src.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        go.layer = src.gameObject.layer;

        if (src is SkinnedMeshRenderer smr)
        {
            var g = go.AddComponent<SkinnedMeshRenderer>();
            g.sharedMesh          = smr.sharedMesh;
            g.bones               = smr.bones;
            g.rootBone            = smr.rootBone;
            g.localBounds         = smr.localBounds;
            g.quality             = smr.quality;
            g.updateWhenOffscreen = smr.updateWhenOffscreen;
            g.sharedMaterials     = mats;
            g.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        var mf = src.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var g = go.AddComponent<MeshRenderer>();
            g.sharedMaterials   = mats;
            g.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        Destroy(go);
        return null;
    }

    private void SetGhostDissolve(float value)
    {
        if (ghostMaterialInstance != null && ghostMaterialInstance.HasProperty(DissolveId))
            ghostMaterialInstance.SetFloat(DissolveId, value);
    }

    private void SetGhostActive(bool active)
    {
        foreach (var g in ghostRenderers)
            if (g != null) g.gameObject.SetActive(active);
    }

    private void DestroyGhostOverlay()
    {
        foreach (var g in ghostRenderers)
            if (g != null) Destroy(g.gameObject);
        ghostRenderers.Clear();
        if (ghostMaterialInstance != null) { Destroy(ghostMaterialInstance); ghostMaterialInstance = null; }
    }
}
