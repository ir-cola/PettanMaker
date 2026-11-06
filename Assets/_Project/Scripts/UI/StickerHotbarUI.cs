/**===========================================================================

 File   : シールホットバーUIクラス [StickerHotbarUI.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/
 -----------------------------------------------------------------------------

 HUDの右下に3スロットを横並びで表示するホットバー
   ・中央が選択中スロット（前面・大きめ）
   ・左右は隣接スロット（中央の裏に少し重なる）
   ・マウスホイールでバウンス＋イージングアニメ付きで切り替え
   ・Item タイプのシールをスタック表示（同一 StickerData は枚数加算）

 RightDown オブジェクトにアタッチすること

===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/**===========================================================================
  シールホットバーUIクラス
===========================================================================**/

public class StickerHotbarUI : MonoBehaviour {
    // ---- Inspector --------------------------------------------------------

    [Header("ステージ固定アイテム（3種固定）")]
    [Tooltip("このステージでドロップする3種のStickerDataをセット")]
    [SerializeField] private StickerData[] stageItems = new StickerData[3];

    [Tooltip("未所持スロットのグレーアウト色")]
    [SerializeField] private Color notOwnedColor = new Color(0.35f, 0.35f, 0.35f, 0.8f);

    [Header("スロットの RectTransform（左・中央・右）")]
    [SerializeField] private RectTransform slotLeft;

    [SerializeField] private RectTransform slotCenter;
    [SerializeField] private RectTransform slotRight;

    [Header("スロットの Image（左・中央・右）")]
    [SerializeField] private Image iconLeft;

    [SerializeField] private Image iconCenter;
    [SerializeField] private Image iconRight;

    [Header("スタック数テキスト（左・中央・右）")]
    [SerializeField] private TextMeshProUGUI countLeft;

    [SerializeField] private TextMeshProUGUI countCenter;
    [SerializeField] private TextMeshProUGUI countRight;

    [Header("レイアウト設定")]
    [Tooltip("中央スロットの基準サイズ")]
    [SerializeField] private Vector2 centerSize = new Vector2(96f, 96f);

    [Tooltip("左右スロットのサイズ")]
    [SerializeField] private Vector2 sideSize = new Vector2(72f, 72f);

    [Tooltip("左右スロットの X オフセット（中央からの距離）")]
    [SerializeField] private float sideOffsetX = 60f;

    [Tooltip("左右スロットが中央の後ろに重なる Y オフセット")]
    [SerializeField] private float sideOffsetY = -8f;

    [Tooltip("空スロット時の色")]
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.2f);

    [Header("アニメーション設定")]
    [Tooltip("切り替えアニメーションの時間（秒）")]
    [SerializeField] private float animDuration = 0.25f;

    [Tooltip("バウンスのオーバーシュート量（0 で無効）")]
    [SerializeField] private float bounceAmount = 0.12f;

    [Tooltip("マウスホイールの感度（デッドゾーン、新InputSystemは120前後の値）")]
    [SerializeField] private float wheelThreshold = 1f;

    [Tooltip("連続切り替えの最短間隔（秒）。これより早いスクロールは無視される")]
    [SerializeField] private float scrollCooldown = 0.1f;

    [Header("ショートカット使用")]
    [Tooltip("E キーで選択中シールを使用したときに効果を発動するコントローラ。空なら自動取得。")]
    [SerializeField] private ItemEffectController itemEffectController;

    // ---- 内部 -------------------------------------------------------------

    // ステージアイテムの所持枚数（stageItems と同じ順番）
    private int[] ownedCounts = new int[3];

    private int selectedIndex = 0;
    private bool isAnimating = false;
    private Coroutine animCoroutine = null;
    private float lastScrollTime = -999f;

    // 各スロットのアニメーション前の位置・サイズ
    private Vector2 posLeft_base;

    private Vector2 posCenter_base;
    private Vector2 posRight_base;

    // =========================================================================
    //  Unity ライフサイクル
    // =========================================================================

    private void Start() {
        ApplyBaseLayout();

        if (StickerInventory.Instance != null) {
            StickerInventory.Instance.onStickerAdded.AddListener(HandleInventoryChanged);
            StickerInventory.Instance.onStickerRemoved.AddListener(HandleInventoryChanged);
        }

        // ItemEffectController を Inspector 未設定なら自動取得
        if (itemEffectController == null)
            itemEffectController = FindObjectOfType<ItemEffectController>();

        RefreshItems();
    }

    private void Update() {
        HandleScroll();
        HandleShortcut();
    }

    // 所持シール増減の通知ハンドラ（購読解除できるよう名前付きにする）
    private void HandleInventoryChanged(StickerData _) => RefreshItems();

    private void OnDestroy() {
        // StickerInventory は DontDestroyOnLoad で持続する。解除しないと
        // ステージ再入のたび破棄済みホットバーへ RefreshItems が飛んで例外＋購読蓄積になる
        if (StickerInventory.Instance != null) {
            StickerInventory.Instance.onStickerAdded.RemoveListener(HandleInventoryChanged);
            StickerInventory.Instance.onStickerRemoved.RemoveListener(HandleInventoryChanged);
        }
    }

    /// <summary>マウスホイール / LB・RB でスロット切り替え</summary>
    private void HandleScroll() {
        int dir = 0;
        if (Mouse.current != null) {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) >= wheelThreshold) dir = scroll > 0f ? -1 : 1;   // 上スクロール = 前のアイテム
        }
        var gp = Gamepad.current;
        if (dir == 0 && gp != null) {
            if (gp.leftShoulder.wasPressedThisFrame) dir = -1;        // LB = 前
            else if (gp.rightShoulder.wasPressedThisFrame) dir = 1;   // RB = 次
        }
        if (dir == 0) return;

        // クールダウン中は入力を無視
        if (Time.unscaledTime - lastScrollTime < scrollCooldown) return;

        lastScrollTime = Time.unscaledTime;
        ShiftSelection(dir);
    }

    /// <summary>E キー / 十字キー上 で選択中シールを使用（ItemEffectController.UseSticker を呼ぶ）。
    /// ※ Q / Y(北) は服のアクティブスキル(PlayerClothSkills)専用に移したためアイテム使用は E。
    ///   ゲームパッドは X(西)=世界シールはがし(StickerPickup) と競合するため十字キー上に割当。</summary>
    private void HandleShortcut() {
        bool pressed = (Keyboard.current?.eKey.wasPressedThisFrame ?? false)
                    || (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false);
        if (!pressed) return;

        StickerData data = GetSelectedSticker();
        if (data == null) {
            Debug.Log("[StickerHotbarUI] 選択中スロットに使用可能なシールがありません。");
            return;
        }
        if (itemEffectController == null) {
            Debug.LogWarning("[StickerHotbarUI] ItemEffectController が未設定/未取得です。");
            return;
        }
        itemEffectController.UseSticker(data);
    }

    // =========================================================================
    //  レイアウト
    // =========================================================================

    /// <summary>Inspector で設定済みの位置・サイズを基準値として読み取る（書き込みしない）</summary>
    private void ApplyBaseLayout() {
        // anchoredPosition / sizeDelta は Inspector の値をそのまま使う
        //    スクリプトから上書きすると起動時に位置がリセットされるため読み取りのみ行う
        if (slotCenter != null)
            posCenter_base = slotCenter.anchoredPosition;

        if (slotLeft != null)
            posLeft_base = slotLeft.anchoredPosition;

        if (slotRight != null)
            posRight_base = slotRight.anchoredPosition;

        // Z オーダー：中央を最前面
        SetSiblingOrder();
    }

    /// <summary>中央スロットを最前面にする（sibling index）</summary>
    private void SetSiblingOrder() {
        if (slotLeft != null) slotLeft.SetSiblingIndex(0);
        if (slotRight != null) slotRight.SetSiblingIndex(1);
        if (slotCenter != null) slotCenter.SetAsLastSibling();
    }

    // =========================================================================
    //  インベントリ反映
    // =========================================================================

    /// <summary>インベントリからステージ3種の所持枚数を集計してスロット更新</summary>
    private void RefreshItems() {
        // stageItems が未設定ならスキップ
        if (stageItems == null || stageItems.Length == 0) return;

        // 配列サイズ確保
        if (ownedCounts.Length != stageItems.Length)
            ownedCounts = new int[stageItems.Length];

        // 全枚数をリセットしてインベントリから集計
        for (int i = 0; i < ownedCounts.Length; i++) ownedCounts[i] = 0;

        if (StickerInventory.Instance != null) {
            List<StickerData> all = StickerInventory.Instance.GetAllStickers();
            foreach (StickerData d in all) {
                for (int i = 0; i < stageItems.Length; i++) {
                    if (stageItems[i] != null && stageItems[i] == d) {
                        ownedCounts[i]++;
                        break;
                    }
                }
            }
        }

        // 選択インデックスを範囲内に収める
        int total = stageItems.Length;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, total - 1);

        UpdateSlotVisuals();
    }

    // =========================================================================
    //  スロット切り替え
    // =========================================================================

    /// <summary>選択インデックスを dir 方向にずらしてアニメ再生</summary>
    private void ShiftSelection(int dir) {
        int total = stageItems != null ? stageItems.Length : 0;
        if (total == 0) return;

        int newIndex = selectedIndex + dir;
        if (newIndex < 0) newIndex = total - 1;
        if (newIndex >= total) newIndex = 0;

        // アニメ中でも中断して即座に次を開始（連続スクロール対応）
        if (animCoroutine != null) {
            StopCoroutine(animCoroutine);
            // 中断された位置をリセット
            if (slotLeft != null) { slotLeft.anchoredPosition = posLeft_base; slotLeft.sizeDelta = sideSize; }
            if (slotCenter != null) { slotCenter.anchoredPosition = posCenter_base; slotCenter.sizeDelta = centerSize; }
            if (slotRight != null) { slotRight.anchoredPosition = posRight_base; slotRight.sizeDelta = sideSize; }
            isAnimating = false;
        }

        animCoroutine = StartCoroutine(AnimateCarousel(dir, newIndex));
    }

    // =========================================================================
    //  アニメーション
    // =========================================================================

    /// <summary>
    /// カルーセル型アニメーション
    /// dir=1（次へ）: 旧Left→画面外左、旧Center→Left位置、旧Right→Center位置、新Right→画面外右からスライドイン
    /// dir=-1（前へ）: 逆方向
    /// </summary>
    private IEnumerator AnimateCarousel(int dir, int newIndex) {
        isAnimating = true;

        int total = stageItems.Length;
        int prevIndex = selectedIndex;
        selectedIndex = newIndex;

        // アニメ前（旧状態）のインデックス
        int oldLeftIdx = (total > 0) ? ((prevIndex - 1 + total) % total) : -1;
        int oldCenterIdx = prevIndex;
        int oldRightIdx = (total > 0) ? ((prevIndex + 1) % total) : -1;

        // アニメ後（新状態）のインデックス
        int newLeftIdx = (total > 0) ? ((newIndex - 1 + total) % total) : -1;
        int newCenterIdx = newIndex;
        int newRightIdx = (total > 0) ? ((newIndex + 1) % total) : -1;

        // 画面外ポジション（スライドインしてくる新スロット用）
        // posLeft_base / posRight_base からの相対オフセットで計算（絶対座標にしない）
        float outsideX = sideOffsetX * 2.2f;
        Vector2 outsideLeft = posLeft_base + new Vector2(-outsideX, 0f);
        Vector2 outsideRight = posRight_base + new Vector2(outsideX, 0f);

        // --- 各スロットの From / To 位置・サイズを決定 ---
        // dir=1 のとき: Left←消える、旧Center→Left、旧Right→Center、新Right←画面外右
        // dir=-1のとき: Right←消える、旧Center→Right、旧Left→Center、新Left←画面外左

        Vector2 fromLeft, toLeft, fromCenter, toCenter, fromRight, toRight;
        Vector2 fromSizeLeft, toSizeLeft, fromSizeCenter, toSizeCenter, fromSizeRight, toSizeRight;

        if (dir == 1) {
            // 「次へ」: 全体が左へ流れる
            // slotLeft   : 旧Center → Left位置へ移動（縮む）
            // slotCenter : 旧Right（新選択）→ Center位置へ移動（大きくなる）
            // slotRight  : 新Right → 画面外右からスライドイン
            SetSlotVisual(iconLeft,   countLeft,   oldCenterIdx);
            SetSlotVisual(iconCenter, countCenter, oldRightIdx);
            SetSlotVisual(iconRight,  countRight,  newRightIdx);

            fromLeft = posCenter_base; toLeft = posLeft_base;    // Center→Left
            fromCenter = posRight_base; toCenter = posCenter_base;  // Right→Center
            fromRight = outsideRight; toRight = posRight_base;   // 画面外→Right

            fromSizeLeft = centerSize; toSizeLeft = sideSize;
            fromSizeCenter = sideSize; toSizeCenter = centerSize;
            fromSizeRight = sideSize; toSizeRight = sideSize;
        }
        else {
            // 「前へ」: 全体が右へ流れる
            // slotRight  : 旧Center → Right位置へ移動（縮む）
            // slotCenter : 旧Left（新選択）→ Center位置へ移動（大きくなる）
            // slotLeft   : 新Left → 画面外左からスライドイン
            SetSlotVisual(iconRight,  countRight,  oldCenterIdx);
            SetSlotVisual(iconCenter, countCenter, oldLeftIdx);
            SetSlotVisual(iconLeft,   countLeft,   newLeftIdx);

            fromRight = posCenter_base; toRight = posRight_base;   // Center→Right
            fromCenter = posLeft_base; toCenter = posCenter_base;  // Left→Center
            fromLeft = outsideLeft; toLeft = posLeft_base;    // 画面外→Left

            fromSizeRight = centerSize; toSizeRight = sideSize;
            fromSizeCenter = sideSize; toSizeCenter = centerSize;
            fromSizeLeft = sideSize; toSizeLeft = sideSize;
        }

        float elapsed = 0f;
        while (elapsed < animDuration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animDuration);
            float et = EaseOutBack(t, bounceAmount);

            if (slotLeft != null) {
                slotLeft.anchoredPosition = Vector2.LerpUnclamped(fromLeft, toLeft, et);
                slotLeft.sizeDelta = Vector2.LerpUnclamped(fromSizeLeft, toSizeLeft, et);
            }
            if (slotCenter != null) {
                slotCenter.anchoredPosition = Vector2.LerpUnclamped(fromCenter, toCenter, et);
                slotCenter.sizeDelta = Vector2.LerpUnclamped(fromSizeCenter, toSizeCenter, et);
            }
            if (slotRight != null) {
                slotRight.anchoredPosition = Vector2.LerpUnclamped(fromRight, toRight, et);
                slotRight.sizeDelta = Vector2.LerpUnclamped(fromSizeRight, toSizeRight, et);
            }

            yield return null;
        }

        // 最終値に確定してビジュアルをリセット
        if (slotLeft != null) { slotLeft.anchoredPosition = posLeft_base; slotLeft.sizeDelta = sideSize; }
        if (slotCenter != null) { slotCenter.anchoredPosition = posCenter_base; slotCenter.sizeDelta = centerSize; }
        if (slotRight != null) { slotRight.anchoredPosition = posRight_base; slotRight.sizeDelta = sideSize; }

        UpdateSlotVisuals();
        isAnimating = false;
        animCoroutine = null;
    }

    /// <summary>EaseOutBack（オーバーシュートして戻るイージング）</summary>
    private float EaseOutBack(float t, float overshoot) {
        float c = overshoot <= 0f ? 1.70158f : overshoot * 10f;
        float s = c + 1f;
        return 1f + s * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f);
    }

    // =========================================================================
    //  ビジュアル更新
    // =========================================================================

    /// <summary>現在の selectedIndex に合わせて3スロットの見た目を更新する</summary>
    private void UpdateSlotVisuals() {
        int total = stageItems != null ? stageItems.Length : 0;
        if (total == 0) return;

        int centerIdx = selectedIndex;
        int leftIdx = (centerIdx - 1 + total) % total;
        int rightIdx = (centerIdx + 1) % total;

        SetSlotVisual(iconLeft, countLeft, leftIdx);
        SetSlotVisual(iconCenter, countCenter, centerIdx);
        SetSlotVisual(iconRight, countRight, rightIdx);
    }

    private void SetSlotVisual(Image icon, TextMeshProUGUI countText, int index) {
        if (icon == null) return;
        if (stageItems == null || index < 0 || index >= stageItems.Length) {
            icon.sprite = null;
            icon.color = emptyColor;
            if (countText != null) countText.text = string.Empty;
            return;
        }

        StickerData data = stageItems[index];
        int count = (ownedCounts != null && index < ownedCounts.Length) ? ownedCounts[index] : 0;
        bool owned = count > 0;

        Sprite sp = (data != null) ? StickerInventory.Instance?.GetDisplaySprite(data) : null;

        if (sp != null) {
            icon.sprite = sp;
            icon.color = owned ? Color.white : notOwnedColor;
        }
        else {
            icon.sprite = null;
            icon.color = owned ? new Color(0.7f, 0.7f, 0.7f, 1f) : notOwnedColor;
        }

        if (countText != null)
            countText.text = owned && count > 1 ? count.ToString() : string.Empty;
    }

    // =========================================================================
    //  公開 API
    // =========================================================================

    /// <summary>現在選択中の StickerData を返す（未所持・未設定時は null）</summary>
    public StickerData GetSelectedSticker() {
        if (stageItems == null || selectedIndex >= stageItems.Length) return null;
        // 未所持なら null を返す
        if (ownedCounts[selectedIndex] <= 0) return null;
        return stageItems[selectedIndex];
    }

    /// <summary>左右ボタンから呼べるラッパー</summary>
    public void ScrollLeft() => ShiftSelection(-1);

    public void ScrollRight() => ShiftSelection(1);
}