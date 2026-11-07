/**===========================================================================

 File   : シールドラッグハンドラ [StickerDragHandler.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------

 シールセルをドラッグしたときにゴーストUIを表示し、
 PlayerViewer3D の RawImage 上にドロップするとデカールを貼る。

===========================================================================**/

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/**===========================================================================
  シールドラッグハンドラ
  各シールスロットの GameObject にアタッチして使う。
===========================================================================**/
public class StickerDragHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler {

    // ドラッグするシールのデータ
    public StickerData StickerData { get; set; }

    [Header("ゴーストUI設定")]
    [SerializeField] private Canvas rootCanvas;   // Inspector でルート Canvas をセット

    [Header("貼り付けフィードバック")]
    [SerializeField] private Color colorNeutral = new Color(1f, 1f, 1f, 0.75f);   // ビューア外
    [SerializeField] private Color colorValid   = new Color(0f, 1f, 0f, 0.65f);   // 貼れる（緑）
    [SerializeField] private Color colorInvalid = new Color(1f, 0f, 0f, 0.65f);   // 貼れない（赤）

    // 内部
    private GameObject   ghostObject;
    private Image        ghostImage;
    private CanvasGroup  ghostCanvasGroup;
    // ドラッグ中にビューアへの参照をキャッシュ
    private PlayerViewer3D cachedViewer;

    /**=======================================================================
      ルート Canvas を外部からセットする
    =======================================================================**/
    public void SetRootCanvas(Canvas canvas) {
        rootCanvas = canvas;
    }

    /**=======================================================================
      ドラッグ開始：ゴーストUIを生成する
    =======================================================================**/
    public void OnBeginDrag(PointerEventData eventData) {
        if (StickerData == null) return;
        if (rootCanvas == null) {
            Debug.LogWarning("[StickerDragHandler] rootCanvas が未設定です。");
            return;
        }

        // ゴーストを Canvas の直下に生成（最前面に表示するため）
        ghostObject = new GameObject("StickerGhost");
        ghostObject.transform.SetParent(rootCanvas.transform, false);
        ghostObject.transform.SetAsLastSibling();

        // サイズ設定
        RectTransform rt = ghostObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(80f, 80f);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        // スプライト設定
        ghostImage = ghostObject.AddComponent<Image>();
        ghostImage.sprite = StickerInventory.Instance != null
            ? StickerInventory.Instance.GetDisplaySprite(StickerData)
            : null;
        ghostImage.raycastTarget = false;  // ゴースト自身がドロップ判定を邪魔しないように
        ghostImage.color = colorNeutral;

        // 半透明（alpha は color 側で管理するため CanvasGroup は 1 に）
        ghostCanvasGroup = ghostObject.AddComponent<CanvasGroup>();
        ghostCanvasGroup.alpha = 1f;
        ghostCanvasGroup.blocksRaycasts = false;

        // シーン内の PlayerViewer3D を探してキャッシュ
        cachedViewer = Object.FindObjectOfType<PlayerViewer3D>();

        MoveGhostToPointer(eventData);
    }

    /**=======================================================================
      ドラッグ中：ゴーストをポインタに追従させ、貼れるかどうかで色を変える
    =======================================================================**/
    public void OnDrag(PointerEventData eventData) {
        if (ghostObject == null) return;
        MoveGhostToPointer(eventData);
        UpdateGhostColor(eventData);
    }

    /**=======================================================================
      ドラッグ終了：ゴーストを削除する
      実際のドロップ処理は PlayerViewer3D の IDropHandler が担当する
    =======================================================================**/
    public void OnEndDrag(PointerEventData eventData) {
        CleanupGhost();
    }

    // ドラッグ中にスロット自体が破棄/無効化されると OnEndDrag が呼ばれず、
    // ゴースト(rootCanvas 直下)が画面に残ってしまう。無効化・破棄時に必ず片付ける。
    private void OnDisable() => CleanupGhost();
    private void OnDestroy() => CleanupGhost();

    private void CleanupGhost() {
        if (ghostObject != null) {
            Destroy(ghostObject);
            ghostObject = null;
        }
        cachedViewer = null;
    }

    /**=======================================================================
      ゴーストの色をビューア上の有効性に合わせて更新する
    =======================================================================**/
    private void UpdateGhostColor(PointerEventData eventData) {
        if (ghostImage == null) return;
        if (cachedViewer == null) {
            ghostImage.color = colorNeutral;
            return;
        }

        // UI カメラを解決（Overlay なら null）
        Camera uiCam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera : null;

        // 服シール：モデルヒット不要、ビューア内なら常に緑（BakeMesh 不要で軽量）
        if (StickerData != null && StickerData.stickerType == StickerType.Clothes) {
            bool overViewer = cachedViewer.IsPointerOverViewer(eventData.position, uiCam);
            ghostImage.color = overViewer ? colorValid : colorNeutral;
            return;
        }

        bool valid = cachedViewer.QueryPasteValidity(eventData.position, uiCam, out bool onModel);

        if (!onModel)
            ghostImage.color = colorNeutral;   // ビューア外 or モデル外 → 通常
        else if (valid)
            ghostImage.color = colorValid;     // モデル上 + ボーン近傍 → 緑
        else
            ghostImage.color = colorInvalid;   // モデル上 + ボーン遠い → 赤
    }

    /**=======================================================================
      ゴーストをポインタ座標に移動する
    =======================================================================**/
    private void MoveGhostToPointer(PointerEventData eventData) {
        if (ghostObject == null || rootCanvas == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(),
            eventData.position,
            rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera,
            out Vector2 localPos
        );

        ghostObject.GetComponent<RectTransform>().localPosition = localPos;
    }
}
