using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背景Spriteと前景Spriteを重ねて表示するBoss01専用HPゲージ。
/// 前景は縮小せずRectMask2Dで右側をクリップするため、模様や縁の太さを保ったまま減少する。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_HealthUI : MonoBehaviour
{
    private const int SortingOrder = 20000;
    private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

    private B1_Health health;
    private Sprite backgroundSprite;
    private Sprite fillSprite;
    private Vector2 gaugeSize;
    private float marginTop;
    private float damageTrailDelay;
    private float damageTrailDuration;
    private Color damageTrailColor;
    private Font labelFont;
    private string labelText;
    private int labelFontSize;
    private Color labelColor;
    private FontStyle labelFontStyle;
    private float labelSpacing;
    private Vector2 fillAnchorMin = Vector2.zero;
    private Vector2 fillAnchorMax = Vector2.one;

    private GameObject canvasObject;
    private RectTransform gaugeRect;
    private CanvasGroup gaugeGroup;
    private RectTransform damageTrailViewport;
    private RectTransform fillViewport;
    private Image backgroundImage;
    private Image damageTrailImage;
    private Image fillImage;
    private Text label;
    private bool subscribed;
    private Coroutine damageTrailRoutine;
    private float damageTrailRatio;
    private Coroutine stickerRoutine;
    private Graphic stickerGraphic;
    private Material stickerBaseMaterial;
    private Vector3 stickerHomePosition;
    private Vector3 stickerHomeScale;
    private Quaternion stickerHomeRotation;
    private bool stickerPrepared;
    private bool stickerPlaying;

    public void Configure(
        B1_Health source,
        Sprite background,
        Sprite fill,
        Vector2 size,
        float topMargin,
        float trailDelay,
        float trailDuration,
        Color trailColor,
        Font font,
        string text,
        int fontSize,
        Color fontColor,
        FontStyle fontStyle,
        float spacing)
    {
        Unsubscribe();
        health = source;
        backgroundSprite = background;
        fillSprite = fill;
        gaugeSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        marginTop = Mathf.Max(0f, topMargin);
        damageTrailDelay = Mathf.Max(0f, trailDelay);
        damageTrailDuration = Mathf.Max(0.01f, trailDuration);
        damageTrailColor = trailColor;
        labelFont = font;
        labelText = text;
        labelFontSize = Mathf.Max(1, fontSize);
        labelColor = fontColor;
        labelFontStyle = fontStyle;
        labelSpacing = Mathf.Max(0f, spacing);

        BuildIfNeeded();
        ApplyLayout();
        Subscribe();
        Refresh(health != null ? health.HpRatio : 0f, true);
    }

    private void BuildIfNeeded()
    {
        if (canvasObject != null) return;

        canvasObject = new GameObject("Boss01_HealthGaugeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var gauge = new GameObject("Gauge", typeof(RectTransform), typeof(CanvasGroup));
        gauge.transform.SetParent(canvasObject.transform, false);
        gaugeRect = (RectTransform)gauge.transform;
        gaugeGroup = gauge.GetComponent<CanvasGroup>();
        gaugeRect.anchorMin = gaugeRect.anchorMax = new Vector2(0.5f, 1f);
        gaugeRect.pivot = new Vector2(0.5f, 1f);

        backgroundImage = CreateImage("Background", gaugeRect);

        damageTrailViewport = CreateFillViewport("DamageTrailViewport", gaugeRect, out damageTrailImage);

        fillViewport = CreateFillViewport("FillViewport", gaugeRect, out fillImage);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(gaugeRect, false);
        label = labelObject.GetComponent<Text>();
        label.raycastTarget = false;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private static RectTransform CreateFillViewport(string objectName, Transform parent, out Image image)
    {
        var viewport = new GameObject(objectName, typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(parent, false);
        var viewportRect = (RectTransform)viewport.transform;
        viewportRect.pivot = new Vector2(0f, 0.5f);

        image = CreateImage(objectName + "Fill", viewportRect);
        image.rectTransform.anchorMin = new Vector2(0f, 0f);
        image.rectTransform.anchorMax = new Vector2(0f, 1f);
        image.rectTransform.pivot = new Vector2(0f, 0.5f);
        image.rectTransform.anchoredPosition = Vector2.zero;
        return viewportRect;
    }

    private static Image CreateImage(string objectName, Transform parent)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void ApplyLayout()
    {
        if (gaugeRect == null) return;

        Vector2 backgroundSize = backgroundSprite != null ? backgroundSprite.rect.size : gaugeSize;
        Vector2 fillSize = fillSprite != null ? fillSprite.rect.size : backgroundSize;
        bool hasLabel = !string.IsNullOrWhiteSpace(labelText);
        float labelHeight = hasLabel ? Mathf.Ceil(labelFontSize * 1.2f) : 0f;
        float spacing = hasLabel ? labelSpacing : 0f;

        gaugeRect.sizeDelta = backgroundSize;
        gaugeRect.anchoredPosition = new Vector2(0f, -(marginTop + labelHeight + spacing));

        var bgRect = backgroundImage.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;

        CalculateFillBounds(backgroundSize, fillSize);
        damageTrailViewport.anchorMin = fillAnchorMin;
        damageTrailViewport.anchorMax = fillAnchorMax;
        damageTrailViewport.offsetMin = damageTrailViewport.offsetMax = Vector2.zero;
        fillViewport.anchorMin = fillAnchorMin;
        fillViewport.anchorMax = fillAnchorMax;
        fillViewport.offsetMin = fillViewport.offsetMax = Vector2.zero;

        damageTrailImage.rectTransform.sizeDelta = new Vector2(fillSize.x, 0f);
        fillImage.rectTransform.sizeDelta = new Vector2(fillSize.x, 0f);
        backgroundImage.sprite = backgroundSprite;
        damageTrailImage.sprite = fillSprite;
        fillImage.sprite = fillSprite;
        backgroundImage.enabled = backgroundSprite != null;
        damageTrailImage.enabled = fillSprite != null;
        fillImage.enabled = fillSprite != null;
        damageTrailImage.color = damageTrailColor;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, spacing);
        labelRect.sizeDelta = new Vector2(0f, labelHeight);
        label.font = labelFont;
        label.text = labelText;
        label.fontSize = labelFontSize;
        label.color = labelColor;
        label.fontStyle = labelFontStyle;
        label.enabled = hasLabel && labelFont != null;
    }

    /// <summary>
    /// Entrance 終了までゲージを隠し、シール貼付の開始姿勢を準備する。
    /// 通常戦闘や Entrance 以外では呼ばれないため、従来どおり即時表示される。
    /// </summary>
    public void PrepareStickerEntrance()
    {
        BuildIfNeeded();
        if (gaugeRect == null || gaugeGroup == null) return;

        CancelStickerPaste();
        CacheStickerHomePose();
        gaugeGroup.alpha = 0f;
        stickerPrepared = true;
        stickerPlaying = false;
    }

    /// <summary>ボス名と同じ設定を使い、HPゲージ全体をシールのように貼り付ける。</summary>
    public void PlayStickerEntrance(B1_CameraSceneSettings.UISettings ui)
    {
        BuildIfNeeded();
        if (gaugeRect == null || gaugeGroup == null) return;

        CancelStickerPaste();
        CacheStickerHomePose();
        gaugeGroup.alpha = 0f;
        stickerPrepared = true;
        stickerPlaying = true;

        // 重ね構造(背景+塗り+トレイル)のゲージで角めくれ(_Peel)を使うと、めくれた部分に
        // 色バーが無く暗い裏面/透け(=黒)が出てしまう。HPゲージは前面の色バーへ接着シマーを
        // 走らせる方式にし、めくれは使わない(holdPeel=0)。スライド着地+定着バウンスで貼り付ける。
        stickerGraphic = fillImage != null && fillImage.enabled
            ? fillImage
            : backgroundImage != null && backgroundImage.enabled ? backgroundImage : null;
        stickerBaseMaterial = stickerGraphic != null ? stickerGraphic.material : null;

        var options = new UIStickerPaste.Options
        {
            duration = ui != null ? ui.healthStickerPasteDuration : 0.7f,
            holdPeel = 0f, // 角めくれ無し(重ね構造では暗い裏面が露出するため)。シマーで表現する
            overshoot = ui != null ? ui.stickerOvershoot : 1.12f,
            tiltDeg = ui != null ? ui.stickerTilt : 9f,
            materialTemplate = ui != null ? ui.bossNameStickerMaterial : null,
            shimmerColor = ui != null ? ui.stickerColor : Color.white,
            useUnscaledTime = true,
            settleEffect = ui == null || ui.stickerSettleEffect,
            settleDuration = ui != null ? ui.stickerSettleDuration : 0.28f,
            settleScale = ui != null ? ui.stickerSettleScale : 0.09f,
            settleShake = ui != null ? ui.stickerSettleShake : 7f,
            onPrepared = () =>
            {
                if (gaugeGroup != null) gaugeGroup.alpha = 1f;
            },
        };
        Vector2 fromDirection = ui != null ? ui.stickerFromDirection : new Vector2(-1f, 1f);
        stickerRoutine = StartCoroutine(PlayStickerRoutine(fromDirection, options));
    }

    /// <summary>Entrance がタイトル前に中断された場合も、HPゲージを確実に表示状態へ戻す。</summary>
    public void RevealImmediately()
    {
        CancelStickerPaste();
        if (gaugeGroup != null) gaugeGroup.alpha = 1f;
        stickerPrepared = false;
        stickerPlaying = false;
    }

    private IEnumerator PlayStickerRoutine(Vector2 fromDirection, UIStickerPaste.Options options)
    {
        yield return UIStickerPaste.PasteLive(
            stickerGraphic,
            gaugeRect,
            stickerHomePosition,
            fromDirection,
            options);

        stickerRoutine = null;
        stickerGraphic = null;
        stickerBaseMaterial = null;
        stickerPrepared = false;
        stickerPlaying = false;
        if (gaugeGroup != null) gaugeGroup.alpha = 1f;
    }

    private void CacheStickerHomePose()
    {
        stickerHomePosition = gaugeRect.localPosition;
        stickerHomeScale = gaugeRect.localScale;
        stickerHomeRotation = gaugeRect.localRotation;
    }

    private void CancelStickerPaste()
    {
        if (stickerRoutine != null)
        {
            StopCoroutine(stickerRoutine);
            stickerRoutine = null;
        }

        if (stickerGraphic != null && stickerGraphic.material != stickerBaseMaterial)
        {
            Material runtimeMaterial = stickerGraphic.material;
            stickerGraphic.material = stickerBaseMaterial;
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial);
                else DestroyImmediate(runtimeMaterial);
            }
        }

        if (gaugeRect != null && stickerPrepared)
        {
            gaugeRect.localPosition = stickerHomePosition;
            gaugeRect.localScale = stickerHomeScale;
            gaugeRect.localRotation = stickerHomeRotation;
        }

        stickerGraphic = null;
        stickerBaseMaterial = null;
        stickerPlaying = false;
    }

    private void CalculateFillBounds(Vector2 backgroundSize, Vector2 fillSize)
    {
        fillAnchorMin = Vector2.zero;
        fillAnchorMax = Vector2.one;
        if (backgroundSize.x <= 0f || backgroundSize.y <= 0f) return;

        float widthRatio = Mathf.Clamp01(fillSize.x / backgroundSize.x);
        float heightRatio = Mathf.Clamp01(fillSize.y / backgroundSize.y);
        Vector2 padding = new((1f - widthRatio) * 0.5f, (1f - heightRatio) * 0.5f);
        fillAnchorMin = padding;
        fillAnchorMax = Vector2.one - padding;
    }

    private void Subscribe()
    {
        if (health == null || subscribed) return;
        health.onHPChanged.AddListener(HandleHPChanged);
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (health != null && subscribed) health.onHPChanged.RemoveListener(HandleHPChanged);
        subscribed = false;
    }

    private void HandleHPChanged(int _)
    {
        // Entrance 演出待ちで隠したまま被弾した場合(演出スキップ等)の保険。貼付アニメ中は演出を優先する。
        if (stickerPrepared && !stickerPlaying) RevealImmediately();
        Refresh(health != null ? health.HpRatio : 0f);
    }

    private void Refresh(float ratio, bool immediateTrail = false)
    {
        if (fillViewport == null) return;
        ratio = Mathf.Clamp01(ratio);
        SetFillRatio(fillViewport, ratio);

        if (immediateTrail || ratio >= damageTrailRatio)
        {
            CancelDamageTrail();
            damageTrailRatio = ratio;
            SetFillRatio(damageTrailViewport, damageTrailRatio);
            return;
        }

        CancelDamageTrail();
        damageTrailRoutine = StartCoroutine(AnimateDamageTrail(ratio));
    }

    private IEnumerator AnimateDamageTrail(float targetRatio)
    {
        if (damageTrailDelay > 0f) yield return new WaitForSecondsRealtime(damageTrailDelay);

        float startRatio = damageTrailRatio;
        float elapsed = 0f;
        while (elapsed < damageTrailDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / damageTrailDuration);
            damageTrailRatio = Mathf.Lerp(startRatio, targetRatio, Mathf.SmoothStep(0f, 1f, t));
            SetFillRatio(damageTrailViewport, damageTrailRatio);
            yield return null;
        }

        damageTrailRatio = targetRatio;
        SetFillRatio(damageTrailViewport, damageTrailRatio);
        damageTrailRoutine = null;
    }

    private void SetFillRatio(RectTransform viewport, float ratio)
    {
        if (viewport == null) return;
        Vector2 max = viewport.anchorMax;
        max.x = Mathf.Lerp(fillAnchorMin.x, fillAnchorMax.x, Mathf.Clamp01(ratio));
        viewport.anchorMax = max;
    }

    private void CancelDamageTrail()
    {
        if (damageTrailRoutine == null) return;
        StopCoroutine(damageTrailRoutine);
        damageTrailRoutine = null;
    }

    private void OnDestroy()
    {
        CancelDamageTrail();
        CancelStickerPaste();
        Unsubscribe();
    }
}
