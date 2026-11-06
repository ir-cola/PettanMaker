using UnityEngine;

/// <summary>
/// スタン（目を回した）演出の「ぐるぐる星」。頭上のアンカー周りに複数の星を
/// 円軌道（Sin/Cos）で公転させ、上下バウンドを重ねる。星は PettanMaker/VFX/StunStar
/// シェーダー（プロシージャル・ビルボード）で描くのでテクスチャ不要。
/// Enemy04 のスタン開始で <see cref="Play"/>、回復完了で <see cref="Stop"/> を呼ぶ。
/// </summary>
public class Enemy04StunStars : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("頭上の基準。未設定なら自身の Transform を使う。")]
    [SerializeField] private Transform anchor;
    [Tooltip("アンカーからの高さ（頭上へのオフセット）。")]
    [SerializeField] private float heightOffset = 1.6f;

    [Header("星")]
    [SerializeField, Min(1)] private int starCount = 3;
    [SerializeField] private float starSize = 0.35f;
    [SerializeField, ColorUsage(true, true)] private Color starColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color edgeColor = new Color(1f, 0.45f, 0.05f, 1f);
    [Tooltip("空なら Shader.Find(\"PettanMaker/VFX/StunStar\") で生成する。")]
    [SerializeField] private Material starMaterial;

    [Header("公転")]
    [Tooltip("公転の半径。")]
    [SerializeField] private float orbitRadius = 0.45f;
    [Tooltip("公転速度（度/秒）。")]
    [SerializeField] private float orbitSpeed = 220f;
    [Tooltip("軌道の傾き（度）。斜め上から見た楕円感を出す。")]
    [SerializeField, Range(0f, 60f)] private float orbitTilt = 20f;

    [Header("バウンド")]
    [SerializeField] private float bobAmplitude = 0.08f;
    [SerializeField] private float bobSpeed = 3f;

    [Header("トレイル")]
    [SerializeField] private bool enableTrail = true;
    [Tooltip("尾の残る時間（秒）。")]
    [SerializeField] private float trailTime = 0.35f;
    [Tooltip("尾の根本の太さ（先端は0へ細くなる）。")]
    [SerializeField] private float trailWidth = 0.12f;
    [Tooltip("空なら Shader.Find(\"PettanMaker/VFX/AdditiveTrail\") で生成する。")]
    [SerializeField] private Material trailMaterial;

    [Header("出現")]
    [Tooltip("ポップイン（拡大）にかける時間。")]
    [SerializeField] private float spawnScaleTime = 0.15f;

    private Transform container;
    private Transform[] stars;
    private TrailRenderer[] trails;
    private float angle;
    private float scaleT;
    private bool playing;

    /// <summary>星を出して回し始める。</summary>
    public void Play()
    {
        EnsureBuilt();
        angle = 0f;
        scaleT = 0f;
        playing = true;
        container.gameObject.SetActive(true);

        // 開始位置へ即座に置いてからトレイルをクリアし、原点からの直線を引かせない。
        UpdateStarTransforms(0f);
        if (trails != null)
            foreach (TrailRenderer tr in trails)
                if (tr != null) tr.Clear();
    }

    /// <summary>星を消す。</summary>
    public void Stop()
    {
        playing = false;
        if (container != null)
            container.gameObject.SetActive(false);
    }

    private void EnsureBuilt()
    {
        if (container != null) return;

        Transform baseTransform = anchor != null ? anchor : transform;

        container = new GameObject("StunStars").transform;
        container.SetParent(baseTransform, false);
        container.localPosition = new Vector3(0f, heightOffset, 0f);
        container.localRotation = Quaternion.identity;

        Material mat = ResolveMaterial();
        Material trailMat = enableTrail ? ResolveTrailMaterial() : null;

        stars = new Transform[starCount];
        trails = new TrailRenderer[starCount];
        for (int i = 0; i < starCount; i++)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Star_{i}";
            Collider col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            Transform t = quad.transform;
            t.SetParent(container, false);
            t.localScale = Vector3.zero;
            stars[i] = t;

            if (enableTrail)
                trails[i] = BuildTrail(quad, trailMat);
        }

        container.gameObject.SetActive(false);
    }

    // 星に尾を付ける。太さは根本→先端で細く、色は星色から透明へフェード。
    private TrailRenderer BuildTrail(GameObject star, Material trailMat)
    {
        TrailRenderer tr = star.AddComponent<TrailRenderer>();
        tr.material = trailMat;
        tr.time = trailTime;
        tr.startWidth = trailWidth;
        tr.endWidth = 0f;
        tr.minVertexDistance = 0.02f;
        tr.numCapVertices = 2;
        tr.numCornerVertices = 2;
        tr.autodestruct = false;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        tr.alignment = LineAlignment.View;   // ビルボード（常にカメラを向く帯）

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(starColor, 0f), new GradientColorKey(starColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        return tr;
    }

    // シェーダーを解決し、色を反映したインスタンスを作る（共有アセットを汚さない）。
    private Material ResolveMaterial()
    {
        Shader shader = starMaterial != null && starMaterial.shader != null
            ? starMaterial.shader
            : Shader.Find("PettanMaker/VFX/StunStar");

        Material mat = starMaterial != null ? new Material(starMaterial) : new Material(shader);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", starColor);
        if (mat.HasProperty("_EdgeColor")) mat.SetColor("_EdgeColor", edgeColor);
        return mat;
    }

    private Material ResolveTrailMaterial()
    {
        Shader shader = trailMaterial != null && trailMaterial.shader != null
            ? trailMaterial.shader
            : Shader.Find("PettanMaker/VFX/AdditiveTrail");

        Material mat = trailMaterial != null ? new Material(trailMaterial) : new Material(shader);
        if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", starColor);
        return mat;
    }

    private void LateUpdate()
    {
        if (!playing || stars == null) return;

        angle += orbitSpeed * Time.deltaTime;
        scaleT = Mathf.Min(1f, scaleT + Time.deltaTime / Mathf.Max(0.0001f, spawnScaleTime));
        UpdateStarTransforms(Mathf.SmoothStep(0f, 1f, scaleT));
    }

    // 現在の angle / pop から各星の位置とスケールを更新する。Play と LateUpdate で共用。
    private void UpdateStarTransforms(float pop)
    {
        if (stars == null) return;
        float tiltSin = Mathf.Sin(orbitTilt * Mathf.Deg2Rad);

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;

            float phaseDeg = angle + i * (360f / stars.Length);
            float rad = phaseDeg * Mathf.Deg2Rad;

            // 円軌道（Sin/Cos）。Y に軌道の傾きぶんを乗せて斜め楕円にする。
            float x = Mathf.Cos(rad) * orbitRadius;
            float z = Mathf.Sin(rad) * orbitRadius;
            float y = Mathf.Sin(rad) * orbitRadius * tiltSin;

            // 上下バウンドを重ねる（星ごとに位相をずらす）。
            y += Mathf.Sin(Time.time * bobSpeed + i) * bobAmplitude;

            stars[i].localPosition = new Vector3(x, y, z);
            stars[i].localScale = Vector3.one * (starSize * pop);
        }
    }

    private void OnDestroy()
    {
        if (container != null)
            Destroy(container.gameObject);
    }
}
