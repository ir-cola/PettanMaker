using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// カメラ前面に一時的なフルスクリーン Quad を重ね、背後のシーンカラーを一瞬だけ
/// 色抜き（彩度ダウン＋わずかな減光/ティント）する。Phase2 Roar 直前の「色を一瞬抜く」演出用。
///
/// drainIn → hold → release のエンベロープで _Amount を 0→peak→0 に動かし、尺が終わると自壊する。
/// Quad は Camera.main の子にして毎フレーム near プレーン手前へフルフレームで貼り直す（FOV/アスペクト変化に追従）。
/// [ExecuteAlways] + EditorApplication.update により Edit モードのプレビューでも動く。
/// B1_ShockwaveDistortion と同じ自己完結・自己破棄パターン。描画には URP の Opaque Texture が必要。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class B1_ScreenColorDrain : MonoBehaviour
{
    /// <summary>1回ぶんの色抜きパラメータ。</summary>
    public struct Params
    {
        public float drainIn;         // 色が抜けきるまでの時間(秒)
        public float hold;            // 抜けた状態を保持する時間(秒)
        public float release;         // 元の色へ戻るまでの時間(秒)
        public float peak;            // 最大色抜き量(_Amount, 0..1)
        public float maxDesaturation; // 彩度ダウンの強さ(0..1)
        public float darken;          // 減光(0..1)
        public Color tint;            // 乗算ティント
        public float tintAmount;      // ティントの適用量(0..1)

        public static Params Default => new Params
        {
            drainIn = 0.08f, hold = 0.12f, release = 0.35f,
            peak = 1f, maxDesaturation = 1f, darken = 0.12f,
            tint = new Color(0.75f, 0.72f, 0.85f, 1f), tintAmount = 0.25f,
        };
    }

    private const string ShaderName = "PettanMaker/Boss_01/ScreenColorDrain";
    private static readonly int AmountID     = Shader.PropertyToID("_Amount");
    private static readonly int MaxDesatID   = Shader.PropertyToID("_MaxDesaturation");
    private static readonly int DarkenID     = Shader.PropertyToID("_Darken");
    private static readonly int TintID       = Shader.PropertyToID("_Tint");
    private static readonly int TintAmountID = Shader.PropertyToID("_TintAmount");

    private Material mat;
    private Params p;
    private float elapsed;
    private float total;
    private Camera cam;
    private float lastRealtime;

    /// <summary>色抜きを1回発生させる。シェーダ/カメラが見つからなければ null。</summary>
    public static B1_ScreenColorDrain Spawn(in Params p)
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning("[B1_ScreenColorDrain] Shader not found; color drain skipped.");
            return null;
        }
        var main = Camera.main;
        if (main == null)
        {
            Debug.LogWarning("[B1_ScreenColorDrain] Camera.main not found; color drain skipped.");
            return null;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "B1_ScreenColorDrain";
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト
        var col = go.GetComponent<Collider>();
        if (col != null) B1_CinematicUtil.SafeDestroy(col);

        var self = go.AddComponent<B1_ScreenColorDrain>();
        self.Init(shader, main, p);
        return self;
    }

    private void Init(Shader shader, Camera camera, in Params src)
    {
        p = src;
        p.drainIn = Mathf.Max(0f, p.drainIn);
        p.hold    = Mathf.Max(0f, p.hold);
        p.release = Mathf.Max(0.01f, p.release);
        p.peak    = Mathf.Clamp01(p.peak <= 0f ? 1f : p.peak);
        total     = p.drainIn + p.hold + p.release;
        cam       = camera;

        mat = new Material(shader);
        mat.SetFloat(AmountID, 0f);
        mat.SetFloat(MaxDesatID, Mathf.Clamp01(p.maxDesaturation));
        mat.SetFloat(DarkenID, Mathf.Clamp01(p.darken));
        mat.SetColor(TintID, p.tint);
        mat.SetFloat(TintAmountID, Mathf.Clamp01(p.tintAmount));

        var mr = GetComponent<MeshRenderer>();
        mr.sharedMaterial     = mat;
        mr.shadowCastingMode  = ShadowCastingMode.Off;
        mr.receiveShadows     = false;
        mr.lightProbeUsage    = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        elapsed = 0f;
        FitToCamera();
    }

    // カメラの near プレーン手前へ、フルフレームを覆うように Quad を配置し直す。
    private void FitToCamera()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        var t = transform;
        t.SetParent(cam.transform, false);

        float dist = cam.nearClipPlane + 0.05f;
        t.localPosition = new Vector3(0f, 0f, dist);
        t.localRotation = Quaternion.identity;

        float h = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        // 端の欠けを防ぐ余白を少し足す。
        t.localScale = new Vector3(w * 1.1f, h * 1.1f, 1f);
    }

    private void Tick(float dt)
    {
        if (mat == null) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        FitToCamera(); // FOV/アスペクトが変わっても常にフルフレームを維持

        elapsed += dt;
        mat.SetFloat(AmountID, AmountAt(elapsed));

        if (elapsed >= total) Kill();
    }

    // drainIn(立ち上げ) → hold(保持) → release(戻し) の 0→peak→0 エンベロープ。
    private float AmountAt(float t)
    {
        if (t < p.drainIn)
            return p.peak * (p.drainIn > 0f ? Mathf.SmoothStep(0f, 1f, t / p.drainIn) : 1f);
        t -= p.drainIn;
        if (t < p.hold) return p.peak;
        t -= p.hold;
        return p.peak * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / p.release)));
    }

    private void Update()
    {
        if (!Application.isPlaying) return; // Edit は EditorUpdate 駆動
        Tick(Time.unscaledDeltaTime);
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            lastRealtime = Time.realtimeSinceStartup;
            UnityEditor.EditorApplication.update += EditorUpdate;
        }
    }

    private void OnDisable()
    {
        UnityEditor.EditorApplication.update -= EditorUpdate;
    }

    private void EditorUpdate()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= EditorUpdate; return; }
        if (Application.isPlaying) return;

        float now = Time.realtimeSinceStartup;
        float dt = Mathf.Clamp(now - lastRealtime, 0f, 0.1f);
        lastRealtime = now;
        Tick(dt);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
#endif

    /// <summary>演出が途中で中断されても確実に消すための強制破棄。</summary>
    public void Kill()
    {
        if (mat != null) { B1_CinematicUtil.SafeDestroy(mat); mat = null; }
        B1_CinematicUtil.SafeDestroy(gameObject);
    }

    private void OnDestroy()
    {
        if (mat != null) { B1_CinematicUtil.SafeDestroy(mat); mat = null; }
    }
}
