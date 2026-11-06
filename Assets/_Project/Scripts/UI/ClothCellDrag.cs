/**===========================================================================

 File   : 服セルのドラッグ着せ替え [ClothCellDrag.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/9
 -----------------------------------------------------------------------------

 CHANGEタブの服セルをドラッグし、右側のプレイヤープレビュー(PlayerViewer3D)
 の上でドロップすると着替える。ドラッグ中はゴースト(服スプライト)が指に追従し、
 ビューア上では色が変わって「ここで離せる」ことを示す。
   ・ドロップ成立時はクリックと同じ選択処理を呼ぶ(装備＋ハイライト＋プレビュー更新)
   ・CHANGEは1ページ構成なので横スワイプとは競合しない
 raycastTarget を持つ Graphic と同じ GameObject（＝セル）に付けること。

===========================================================================**/
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ClothCellDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {

    private Sprite        _sprite;
    private Canvas        _rootCanvas;
    private System.Action _onDropOnViewer;

    private PlayerViewer3D _viewer;
    private GameObject     _ghost;
    private Image          _ghostImg;

    private static readonly Color cNeutral = new Color(1f, 1f, 1f, 0.80f);
    private static readonly Color cValid   = new Color(0.55f, 1f, 0.70f, 0.95f);   // ビューア上（着替えOK）
    private const int GHOST_SORTING_ORDER = 3400;   // 演出中の前面化(3280-3340)より上

    /// <summary>生成時に ClothesSwipeUI から設定する。</summary>
    public void Setup(Sprite sprite, Canvas rootCanvas, System.Action onDropOnViewer) {
        _sprite = sprite;
        _rootCanvas = rootCanvas;
        _onDropOnViewer = onDropOnViewer;
    }

    public void OnBeginDrag(PointerEventData e) {
        _viewer = Object.FindFirstObjectByType<PlayerViewer3D>();
        if (_rootCanvas == null) _rootCanvas = GetComponentInParent<Canvas>();
        if (_rootCanvas == null) return;

        _ghost = new GameObject("ClothGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _ghost.transform.SetParent(_rootCanvas.transform, false);
        _ghost.transform.SetAsLastSibling();
        var rt = (RectTransform)_ghost.transform;
        rt.sizeDelta = new Vector2(160f, 200f);
        rt.pivot = rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        _ghostImg = _ghost.GetComponent<Image>();
        _ghostImg.sprite = _sprite;
        _ghostImg.preserveAspect = true;
        _ghostImg.raycastTarget = false;   // ドロップ判定を邪魔しない
        _ghostImg.color = cNeutral;

        // アリス入手演出中は服のハンガーやビューポートが overrideSorting(3280) で前面化される。
        // ゴーストがその下に潜って見えなくならないよう、ドラッグ中は常に最前面へ持ち上げる。
        var ghostCanvas = _ghost.AddComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = GHOST_SORTING_ORDER;

        MoveGhost(e);
    }

    public void OnDrag(PointerEventData e) {
        if (_ghost == null) return;
        MoveGhost(e);
        bool over = _viewer != null && _viewer.IsPointerOverViewer(e.position, UiCam());
        _ghostImg.color = over ? cValid : cNeutral;
    }

    public void OnEndDrag(PointerEventData e) {
        bool over = _viewer != null && _viewer.IsPointerOverViewer(e.position, UiCam());
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
        if (over && _onDropOnViewer != null) _onDropOnViewer.Invoke();   // クリックと同じ着せ替え処理
    }

    private Camera UiCam() =>
        (_rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _rootCanvas.worldCamera : null;

    private void MoveGhost(PointerEventData e) {
        if (_ghost == null || _rootCanvas == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_rootCanvas.transform, e.position, UiCam(), out Vector2 lp);
        ((RectTransform)_ghost.transform).localPosition = lp;
    }
}
