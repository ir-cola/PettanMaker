/**===========================================================================

 File   : シールページビュークラス [ClothesSwipeUI.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/  (2026/7/8 リメイク：汎用ページビュー化)
 -----------------------------------------------------------------------------

 シールを 2×2 の4枠ページで横スワイプ選択する汎用ビュー。
   ・マウスドラッグで左右スクロール（慣性＋スナップ）
   ・ページ下部にドットインジケーター（on/off スプライト）
   ・CHANGE はハンガー枠付き、ITEM/SKILL は素貼りセル
   ・セルクリックで OnSelected を通知（StickerBookUI が説明文を更新）
   ・セル／ページ／ドットはコードで動的生成（プレハブ非依存）

 左ページのビュー用オブジェクトにアタッチすること（Viewport の祖先で、
 ドラッグイベントを受け取れる位置）。

===========================================================================**/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/**===========================================================================
  シールページビュークラス
===========================================================================**/
public class ClothesSwipeUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // =========================================================================
    //  Inspector
    // =========================================================================

    [Header("ビューポート／スクロール")]
    [Tooltip("表示領域（Mask 付き）。ページ幅の基準にもなる")]
    [SerializeField] private RectTransform viewport;
    [Tooltip("横に並ぶページを収めるコンテナ（viewport の子）")]
    [SerializeField] private RectTransform scrollContainer;

    [Header("ページドットインジケーター")]
    [SerializeField] private Transform  dotParent;
    [SerializeField] private Sprite     dotOnSprite;
    [SerializeField] private Sprite     dotOffSprite;
    [SerializeField] private Vector2    dotOnSize  = new Vector2(30f, 30f);
    [SerializeField] private Vector2    dotOffSize = new Vector2(20f, 20f);

    [Header("セル配置（2×2）")]
    [Tooltip("ハンガー枠スプライト（CHANGE 用）")]
    [SerializeField] private Sprite  hangerSprite;
    [SerializeField] private Vector2 cellSize    = new Vector2(250f, 360f);
    [SerializeField] private Vector2 cellSpacing = new Vector2(70f, 40f);
    [Tooltip("グリッド全体の縦オフセット（+で上へ。下部のドット分を空ける）")]
    [SerializeField] private float   gridYOffset = 30f;
    [Tooltip("セル内スプライトの大きさ（セルに対する比率）")]
    [SerializeField] private Vector2 stickerFill = new Vector2(0.8f, 0.72f);
    [Tooltip("ハンガー枠のときスプライトを下げる量（ノッチ分。セル高さ比）")]
    [SerializeField] private float   hangerStickerDrop = 0.05f;

    [Header("スクロール設定")]
    [SerializeField] private float snapDuration  = 0.3f;
    [SerializeField] private float snapThreshold = 200f;

    // =========================================================================
    //  公開
    // =========================================================================

    /// <summary>セルをクリックしたときの通知（引数：アイテムのインデックス, 表示データ）。着せ替えはしない。</summary>
    public Action<int, BookItem> OnSelected;

    /// <summary>セルを右のプレイヤーへドロップして着せ替えを要求するときの通知。</summary>
    public Action<int, BookItem> OnEquipRequested;

    /// <summary>いずれかのセルからカーソルが離れたときの通知（説明を着用中へ戻す等）。</summary>
    public Action OnHoverExit;

    // =========================================================================
    //  内部変数
    // =========================================================================

    private readonly List<GameObject> pages     = new List<GameObject>();
    private readonly List<Image>      dotImages = new List<Image>();
    private readonly List<RectTransform> cellRects = new List<RectTransform>();

    private int   currentPage = 0;
    private int   pageCount   = 0;
    private float pageWidth   = 0f;

    private bool  isDragging   = false;
    private float dragStartX   = 0f;
    private float containerStartX = 0f;
    private float velocity     = 0f;
    private float lastDragX    = 0f;

    private int   equippedIndex = -1;   // 「着用中」ハイライト中のセル（クリック選択とは別概念）
    private Coroutine snapCoroutine = null;

    private const int COLS = 2;
    private const int ROWS = 2;
    private const int CELLS_PER_PAGE = COLS * ROWS;

    // =========================================================================
    //  ページ構築
    // =========================================================================

    /// <summary>表示アイテムをセットしてページを再構築する</summary>
    public void Show(List<BookItem> items, bool useHanger)
    {
        items = items ?? new List<BookItem>();

        // 既存を破棄
        foreach (var p in pages) if (p != null) Destroy(p);
        pages.Clear();
        cellRects.Clear();
        if (dotParent != null)
            foreach (Transform d in dotParent) Destroy(d.gameObject);
        dotImages.Clear();
        equippedIndex = -1;

        pageWidth = (viewport != null) ? viewport.rect.width : 700f;
        float pageHeight = (viewport != null) ? viewport.rect.height : 900f;
        pageCount = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)CELLS_PER_PAGE));

        for (int p = 0; p < pageCount; p++)
        {
            GameObject page = MakeChildRect(scrollContainer, "Page_" + p);
            RectTransform prt = page.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot     = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(pageWidth, pageHeight);
            prt.anchoredPosition = new Vector2(p * pageWidth, 0f);
            pages.Add(page);

            for (int i = 0; i < CELLS_PER_PAGE; i++)
            {
                int idx = p * CELLS_PER_PAGE + i;
                if (idx >= items.Count) break;
                BuildCell(prt, i, idx, items[idx], useHanger);
            }
        }

        // コンテナを整える（左寄せアンカー）
        if (scrollContainer != null)
        {
            scrollContainer.anchorMin = scrollContainer.anchorMax = new Vector2(0f, 0.5f);
            scrollContainer.pivot     = new Vector2(0f, 0.5f);
            scrollContainer.sizeDelta = new Vector2(pageWidth * pageCount, pageHeight);
            scrollContainer.anchoredPosition = Vector2.zero;
        }

        currentPage = 0;
        BuildDots();
        UpdateDots();
    }

    /**=======================================================================
      セル1枚を生成する
    =======================================================================**/
    private void BuildCell(RectTransform page, int slotInPage, int itemIndex, BookItem item, bool useHanger)
    {
        // グリッド内位置（page は pivot(0,0.5)：ローカル原点は左端中央）
        float stepX = cellSize.x + cellSpacing.x;
        float stepY = cellSize.y + cellSpacing.y;
        int col = slotInPage % COLS;
        int row = slotInPage / COLS;
        float cx = pageWidth * 0.5f + (col - (COLS - 1) * 0.5f) * stepX;
        float cy = gridYOffset - (row - (ROWS - 1) * 0.5f) * stepY;

        // セル本体（Button）
        GameObject cell = MakeChildRect(page, "Cell_" + itemIndex);
        RectTransform crt = cell.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = cellSize;
        crt.anchoredPosition = new Vector2(cx, cy);

        Image bg = cell.AddComponent<Image>();
        if (useHanger && hangerSprite != null) {
            bg.sprite = hangerSprite;
            bg.preserveAspect = true;
            bg.color = Color.white;
        } else {
            bg.color = new Color(0f, 0f, 0f, 0f);   // 透明（当たり判定のみ）
            // 素貼りは少し傾けて「貼った」感を出す（index で決定的）
            crt.localRotation = Quaternion.Euler(0, 0, (itemIndex % 3 - 1) * 5f);
        }
        bg.raycastTarget = true;

        // スプライト
        GameObject art = MakeChildRect(cell.transform, "Art");
        RectTransform art_rt = art.GetComponent<RectTransform>();
        art_rt.anchorMin = art_rt.anchorMax = new Vector2(0.5f, 0.5f);
        art_rt.pivot = new Vector2(0.5f, 0.5f);
        art_rt.sizeDelta = new Vector2(cellSize.x * stickerFill.x, cellSize.y * stickerFill.y);
        art_rt.anchoredPosition = new Vector2(0f, useHanger ? -cellSize.y * hangerStickerDrop : 0f);
        Image artImg = art.AddComponent<Image>();
        artImg.sprite = item.sprite;
        artImg.preserveAspect = true;
        artImg.raycastTarget = false;
        if (item.sprite == null) artImg.color = new Color(0.8f, 0.8f, 0.8f, 1f);

        // クリック選択
        Button btn = cell.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        int captured = itemIndex;
        BookItem capturedItem = item;
        btn.onClick.AddListener(() => Select(captured, capturedItem));

        // カーソルを重ねるとスプラトゥーン風にチャカチャカ揺れる＋その瞬間に説明を切り替える。
        // 離したら説明を着用中へ戻す（OnHoverExit）。
        var jiggle = cell.AddComponent<SplatoonHoverJiggle>();
        jiggle.OnHover = () => Select(captured, capturedItem);
        jiggle.OnHoverExit = () => OnHoverExit?.Invoke();

        // CHANGE（服）タブ：セルを右のプレイヤーへドラッグ＆ドロップして着替え
        if (useHanger) {
            var clothDrag = cell.AddComponent<ClothCellDrag>();
            clothDrag.Setup(item.sprite, GetComponentInParent<Canvas>(), () => OnEquipRequested?.Invoke(captured, capturedItem));
        }

        cellRects.Add(crt);
    }

    /**=======================================================================
      選択（説明更新＋ポップ演出）
    =======================================================================**/
    private void Select(int index, BookItem item)
    {
        // ハイライト(1.08)は「着用中」表示専用。クリックしただけでは動かさない。
        // 着せ替えが成立したら StickerBookUI が SetEquippedIndex で移動させるので、
        // 装備できない服（プレハブ未設定）をクリックしても着用中セルの表示を奪わない。
        OnSelected?.Invoke(index, item);
    }

    /**=======================================================================
      指定インデックスを「着用中」として選択表示する（OnSelected は呼ばない）
        StickerBookUI が開いた時、いま着ている服を初期ハイライトするのに使う。
        そのセルを含むページへアニメーションなしで即座に合わせる。
        Show() の直後に呼ぶこと（pageWidth 確定後）。
    =======================================================================**/
    public void SetEquippedIndex(int index)
    {
        equippedIndex = index;
        for (int i = 0; i < cellRects.Count; i++) {
            if (cellRects[i] == null) continue;
            cellRects[i].localScale = (i == index) ? Vector3.one * 1.08f : Vector3.one;
        }

        if (index < 0) return;
        JumpToPage(index / CELLS_PER_PAGE);
    }

    // =========================================================================
    //  演出用アクセサ（AliceUnlockTutorial 等から使用）
    // =========================================================================

    /// <summary>表示順（Show に渡した並び）での index のセル RectTransform を返す。無効なら null。</summary>
    public RectTransform GetCell(int visibleIndex) {
        if (visibleIndex < 0 || visibleIndex >= cellRects.Count) return null;
        return cellRects[visibleIndex];
    }
    public int CellCount    => cellRects.Count;
    public int CellsPerPage => CELLS_PER_PAGE;
    /// <summary>アニメーションなしで指定ページへ合わせる（演出用の公開ラッパ）</summary>
    public void GoToPage(int page) => JumpToPage(page);

    /// <summary>アニメーションなしで即座に指定ページへ合わせる（初期表示用）</summary>
    private void JumpToPage(int page)
    {
        if (snapCoroutine != null) { StopCoroutine(snapCoroutine); snapCoroutine = null; }
        currentPage = Mathf.Clamp(page, 0, pageCount - 1);
        if (scrollContainer != null)
            scrollContainer.anchoredPosition =
                new Vector2(-currentPage * pageWidth, scrollContainer.anchoredPosition.y);
        UpdateDots();
    }

    // =========================================================================
    //  ドットインジケーター
    // =========================================================================

    private void BuildDots()
    {
        if (dotParent == null) return;
        for (int i = 0; i < pageCount; i++)
        {
            GameObject dot = MakeChildRect(dotParent, "Dot_" + i);
            Image img = dot.AddComponent<Image>();
            img.raycastTarget = false;
            dotImages.Add(img);
        }
    }

    private void UpdateDots()
    {
        for (int i = 0; i < dotImages.Count; i++)
        {
            bool on = (i == currentPage);
            dotImages[i].sprite = on ? dotOnSprite : dotOffSprite;
            dotImages[i].SetNativeSize();
            RectTransform rt = dotImages[i].rectTransform;
            rt.sizeDelta = on ? dotOnSize : dotOffSize;
        }
    }

    // =========================================================================
    //  ドラッグ処理
    // =========================================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (scrollContainer == null) return;
        if (snapCoroutine != null) { StopCoroutine(snapCoroutine); snapCoroutine = null; }
        isDragging      = true;
        dragStartX      = eventData.position.x;
        lastDragX       = eventData.position.x;
        containerStartX = scrollContainer.anchoredPosition.x;
        velocity        = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || scrollContainer == null) return;
        float delta = eventData.position.x - dragStartX;
        scrollContainer.anchoredPosition = new Vector2(containerStartX + delta, scrollContainer.anchoredPosition.y);
        velocity  = (eventData.position.x - lastDragX) / Mathf.Max(Time.deltaTime, 1e-4f);
        lastDragX = eventData.position.x;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        if (scrollContainer == null) return;

        int targetPage = currentPage;
        if (velocity < -snapThreshold && currentPage < pageCount - 1)      targetPage = currentPage + 1;
        else if (velocity > snapThreshold && currentPage > 0)             targetPage = currentPage - 1;
        else {
            float pos = -scrollContainer.anchoredPosition.x;
            targetPage = Mathf.Clamp(Mathf.RoundToInt(pos / pageWidth), 0, pageCount - 1);
        }
        SnapToPage(targetPage);
    }

    // =========================================================================
    //  スナップアニメーション
    // =========================================================================

    private void SnapToPage(int page)
    {
        currentPage = Mathf.Clamp(page, 0, pageCount - 1);
        UpdateDots();
        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(AnimateSnap(-currentPage * pageWidth));
    }

    private IEnumerator AnimateSnap(float targetX)
    {
        float startX  = scrollContainer.anchoredPosition.x;
        float elapsed = 0f;
        while (elapsed < snapDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / snapDuration);
            float et = 1f - Mathf.Pow(1f - t, 3f);   // EaseOutCubic
            scrollContainer.anchoredPosition = new Vector2(Mathf.Lerp(startX, targetX, et), scrollContainer.anchoredPosition.y);
            yield return null;
        }
        scrollContainer.anchoredPosition = new Vector2(targetX, scrollContainer.anchoredPosition.y);
        snapCoroutine = null;
    }

    // =========================================================================
    //  ユーティリティ
    // =========================================================================

    private static GameObject MakeChildRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
