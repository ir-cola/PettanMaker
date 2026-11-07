/**===========================================================================

 File   : 回避残像 [PlayerDodgeTrail.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/4
 -----------------------------------------------------------------------------
 回避ロール中に SkinnedMeshTrailRenderer (BakeMesh 残像) と赤い紙吹雪パーティクルを
 放出する。StateRoll から Ensure() 経由でランタイム装着されるためシーン設定は不要。
 着せ替えで SkinnedMeshRenderer が入れ替わるので放出開始のたびに sources を収集し直し、
 回避終了後も残速度に合わせて emitTail 秒だけ放出を続けてから止める。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerDodgeTrail : MonoBehaviour {

    [Header("残像設定 (未設定は既定値で自動構成)")]
    [Tooltip("残像のマテリアル。未設定なら Player/DashAfterimage (斜線) を実行時に生成する")]
    [SerializeField] private Material trailMaterial;

    [SerializeField] private Color startColor = new Color(0.7f, 0.4f, 1f, 0.85f);
    [SerializeField] private Color endColor   = new Color(0.7f, 0.4f, 1f, 0f);
    [Tooltip("残像1枚の寿命 (秒)")]
    [SerializeField, Min(0.01f)] private float lifeTime = 0.9f;
    [Tooltip("残像を落とす間隔 (秒)。小さいほど密な残像")]
    [SerializeField, Min(0.01f)] private float interval = 0.09f;
    [Tooltip("残像メッシュの最大保持数の下限。実際は「パーツ数×同時スナップショット数」" +
        "との大きい方が使われる (足りないと残像のパーツが欠ける)")]
    [SerializeField, Min(1)] private int maxTrailCount = 80;
    [Tooltip("回避終了後も放出を続ける時間 (秒)。残った勢いの間も残像が続く")]
    [SerializeField, Min(0f)] private float emitTail = 0.35f;

    [Header("紙吹雪パーティクル")]
    [Tooltip("移動1mあたりの発生数。0で無効")]
    [SerializeField, Min(0f)] private float particlesPerMeter = 16f;
    [Tooltip("紙吹雪の色")]
    [SerializeField] private Color particleColor = new Color(1f, 0.18f, 0.22f, 1f);

    private SkinnedMeshTrailRenderer trail;
    private Material runtimeMaterial;
    private ParticleSystem particles;
    private Material particleMaterial;
    private Texture2D particleTexture;
    private Coroutine tailRoutine;

    /// <summary>プレイヤーに装着済みの本コンポーネントを返す。無ければ追加する。</summary>
    public static PlayerDodgeTrail Ensure(GameObject playerGO) {
        var t = playerGO.GetComponent<PlayerDodgeTrail>();
        if (t == null) t = playerGO.AddComponent<PlayerDodgeTrail>();
        return t;
    }

    public void SetEmitting(bool on) {
        if (on) {
            if (tailRoutine != null) { StopCoroutine(tailRoutine); tailRoutine = null; }
            EnsureTrailRenderer();
            EnsureParticles();
            trail.IsEmitting = true;
            SetParticleEmission(true);
        } else {
            // 勢いが残っている間 (emitTail 秒) は出し続けてから止める
            if (tailRoutine != null) StopCoroutine(tailRoutine);
            if (isActiveAndEnabled) tailRoutine = StartCoroutine(StopAfterTail());
            else StopNow();
        }
    }

    private IEnumerator StopAfterTail() {
        yield return new WaitForSeconds(emitTail);
        StopNow();
        tailRoutine = null;
    }

    private void StopNow() {
        if (trail != null) trail.IsEmitting = false;
        SetParticleEmission(false);
    }

    private void SetParticleEmission(bool on) {
        if (particles == null) return;
        var em = particles.emission;
        em.enabled = on;
        if (on && !particles.isPlaying) particles.Play();
    }

    // 赤い紙吹雪 (小さな四角) を移動距離ベースで散らすパーティクルを実行時構築
    private void EnsureParticles() {
        if (particles != null || particlesPerMeter <= 0f) return;

        var go = new GameObject("DodgeConfetti");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.6f, 0f);   // 胴体の高さ
        particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 軌跡に残る
        // プレイヤーは CharacterController 移動 (Rigidbody 速度は0) のため、
        // 距離ベース発生の移動検知は Transform 基準にする
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        // 寿命は視認性優先 (短すぎると赤い粒が出ていないように見える)。
        // サイズは大小ランダム (一律だと大粒が目立ちすぎる)
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.13f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.25f;
        main.maxParticles    = 200;
        main.startColor      = new ParticleSystem.MinMaxGradient(
            particleColor, new Color(0.85f, 0.08f, 0.16f, 1f));

        var em = particles.emission;
        em.rateOverTime     = 0f;
        em.rateOverDistance = particlesPerMeter;
        em.enabled = false;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        var rot = particles.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);   // くるくる回る紙吹雪

        // 拡縮: 出現時に膨らみ→中盤キープ→縮みながら消える
        var sol = particles.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.15f, 1f),
            new Keyframe(0.55f, 0.8f), new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var col = particles.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        Shader ps = Shader.Find("Sprites/Default");         // Always Included 済み
        if (ps != null) {
            particleMaterial = new Material(ps) { name = "M_DodgeConfetti (Auto)" };
            particleTexture = MakeCircleTexture();          // 丸い粒子にする
            particleMaterial.mainTexture = particleTexture;
            pr.sharedMaterial = particleMaterial;
        }
    }

    // 芯の詰まった丸ドットのテクスチャを生成 (粒子っぽい見た目用)。
    // 全体を柔らかくぼかすと遠目で薄れて見えなくなるため、中心は不透明のまま
    // 縁だけ短くフェードさせる (係数3 = 縁の外側1/3だけソフト)。
    private static Texture2D MakeCircleTexture() {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) {
            name = "T_DodgeParticle (Auto)",
            wrapMode = TextureWrapMode.Clamp,
        };
        float half = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++) {
            for (int x = 0; x < S; x++) {
                float dx = (x - half) / half, dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - d) * 3f);   // 芯は不透明、縁だけソフト
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        return tex;
    }

    private void EnsureTrailRenderer() {
        if (trail == null) {
            trail = GetComponent<SkinnedMeshTrailRenderer>();
            if (trail == null) trail = gameObject.AddComponent<SkinnedMeshTrailRenderer>();
        }

        Material mat = trailMaterial;
        if (mat == null) {
            if (runtimeMaterial == null) runtimeMaterial = CreateDefaultMaterial();
            mat = runtimeMaterial;
        }

        // 着せ替えでレンダラーが入れ替わるため毎回収集し直す (非アクティブは除外)
        var sources = GetComponentsInChildren<SkinnedMeshRenderer>(false);

        // 1スナップショット=結合1枚なので、上限は「同時に生きるスナップショット数」でよい
        int snapshots = Mathf.CeilToInt(lifeTime / Mathf.Max(0.01f, interval)) + 2;

        trail.Configure(sources, mat, startColor, endColor, lifeTime, interval,
                        Mathf.Max(maxTrailCount, snapshots));
    }

    // 既定は斜線ストライプの残像シェーダー (Player/DashAfterimage)。
    // Resources のマテリアル経由で参照する (Shader.Find だけだとビルドで
    // シェーダーが削除されるため)。
    // 残像の色・フェードは SkinnedMeshTrailRenderer が _BaseColor へ毎フレーム書き込む。
    private Material CreateDefaultMaterial() {
        Material stripesBase = Resources.Load<Material>("M_DashAfterimage");
        if (stripesBase != null) return new Material(stripesBase) { name = "M_DodgeTrail (Auto)" };

        Shader stripes = Shader.Find("Player/DashAfterimage");
        if (stripes != null) return new Material(stripes) { name = "M_DodgeTrail (Auto)" };

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var mat = new Material(sh) { name = "M_DodgeTrail (Auto)" };
        if (mat.HasProperty("_Surface")) {
            mat.SetFloat("_Surface", 1f);                                   // Transparent
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        return mat;
    }

    private void OnDestroy() {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (particleMaterial != null) Destroy(particleMaterial);
        if (particleTexture != null) Destroy(particleTexture);
    }
}
