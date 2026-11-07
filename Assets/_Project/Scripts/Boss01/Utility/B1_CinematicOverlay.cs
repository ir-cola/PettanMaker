using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// カメラ演出中に前面へ重ねる UI。
/// B1_CameraSceneSettings.UISettings に従って、ボス名 (TMP) / 集中線 /
/// 歪みオーバーレイを実行時に生成し、テキストエフェクト(傾き/タイプライター/
/// ポップイン/シェイク/ゆらぎ)を個別 ON/OFF で適用する。
///
/// [ExecuteAlways] + EditorApplication.update により、Edit モードのカメラ
/// プレビュー中でもエフェクトが動作する。時間は Play=unscaledDeltaTime /
/// Edit=realtime 差分。フェード表示は PlayTimed の状態機械で駆動(コルーチン非依存)。
/// フォントや歪みマテリアルなど参照が未設定の要素は自動でスキップする。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class B1_CinematicOverlay : MonoBehaviour
{
    private CanvasGroup group;

    // タイトルエフェクト用の参照/状態
    private B1_CameraSceneSettings.UISettings ui;
    private RectTransform titleRect;
    private RectTransform titleEffectRect;
    private Graphic titleGraphic;
    private TextMeshProUGUI titleTMP;
    private float baseTiltDeg;
    private int totalChars;
    private bool began;
    private float effectTime;
    private float popTimer;
    private float typeTimer;

    // 自己タイマー表示(PlayTimed)の状態機械
    private bool timed;
    private int timedPhase;   // 0=フェードイン, 1=保持, 2=フェードアウト
    private float timedTimer;
    private float timedFade;
    private float timedHold;

    private float lastRealtime;

    /// <summary>
    /// UI を生成する。表示要素が一つも無ければ null を返す。
    /// </summary>
    public static B1_CinematicOverlay Create(B1_CameraSceneSettings.UISettings s)
    {
        if (s == null) return null;
        bool wantText   = s.showBossName &&
                          (s.bossNameTexture != null || !string.IsNullOrEmpty(s.bossName));
        bool wantLines  = s.showConvergenceLines && s.convergenceLineCount > 0;
        bool wantWarp   = s.showDistortionOverlay && s.distortionOverlayMaterial != null;
        if (!wantText && !wantLines && !wantWarp) return null;

        var go = new GameObject("B1_CinematicOverlay");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト。シーンに保存しない
        var self = go.AddComponent<B1_CinematicOverlay>();
        self.Build(s, wantText, wantLines, wantWarp);
        return self;
    }

    private void Build(B1_CameraSceneSettings.UISettings s, bool wantText, bool wantLines, bool wantWarp)
    {
        ui = s;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // シーン UI より前・遷移演出(32760)より後ろ

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>().enabled = false;

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // 背面から順に: 歪みオーバーレイ → 集中線 → ボス名
        if (wantWarp)  BuildDistortion(s);
        if (wantLines) BuildConvergenceLines(s);
        if (wantText)  BuildTitle(s);
    }

    private void BuildDistortion(B1_CameraSceneSettings.UISettings s)
    {
        var go = new GameObject("Distortion", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);
        Stretch((RectTransform)go.transform);
        var img = go.GetComponent<Image>();
        img.material = s.distortionOverlayMaterial;
        img.color = Color.white;
        img.raycastTarget = false;
    }

    private void BuildConvergenceLines(B1_CameraSceneSettings.UISettings s)
    {
        var container = new GameObject("ConvergenceLines", typeof(RectTransform));
        container.transform.SetParent(transform, false);
        var crt = (RectTransform)container.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;

        // 参照解像度基準で対角長を求め、画面外まで届く長さにする。
        float w = 1920f, h = 1080f;
        float diagonal = Mathf.Sqrt(w * w + h * h);
        float innerRadius = h * 0.16f; // 中央は空けて集中線らしく
        float length = diagonal * 0.5f + innerRadius;
        float thickness = Mathf.Max(2f, w * 0.004f);

        int count = Mathf.Clamp(s.convergenceLineCount, 1, 256);
        for (int i = 0; i < count; i++)
        {
            float angle = 360f * i / count;
            var line = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            line.transform.SetParent(container.transform, false);
            var rt = (RectTransform)line.transform;
            rt.pivot = new Vector2(0.5f, 0f);          // 下端(=内側)を基準に外へ伸ばす
            rt.sizeDelta = new Vector2(thickness, length);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            // 内側を innerRadius だけ空けるため、回転方向へオフセット
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
            rt.anchoredPosition = dir * innerRadius;

            var img = line.GetComponent<Image>();
            img.color = s.convergenceLineColor;
            img.raycastTarget = false;
        }
    }

    private void BuildTitle(B1_CameraSceneSettings.UISettings s)
    {
        var go = new GameObject("BossName", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        titleRect = (RectTransform)go.transform;
        // 位置/サイズは UISettings で調整（ビューポート中心 titleAnchor + 領域サイズ）。
        Vector2 center = s.titleAnchor;
        float halfW = Mathf.Clamp01(s.titleAreaWidth) * 0.5f;
        float halfH = Mathf.Clamp01(s.titleAreaHeight) * 0.5f;
        titleRect.anchorMin = new Vector2(center.x - halfW, center.y - halfH);
        titleRect.anchorMax = new Vector2(center.x + halfW, center.y + halfH);
        titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(titleRect, false);
        titleEffectRect = (RectTransform)content.transform;
        Stretch(titleEffectRect);
        baseTiltDeg = s.randomTilt ? Random.Range(-s.randomTiltAngle, s.randomTiltAngle) : 0f;
        titleEffectRect.localScale = new Vector3(s.titleScale.x, s.titleScale.y, 1f);
        titleEffectRect.localRotation = Quaternion.Euler(0f, 0f, s.titleRotation + baseTiltDeg);

        if (s.bossNameTexture != null)
        {
            var raw = content.AddComponent<RawImage>();
            raw.texture = s.bossNameTexture;
            raw.color = s.stickerColor;
            raw.raycastTarget = false;
            titleGraphic = raw;

            if (s.preserveStickerAspect && s.bossNameTexture.height > 0)
            {
                var fitter = content.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = (float)s.bossNameTexture.width / s.bossNameTexture.height;
            }
            return;
        }

        var tmp = content.AddComponent<TextMeshProUGUI>();
        titleTMP = tmp;
        tmp.text = s.bossName;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        // タイプライター: 文字数を数え、表示開始まで隠す。
        tmp.ForceMeshUpdate();
        totalChars = tmp.textInfo.characterCount;
        if (s.typewriter) tmp.maxVisibleCharacters = 0;
    }

    /// <summary>
    /// 表示開始トリガー。ポップイン/タイプライターなど「出す瞬間」のエフェクトを起動する。
    /// フェードインの直前に呼ぶ。
    /// </summary>
    public void Begin()
    {
        if (began) return;
        began = true;
        effectTime = 0f;
        popTimer = 0f;
        typeTimer = 0f;
        if (ui != null && ui.typewriter && titleTMP != null)
            titleTMP.maxVisibleCharacters = 0;

        if (Application.isPlaying && ui != null && ui.pasteBossNameTexture && titleGraphic != null)
        {
            // RawImage の既定表示が貼付マテリアル/開始位置より先に描かれると青く瞬くため、
            // UIStickerPaste が初期化を完了するまで描画しない。
            titleGraphic.enabled = false;
            StartCoroutine(UIStickerPaste.PasteLive(
                titleGraphic,
                titleRect,
                titleRect.localPosition,
                ui.stickerFromDirection,
                new UIStickerPaste.Options
                {
                    duration = ui.stickerPasteDuration,
                    holdPeel = ui.stickerPeel,
                    overshoot = ui.stickerOvershoot,
                    tiltDeg = ui.stickerTilt,
                    materialTemplate = ui.bossNameStickerMaterial,
                    shimmerColor = ui.stickerColor,
                    useUnscaledTime = true,
                    settleEffect = ui.stickerSettleEffect,
                    settleDuration = ui.stickerSettleDuration,
                    settleScale = ui.stickerSettleScale,
                    settleShake = ui.stickerSettleShake,
                    onPrepared = () =>
                    {
                        if (titleGraphic != null) titleGraphic.enabled = true;
                    },
                }));
        }
    }

    /// <summary>
    /// カメラ演出から独立して、フェードイン→duration 保持→フェードアウトして自壊する。
    /// 呼び出し側は所有権を手放し、Dispose を呼ばないこと。コルーチン非依存。
    /// </summary>
    public void PlayTimed(float duration, float fade)
    {
        Begin();
        timedHold = Mathf.Max(0f, duration);
        timedFade = Mathf.Max(0f, fade);
        timedTimer = 0f;
        timedPhase = 0;
        timed = true;
        SetAlpha(0f);
    }

    // 毎フレームの駆動。Play=Update / Edit=EditorUpdate から dt を渡して呼ぶ。
    private void Tick(float dt)
    {
        // テキストエフェクト
        if (began && titleEffectRect != null && ui != null)
        {
            effectTime += dt;

            // ポップイン(オーバーシュートして 1 に収束)
            float scale = 1f;
            if (ui.popIn && titleGraphic == null && popTimer < ui.popInDuration)
            {
                popTimer += dt;
                float t = Mathf.Clamp01(popTimer / Mathf.Max(0.01f, ui.popInDuration));
                scale = Mathf.LerpUnclamped(ui.popInScale, 1f, EaseOutBack(t));
            }

            // 傾き + ゆらぎ(回転)
            float rot = ui.titleRotation + baseTiltDeg;
            if (ui.wobble)
                rot += Mathf.Sin(effectTime * ui.wobbleSpeed * Mathf.PI * 2f) * ui.wobbleAngle;

            // シェイク(位置) + ピクセルオフセット(位置調整)
            Vector2 pos = ui.shake ? Random.insideUnitCircle * ui.shakeIntensity : Vector2.zero;
            pos += ui.titlePixelOffset;

            titleEffectRect.localScale = new Vector3(ui.titleScale.x * scale, ui.titleScale.y * scale, 1f);
            titleEffectRect.localRotation = Quaternion.Euler(0f, 0f, rot);
            titleEffectRect.anchoredPosition = pos;

            // タイプライター(1文字ずつ表示)
            if (ui.typewriter && titleTMP != null && totalChars > 0)
            {
                typeTimer += dt;
                int reveal = Mathf.FloorToInt(typeTimer * Mathf.Max(1f, ui.typewriterCharsPerSecond));
                titleTMP.maxVisibleCharacters = Mathf.Clamp(reveal, 0, totalChars);
            }
        }

        // 自己タイマー表示(フェードイン→保持→フェードアウト→自壊)
        if (timed)
        {
            timedTimer += dt;
            switch (timedPhase)
            {
                case 0:
                    SetAlpha(timedFade > 0f ? Mathf.Clamp01(timedTimer / timedFade) : 1f);
                    if (timedTimer >= timedFade) { SetAlpha(1f); timedPhase = 1; timedTimer = 0f; }
                    break;
                case 1:
                    SetAlpha(1f);
                    if (timedTimer >= timedHold) { timedPhase = 2; timedTimer = 0f; }
                    break;
                default:
                    SetAlpha(timedFade > 0f ? 1f - Mathf.Clamp01(timedTimer / timedFade) : 0f);
                    if (timedTimer >= timedFade) { SetAlpha(0f); timed = false; Dispose(); }
                    break;
            }
        }
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

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>0〜1 で表示アルファを設定する。</summary>
    public void SetAlpha(float a)
    {
        if (group != null) group.alpha = Mathf.Clamp01(a);
    }

    /// <summary>破棄する。</summary>
    public void Dispose()
    {
        if (this != null && gameObject != null) B1_CinematicUtil.SafeDestroy(gameObject);
    }
}

/// <summary>
/// カメラ演出中だけ表示する上下のシネマティック黒帯。
/// 画面端から滑らかに開閉し、内側へ細いアクセントラインを重ねる。
/// </summary>
public sealed class B1_CinematicLetterbox
{
    private const float ReferenceHeight = 1080f;
    private const int SortingOrder = 29900;

    private readonly B1_CameraSceneSettings.LetterboxSettings settings;
    private readonly GameObject root;
    private readonly RectTransform topBar;
    private readonly RectTransform bottomBar;
    private readonly RectTransform topAccent;
    private readonly RectTransform bottomAccent;
    private float progress;
    private float target;
    private bool disposeWhenHidden;

    public bool IsDisposed { get; private set; }

    public static B1_CinematicLetterbox Create(B1_CameraSceneSettings.LetterboxSettings settings)
    {
        return settings == null || !settings.enabled || settings.barHeight <= 0f
            ? null
            : new B1_CinematicLetterbox(settings);
    }

    private B1_CinematicLetterbox(B1_CameraSceneSettings.LetterboxSettings settings)
    {
        this.settings = settings;
        root = new GameObject("B1_CinematicLetterbox") { hideFlags = HideFlags.DontSave };

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        root.AddComponent<GraphicRaycaster>().enabled = false;
        topBar = BuildBar("LetterboxTop", new Vector2(0.5f, 1f), out topAccent);
        bottomBar = BuildBar("LetterboxBottom", new Vector2(0.5f, 0f), out bottomAccent);
        Apply();
        Show();
    }

    private RectTransform BuildBar(string objectName, Vector2 edge, out RectTransform accent)
    {
        var barObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barObject.transform.SetParent(root.transform, false);

        var bar = (RectTransform)barObject.transform;
        bar.anchorMin = new Vector2(0f, edge.y);
        bar.anchorMax = new Vector2(1f, edge.y);
        bar.pivot = edge;
        bar.sizeDelta = Vector2.zero;
        bar.anchoredPosition = Vector2.zero;

        var barImage = barObject.GetComponent<Image>();
        barImage.color = settings.barColor;
        barImage.raycastTarget = false;

        accent = null;
        if (!settings.showAccentLine || settings.accentThickness <= 0f)
            return bar;

        var accentObject = new GameObject("GoldAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accentObject.transform.SetParent(bar, false);
        accent = (RectTransform)accentObject.transform;
        float innerEdge = edge.y > 0.5f ? 0f : 1f;
        accent.anchorMin = new Vector2(0f, innerEdge);
        accent.anchorMax = new Vector2(1f, innerEdge);
        accent.pivot = new Vector2(0.5f, innerEdge);
        accent.sizeDelta = Vector2.zero;
        accent.anchoredPosition = Vector2.zero;

        var accentImage = accentObject.GetComponent<Image>();
        accentImage.color = settings.accentColor;
        accentImage.raycastTarget = false;
        return bar;
    }

    private void Show()
    {
        disposeWhenHidden = false;
        target = 1f;
    }

    public void HideAndDispose()
    {
        target = 0f;
        disposeWhenHidden = true;
        if (settings.exitDuration <= 0f)
            Dispose();
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (IsDisposed || Mathf.Approximately(progress, target))
            return;

        float duration = target > progress ? settings.enterDuration : settings.exitDuration;
        progress = duration > 0f
            ? Mathf.MoveTowards(progress, target, unscaledDeltaTime / duration)
            : target;
        Apply();

        if (disposeWhenHidden && progress <= 0f)
            Dispose();
    }

    private void Apply()
    {
        float eased = progress * progress * (3f - 2f * progress);
        float height = ReferenceHeight * Mathf.Clamp(settings.barHeight, 0f, 0.35f) * eased;
        float accentHeight = Mathf.Max(0f, settings.accentThickness) * eased;

        if (topBar != null) topBar.sizeDelta = new Vector2(0f, height);
        if (bottomBar != null) bottomBar.sizeDelta = new Vector2(0f, height);
        if (topAccent != null) topAccent.sizeDelta = new Vector2(0f, accentHeight);
        if (bottomAccent != null) bottomAccent.sizeDelta = new Vector2(0f, accentHeight);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (root != null) B1_CinematicUtil.SafeDestroy(root);
    }
}
