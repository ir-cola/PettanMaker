using UnityEngine;

/// <summary>
/// 突進中に目のメッシュを赤く光らせ、TrailRenderer で光の軌跡を出す。
/// Eye メッシュの GameObject にアタッチする。
/// </summary>
public class B1_EyeRushVFX : MonoBehaviour
{
    [Header("マテリアル差し替え")]
    [SerializeField] private Material rushMaterial;

    [Header("Trail")]
    [SerializeField] private float   trailTime       = 0.25f;
    [SerializeField] private float   trailWidthStart = 0.08f;
    [SerializeField] private float   trailWidthEnd   = 0.0f;
    [SerializeField] private Color   trailColorHead  = new Color(1f, 0.15f, 0.05f, 1f);
    [SerializeField] private Color   trailColorTail  = new Color(1f, 0.35f, 0.1f,  0f);
    [Tooltip("未設定の場合は Unity デフォルトの Trail マテリアルを使用")]
    [SerializeField] private Material trailMaterial;

    private Renderer      meshRenderer;
    private SkinnedMeshRenderer skinnedMeshRenderer;
    private Material[]    originalMaterials;
    private TrailRenderer trail;
    private Transform     trailAnchor;
    private Transform     trailTarget;
    private Mesh          bakedMesh;
    private bool          rushActive;
    private bool          enrageActive;

    private void Awake()
    {
        meshRenderer = GetComponent<Renderer>();
        skinnedMeshRenderer = meshRenderer as SkinnedMeshRenderer;
        if (meshRenderer != null)
            originalMaterials = meshRenderer.sharedMaterials;
        // BuildTrail は Play() で遅延実行する。Configure() を先に呼べるようにするため。
    }

    /// <summary>B1_EffectSettings からパラメータを適用する（Play() より前に呼ぶこと）。</summary>
    public void Configure(EyeRushVFXSettings cfg)
    {
        if (cfg == null) return;
        trailTime       = cfg.TrailTime;
        trailWidthStart = cfg.TrailWidthStart;
        trailWidthEnd   = cfg.TrailWidthEnd;
        trailColorHead  = cfg.TrailColorHead;
        trailColorTail  = cfg.TrailColorTail;
        if (cfg.RushMaterial  != null) rushMaterial  = cfg.RushMaterial;
        if (cfg.TrailMaterial != null) trailMaterial = cfg.TrailMaterial;
    }

    public void Play()
    {
        if (trail == null) BuildTrail();
        rushActive = true;
        SwapToRushMaterial();
        UpdateTrailEmission();
    }

    public void Stop()
    {
        rushActive = false;
        if (!enrageActive) RestoreOriginalMaterials();
        UpdateTrailEmission();
    }

    /// <summary>
    /// 激昂中の目Trailを切り替える。突進とは独立しているため、突進終了時にも激昂Trailは途切れない。
    /// </summary>
    public void SetEnrageTrail(bool active)
    {
        if (active && trail == null) BuildTrail();
        enrageActive = active;
        if (!active && !rushActive) RestoreOriginalMaterials();
        UpdateTrailEmission();
    }

    /// <summary>Trailの明示的な追従先。null のときだけ変形後メッシュ中心を使用する。</summary>
    public void SetTrailTarget(Transform target)
    {
        trailTarget = target;
        UpdateTrailAnchorPosition();
    }

    private void LateUpdate()
    {
        // SkinnedMeshRenderer の GameObject 自体は、ボーンで目が動いても移動しない場合がある。
        // 描画後の world bounds 中心へアンカーを追従させ、TrailRenderer の点を実際の目位置に置く。
        if (rushActive || enrageActive) UpdateTrailAnchorPosition();
    }

    private void OnDisable()
    {
        rushActive = false;
        enrageActive = false;
        RestoreOriginalMaterials();
        if (trail == null) return;
        trail.emitting = false;
        trail.Clear();
    }

    private void OnDestroy()
    {
        if (bakedMesh != null) Destroy(bakedMesh);
    }

    private void SwapToRushMaterial()
    {
        if (meshRenderer == null || rushMaterial == null) return;
        var mats = new Material[meshRenderer.sharedMaterials.Length];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = rushMaterial;
        meshRenderer.materials = mats;
    }

    private void RestoreOriginalMaterials()
    {
        if (meshRenderer == null || originalMaterials == null) return;
        meshRenderer.materials = originalMaterials;
    }

    private void BuildTrail()
    {
        // アンカーは LateUpdate でレンダラーの world bounds 中心へ追従させる。
        // SkinnedMeshRenderer の変形後の目位置を使うため、メッシュのスナップショット残像にならない。
        var anchor = new GameObject("TrailAnchor");
        anchor.transform.SetParent(transform, false);
        trailAnchor = anchor.transform;
        UpdateTrailAnchorPosition();

        trail                   = anchor.AddComponent<TrailRenderer>();
        trail.time              = trailTime;
        trail.startWidth        = trailWidthStart;
        trail.endWidth          = trailWidthEnd;
        trail.minVertexDistance = 0.02f;
        trail.numCornerVertices = 4;
        trail.numCapVertices    = 4;
        trail.alignment         = LineAlignment.View;
        trail.textureMode       = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows    = false;
        trail.generateLightingData = false;

        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(trailColorHead, 0f),
                new GradientColorKey(trailColorTail, 1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(trailColorHead.a, 0f),
                new GradientAlphaKey(0f,               1f),
            });
        trail.colorGradient = grad;

        if (trailMaterial != null)
            trail.material = trailMaterial;

        trail.emitting = false;
    }

    private void UpdateTrailEmission()
    {
        if (trail == null) return;

        bool shouldEmit = rushActive || enrageActive;
        if (shouldEmit == trail.emitting) return;

        if (shouldEmit)
        {
            UpdateTrailAnchorPosition();
            trail.Clear();
        }
        trail.emitting = shouldEmit;
    }

    private void UpdateTrailAnchorPosition()
    {
        if (trailAnchor == null) return;

        if (trailTarget != null)
        {
            trailAnchor.SetPositionAndRotation(trailTarget.position, trailTarget.rotation);
            return;
        }

        // Renderer.bounds はインポート時の広い localBounds になることがあるため、
        // SkinnedMesh は小さな目メッシュを Bake して変形後の実中心を求める。
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            if (bakedMesh == null)
            {
                bakedMesh = new Mesh { name = name + "_EyeTrailBake", hideFlags = HideFlags.DontSave };
                bakedMesh.MarkDynamic();
            }
            skinnedMeshRenderer.BakeMesh(bakedMesh);
            trailAnchor.position = skinnedMeshRenderer.transform.TransformPoint(bakedMesh.bounds.center);
            return;
        }

        trailAnchor.position = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
    }
}
