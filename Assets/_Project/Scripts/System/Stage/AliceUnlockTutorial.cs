/**===========================================================================

 File   : アリス服 入手チュートリアル演出 [AliceUnlockTutorial.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/11
 -----------------------------------------------------------------------------

 アリスの服シールを剥がした（＝入手した）瞬間に始まる一連の導入演出。

   Beat0 入手直後の間（地面シールの剥がれ演出を見せる）
   Beat1 世界停止＋暗転。右上の既存ポップアップ(Canvas/HUD/RigthUp)で
         「TABでシール帳を開こう」を1文字ずつ表示（顔=ラベル画像で口パク）。
         ブックボタンだけ明るく残し、縁を発光。
   Beat2 TABで開いたら、デフォルト服の隣にアリスのハンガー枠が出現（チャカチャカ）、
         その上へアリスの服シールが「貼られる」（StickerFX.ApplyUI）。
   Beat3 同ポップアップを左下へ移して「ドラッグして貼ろう」。アリスのハンガー→右の
         プレビュー枠へ矢印が繰り返し伸びる。ハンガーとプレビュー枠だけ明るく。
   Beat4 着替え成立で「手に入れたね！おめでとう！早速使ってみよう」。暗転解除。

 吹き出しは既存の RigthUp を流用。演出中だけ overrideSorting で暗転より前面へ出す。
 世界停止は Time.timeScale=0 ＋ Player.SetUIMode。UI 系は全て unscaled で動く。
 一度きり（ClothUnlockState.AliceTutorialDone）。再入場時はアリスを解放するだけ。

===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AliceUnlockTutorial : MonoBehaviour {

    [Header("参照（未設定は自動解決）")]
    [SerializeField] private StickerBookUI       book;
    [Tooltip("解放＆着替え対象の服プレハブ（Cloth_Alice.prefab）")]
    [SerializeField] private GameObject          aliceClothPrefab;
    [Tooltip("入手判定に使うドロップ側 StickerData（Cloth_Alice.asset）。未設定なら Clothes 種別で判定")]
    [SerializeField] private StickerData         aliceStickerData;
    [SerializeField] private PlayerLifetimeScope player;
    [Tooltip("Beat1で暗転させず前面に出すブックボタン群（HUD/RightMiddle/Sticker_UI_2 = Book + Button_Book）")]
    [SerializeField] private RectTransform       bookButtonGroup;
    [Tooltip("右ページのプレビュー枠（Beat3で矢印の到達先＆スポット）")]
    [SerializeField] private RectTransform       previewFrameRect;

    [Header("オトモ吹き出し（既存 Canvas/HUD/RigthUp を流用）")]
    [SerializeField] private GameObject          companionPopup;      // RigthUp（開閉するルート）
    [SerializeField] private RectTransform       companionPopupRoot;  // Sticker_UI_3（拡縮/移動対象）
    [SerializeField] private TMP_Text            companionText;       // BookComment_BG/Text (TMP)
    [SerializeField] private RectTransform       companionFaceSlot;   // BookComment_Icon（ラベル顔を入れる枠）
    [Tooltip("Beat1 でオトモを画面中央へ寄せるオフセット（右上→中央に貼る）")]
    [SerializeField] private Vector2             centerOffset     = new Vector2(-665f, -383f);
    [Tooltip("Beat4 で左下へ移すオフセット（反転吹き出し）")]
    [SerializeField] private Vector2             bottomLeftOffset = new Vector2(-1332f, -690f);

    [Header("オトモの顔（ラベル画像）")]
    [SerializeField] private CompanionFaceSet    faceSet = new CompanionFaceSet();

    [Header("セリフ")]
    [SerializeField, TextArea(1,3)] private string tabPromptText  = "<color=#FF4FA3>TAB</color>キーを押して\nシール帳を開こう！";
    [SerializeField, TextArea(1,3)] private string dragPromptText = "<color=#FF4FA3>早速アリスの服</color>に\n着替えてみよう！";
    [SerializeField, TextArea(1,3)] private string congratsText   = "かわいい！\n似合ってるよ！";

    [Header("調整")]
    [SerializeField] private float preDelay  = 1.3f;    // 入手→暗転までの間（地面シールの剥がれを見せる）
    [SerializeField] private float dimAlpha  = 0.78f;
    [SerializeField] private float typeCps   = 24f;
    [SerializeField] private int   popupSortingOrder = 3300;  // 暗転(3000)より前面
    [SerializeField] private Color glowColor  = new Color(1f, 0.85f, 0.32f, 1f);
    [SerializeField] private Color arrowColor = new Color(1f, 0.5f, 0.72f, 1f);

    // 実行時
    bool _started;
    float _prevTimeScale = 1f;
    SpotlightOverlay _spot;
    CompanionSpeaker _speaker;
    ArrowGuide       _arrow;
    GameObject       _arrowCanvas;
    readonly List<LiftToken> _lifts = new List<LiftToken>();
    Animator _playerAnimator; AnimatorUpdateMode _playerAnimMode;         // 着替え中のスキニング崩れ対策
    SkirtDynamicBone[] _skirtBones;                                        // 世界停止中もスカート物理を回す（崩れ防止）
    Vector2 _textAncMin, _textAncMax, _textOffMin, _textOffMax;           // 吹き出しテキスト矩形の復元用
    RectTransform _sayRect;                                               // BookComment_Say（チェック画像）
    Vector2 _iconOrigPos, _sayOrigPos; Vector3 _iconOrigScale = Vector3.one, _sayOrigScale = Vector3.one; // 反転の復元用

    // RigthUp 復元用
    Canvas   _popupCanvas;       // 追加した overrideSorting Canvas
    GraphicRaycaster _popupRay;
    bool     _addedCanvas;
    Vector3  _popupOrigPos, _popupOrigScale, _textOrigScale;
    Color    _textOrigColor;
    bool     _faceSlotImgWasEnabled;
    Image    _faceSlotImg;

    // ====================================================================
    void Start() {
        ResolveRefs();
        if (book == null || aliceClothPrefab == null) {
            Debug.LogWarning("[AliceUnlockTutorial] book / aliceClothPrefab 未設定。無効化します。");
            enabled = false; return;
        }

        bool alreadyOwned = ClothUnlockState.AliceTutorialDone
                         || ClothUnlockState.IsUnlocked(aliceClothPrefab)
                         || InventoryHasAlice();
        if (alreadyOwned) {
            book.UnlockCostume(aliceClothPrefab, false);   // 再入場：解放済みにするだけ（演出なし）
            _started = true;
            return;
        }
        if (StickerInventory.Instance != null)
            StickerInventory.Instance.onStickerAdded.AddListener(OnStickerAdded);
        else
            StartCoroutine(SubscribeWhenReady());          // 直プレイ等で Inventory が後から出来る場合に対応
    }

    void OnDestroy() {
        if (StickerInventory.Instance != null)
            StickerInventory.Instance.onStickerAdded.RemoveListener(OnStickerAdded);
    }

    IEnumerator SubscribeWhenReady() {
        float t = 20f;
        while (StickerInventory.Instance == null && t > 0f) { t -= Time.unscaledDeltaTime; yield return null; }
        if (_started || StickerInventory.Instance == null) yield break;
        if (ClothUnlockState.AliceTutorialDone || ClothUnlockState.IsUnlocked(aliceClothPrefab) || InventoryHasAlice()) {
            book.UnlockCostume(aliceClothPrefab, false); _started = true;
        } else {
            StickerInventory.Instance.onStickerAdded.AddListener(OnStickerAdded);
        }
    }

    void ResolveRefs() {
        if (book == null)   book   = FindObjectOfType<StickerBookUI>();
        if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();

        var canvas = GameObject.Find("Canvas");
        Transform ct = canvas != null ? canvas.transform : null;
        if (ct != null) {
            // Find は非アクティブの子孫も辿れる（Canvas 自体はアクティブ）
            if (companionPopup == null)     { var t = ct.Find("HUD/RigthUp");                                 if (t) companionPopup = t.gameObject; }
            if (companionPopupRoot == null) { var t = ct.Find("HUD/RigthUp/Sticker_UI_3");                    if (t) companionPopupRoot = t as RectTransform; }
            if (companionText == null)      { var t = ct.Find("HUD/RigthUp/Sticker_UI_3/BookComment_BG/Text (TMP)"); if (t) companionText = t.GetComponent<TMP_Text>(); }
            if (companionFaceSlot == null)  { var t = ct.Find("HUD/RigthUp/Sticker_UI_3/BookComment_Icon");   if (t) companionFaceSlot = t as RectTransform; }
            if (bookButtonGroup == null)    { var t = ct.Find("HUD/RightMiddle/Sticker_UI_2");                if (t) bookButtonGroup = t as RectTransform; }
        }
        if (previewFrameRect == null) {
            foreach (var pv in Resources.FindObjectsOfTypeAll<PlayerViewer3D>())
                if (pv.gameObject.scene.IsValid()) { previewFrameRect = pv.transform as RectTransform; break; }
        }
    }

    bool InventoryHasAlice() {
        if (StickerInventory.Instance == null || aliceStickerData == null) return false;
        var list = StickerInventory.Instance.GetStickers(StickerType.Clothes);
        return list != null && list.Contains(aliceStickerData);
    }
    bool IsAliceSticker(StickerData d) {
        if (d == null) return false;
        if (aliceStickerData != null) return d == aliceStickerData;
        return d.stickerType == StickerType.Clothes;
    }

    void OnStickerAdded(StickerData d) {
        if (_started || !IsAliceSticker(d)) return;
        _started = true;
        StartCoroutine(Run());
    }

    [ContextMenu("Debug: Trigger Tutorial")]
    public void DebugTrigger() {
        if (_started) return;
        _started = true;
        StartCoroutine(Run());
    }

    // ====================================================================
    IEnumerator Run() {
        bool completed = false;
        try {
        // Beat0 : 入手の余韻（地面シールの剥がれを見せる）。プレイヤー入力だけ止める。
        if (player != null) player.SetUIMode(true);
        float t0 = 0f;
        while (t0 < preDelay) { t0 += Time.unscaledDeltaTime; yield return null; }

        // 世界停止＋暗転。プレイヤーの Animator は unscaled にして着替え中のスキニング崩れ（引き延ばし）を防ぐ
        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (book != null) book.SetLockOpen(true);
        SetPlayerAnimatorUnscaled(true);

        _spot = SpotlightOverlay.Create(3000, dimAlpha);
        StartCoroutine(_spot.FadeIn(0.35f));

        // ---- Beat1 : オトモは右上のまま。丸がブックアイコンに集まる暗転 ＋ アイコンをその場で拡縮バウンス ----
        RectTransform bookIcon = (bookButtonGroup != null ? bookButtonGroup.Find("Book") as RectTransform : null) ?? bookButtonGroup;
        var bookBtnLift = Lift(bookButtonGroup, popupSortingOrder);        // ブック群は通常色（前面化）
        var iris = StartCoroutine(IrisIn(bookIcon, 1.6f));                 // 丸がブックアイコンにフォーカス
        ShowCompanion(false);
        yield return SlapInCompanion(Vector2.zero);                       // 右上へ画面外から飛来して貼付
        yield return _speaker.SayRoutine(tabPromptText, typeCps);

        Vector3 iconBaseScale = bookIcon != null ? bookIcon.localScale : Vector3.one;
        var bounce = StartCoroutine(AttentionBounce(bookIcon));           // 中心ピボットのアイコンをその場で拡縮
        while (book != null && !book.IsBookOpen) yield return null;        // TAB 待ち
        if (bounce != null) StopCoroutine(bounce);
        if (bookIcon != null) bookIcon.localScale = iconBaseScale;
        if (iris != null) StopCoroutine(iris);
        Unlift(bookBtnLift);

        // ---- Beat2 : オトモを隠して、アリスのハンガー出現 → シール貼り（ゆっくり）。ハンガー+服は前面化で通常色（白い長方形は出さない）----
        HideCompanion();
        if (book != null) book.LockNonClothesTabs(true);
        book.UnlockCostume(aliceClothPrefab, true);
        yield return null;
        int idx = book.VisibleIndexOf(aliceClothPrefab);
        RectTransform cell = (idx >= 0 && book.PageView != null) ? book.PageView.GetCell(idx) : null;
        if (book.PageView != null) book.PageView.GoToPage(idx >= 0 ? idx / Mathf.Max(1, book.PageView.CellsPerPage) : 0);

        _spot.SetTargets();                                               // 穴なし＝均一に暗転。明るくしたい所は前面化で
        if (cell != null) {
            Lift(cell, popupSortingOrder - 20);                           // ハンガー+アリス服だけ通常色（形ぴったり＝長方形の光なし）
            yield return RevealAliceHanger(cell);
        }

        // ---- Beat3 : 左下オトモ「早速着替えてみよう！」＋ アリスの服→3Dプレイヤーへ矢印。プレイヤー枠も前面化 ----
        if (previewFrameRect != null) Lift(previewFrameRect, popupSortingOrder - 20);   // プレイヤー枠を明るく（形ぴったり）
        _spot.SetTargets();
        ShowCompanion(true);                                             // 左下・反転
        yield return SlapInCompanion(bottomLeftOffset);
        if (cell != null && previewFrameRect != null) {
            _arrowCanvas = MakeOverlayCanvas("ArrowCanvas", popupSortingOrder + 40);
            _arrow = ArrowGuide.Create(_arrowCanvas.GetComponent<Canvas>(), arrowColor, popupSortingOrder + 40);
            _arrow.SetEndpoints(cell, previewFrameRect);                // アリスの服 → 右の3Dプレイヤー
        }
        yield return _speaker.SayRoutine(dragPromptText, typeCps);       // 早速着替えてみよう！

        while (book != null && (book.ModelSwitcher == null || book.ModelSwitcher.CurrentClothPrefab != aliceClothPrefab))
            yield return null;

        // ---- Beat4 : 着替え成立。左下オトモそのまま「かわいい！似合ってるよ！」 ----
        if (_arrow != null) { _arrow.Stop(); _arrow.Dismiss(); _arrow = null; }
        if (_arrowCanvas != null) { Destroy(_arrowCanvas); _arrowCanvas = null; }
        yield return _speaker.SayAndHold(congratsText, 1.3f, typeCps);   // かわいい！似合ってるよ！

        // ---- 後始末 ----
        yield return _spot.FadeOut(0.35f);
        HideCompanion();
        if (book != null) book.LockNonClothesTabs(false);
        if (_spot != null)  _spot.Dismiss();
        UnliftAll();

        // アニメータ/スカートを通常更新へ戻すのは、timeScale を戻して dt>0 になった「次フレーム」で行う。
        // 同フレームで戻すと、そのフレームの Time.deltaTime は 0（フレーム開始時 timeScale=0 のまま）なので
        // SkirtDynamicBone.LateUpdate が dt<=0 で冒頭 return し、静止姿勢への戻しが 1 フレーム抜け、
        // unscaled で回っていたアニメの生スカートポーズが一瞬出る＝アリスの服が崩れて見える不具合になる。
        Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
        yield return null;
        SetPlayerAnimatorUnscaled(false);
        if (book != null) book.SetLockOpen(false);
        ClothUnlockState.AliceTutorialDone = true;
        completed = true;
        }
        finally {
            // 例外・中断・シーン遷移などで途中終了しても、世界停止と各種ロックを必ず解除する
            // (timeScale=0 のまま固まる／入力が戻らない重大なソフトロックを防ぐ)
            Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
            SetPlayerAnimatorUnscaled(false);
            if (book != null) book.SetLockOpen(false);
            if (_arrow != null) { _arrow.Stop(); _arrow.Dismiss(); _arrow = null; }
            if (_arrowCanvas != null) { Destroy(_arrowCanvas); _arrowCanvas = null; }
            if (_spot != null) { _spot.Dismiss(); _spot = null; }
            // 正常終了時はブックを開いたまま残す設計のため SetUIMode は触らない。
            // 異常終了時のみ、入力ロックが残らないよう解放する。
            if (!completed && player != null) player.SetUIMode(false);
        }
    }

    // ====================================================================
    //  オトモ表示ヘルパー
    // ====================================================================
    void ShowCompanion(bool flip) {
        SetupCompanionPopup(true);
        _speaker = CompanionSpeaker.AttachTo(companionPopupRoot, companionText, companionFaceSlot, faceSet);
        // フォント割当(AttachTo)でマテリアルが差し替わるため、白文字の縁取りはここで付ける
        if (companionText != null) {
            companionText.outlineWidth = 0.22f;                        // 濃い縁で白文字を背景から浮かせる
            companionText.outlineColor = new Color32(70, 35, 55, 255);
        }
        ApplyPopupFlip(flip);
    }
    void HideCompanion() {
        if (_speaker != null) { _speaker.Detach(); _speaker = null; }
        SetupCompanionPopup(false);
    }

    /// <summary>プレイヤーの Animator を unscaled 更新に（true）／元へ（false）。timeScale=0 中も骨が更新され、着替えたメッシュが伸びない。
    /// 併せてスカート物理(SkirtDynamicBone)も unscaled 化する。これを行わないと timeScale=0 で
    /// LateUpdate が dt=0 のまま走らず、静止姿勢への戻しが効かずアリスの服(スカート)が崩れて見える。</summary>
    void SetPlayerAnimatorUnscaled(bool on) {
        if (on) {
            if (_playerAnimator == null) {
                var pgo = GameObject.FindWithTag("Player");
                if (pgo != null) {
                    _playerAnimator = pgo.GetComponentInChildren<Animator>();
                    _skirtBones     = pgo.GetComponentsInChildren<SkirtDynamicBone>(true);
                }
            }
            if (_playerAnimator != null) { _playerAnimMode = _playerAnimator.updateMode; _playerAnimator.updateMode = AnimatorUpdateMode.UnscaledTime; }
            if (_skirtBones != null) foreach (var s in _skirtBones) if (s != null) s.ForceUnscaledTime = true;
        } else {
            if (_playerAnimator != null) _playerAnimator.updateMode = _playerAnimMode;
            if (_skirtBones != null) foreach (var s in _skirtBones) if (s != null) s.ForceUnscaledTime = false;
        }
    }

    /// <summary>吹き出し(BG・オトモ顔・チェック画像)を吹き出し中心で左右反転。テキストは逆補正で
    /// 読める向きに保つ。BG のミラーによりテキストの寄せも左右自動で入れ替わる。</summary>
    void ApplyPopupFlip(bool flip) {
        float sx = flip ? -1f : 1f;
        const float pivotX = 665f;   // BG 中心＝吹き出しの水平中心（Icon/Say はここを軸にミラー）

        // BG グラフィックを左右反転（尻尾が逆側へ）。子のテキストもこれで鏡像化するので、
        // 局所空間で左寄せにしておけば、非反転=画面左寄せ／反転=画面右寄せ を自動で満たす。
        RectTransform bubble = companionPopupRoot != null ? companionPopupRoot.Find("BookComment_BG") as RectTransform : null;
        if (bubble != null) { var s = bubble.localScale; s.x = Mathf.Abs(s.x) * sx; bubble.localScale = s; }

        // オトモ顔(BookComment_Icon)・チェック画像(BookComment_Say)も位置ミラー＋左右反転
        MirrorChild(companionFaceSlot, flip, pivotX, _iconOrigPos, _iconOrigScale);
        MirrorChild(_sayRect,          flip, pivotX, _sayOrigPos,  _sayOrigScale);

        // テキストは読める向きに（反転時は逆補正）
        if (companionText != null) { var s = companionText.rectTransform.localScale; s.x = Mathf.Abs(s.x) * sx; companionText.rectTransform.localScale = s; }
        ApplyTextLayout();
    }

    /// <summary>子(顔・チェック)を吹き出し中心 pivotX で位置ミラー＋左右反転する／元へ戻す。</summary>
    static void MirrorChild(RectTransform rt, bool flip, float pivotX, Vector2 origPos, Vector3 origScale) {
        if (rt == null) return;
        if (flip) {
            rt.anchoredPosition = new Vector2(2f * pivotX - origPos.x, origPos.y);
            var s = origScale; s.x = -Mathf.Abs(s.x); rt.localScale = s;
        } else {
            rt.anchoredPosition = origPos;
            rt.localScale = origScale;
        }
    }

    /// <summary>セリフ文字のサイズと左右の寄せ（枠からはみ出さない範囲で少し左寄せ）。
    /// BG が反転しているとこの左寄せが画面上では右寄せになるため、両吹き出しで共通で良い。</summary>
    void ApplyTextLayout() {
        if (companionText == null) return;
        var trt = companionText.rectTransform;
        // 左を詰め、右を大きく空けてテキスト枠の中心を左へ寄せる（局所空間）。オトモの顔が枠の
        // 右 1/3 を占めるため、そこを避けて左へ。反転時(左下吹き出し)は BG ミラーで右寄せになる。
        trt.offsetMin = new Vector2(30f, 40f);
        trt.offsetMax = new Vector2(-190f, -34f);
        companionText.fontSizeMin = 28f;
        companionText.fontSizeMax = 50f;   // 以前より少し小さめ
    }

    /// <summary>吹き出しをイントロと同じ「シールを貼る」モーション(UIStickerPaste)で登場させる。
    /// 画面外から定位置(homeOffset)へ飛来しつつ BG に接着シマーを掃引して密着する。</summary>
    IEnumerator SlapInCompanion(Vector2 homeOffset) {
        RectTransform bg = companionPopupRoot != null ? companionPopupRoot.Find("BookComment_BG") as RectTransform : null;
        Image bgImg = bg != null ? bg.GetComponent<Image>() : null;

        Vector3 homeLocal = _popupOrigPos + (Vector3)homeOffset;
        // 画面外から来る向き＝この吹き出しが画面中央からどちら側にあるか
        // (root は Canvas 中央、BG は root から (665,383) の位置にある)
        Vector2 fromDir = new Vector2(homeLocal.x + 665f, homeLocal.y + 383f);

        yield return UIStickerPaste.PasteLive(bgImg, companionPopupRoot, homeLocal, fromDir,
            new UIStickerPaste.Options {
                duration = 0.62f, holdPeel = 0f, overshoot = 1.14f, tiltDeg = 8f,
                shimmer = 1.3f, useUnscaledTime = true
            });
        yield return new WaitForSecondsRealtime(0.08f);
    }
    IEnumerator PopIn(RectTransform rt, float dur) {
        if (rt == null) yield break;
        Vector3 baseS = (rt.localScale == Vector3.zero) ? Vector3.one : rt.localScale;   // 反転(±)も保持
        rt.localScale = new Vector3(baseS.x * 0.4f, baseS.y * 0.4f, baseS.z);
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Lerp(0.4f, 1f, EaseOutBack(Mathf.Clamp01(t / dur)));
            rt.localScale = new Vector3(baseS.x * k, baseS.y * k, baseS.z);
            yield return null;
        }
        rt.localScale = baseS;
    }

    /// <summary>画面外の大きな丸が縮小して対象へ集まる暗転（丸の中だけ明るい）。縮小後は保持。</summary>
    IEnumerator IrisIn(RectTransform center, float dur) {
        if (_spot == null || center == null) yield break;
        const float bigR = 2800f, smallR = 230f;
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            float r = Mathf.Lerp(bigR, smallR, EaseInOutCubic(Mathf.Clamp01(t / dur)));
            var tgt = new SpotlightOverlay.Target(center); tgt.circleRadius = r; tgt.softness = 60f;
            _spot.SetTargets(tgt);
            yield return null;
        }
        var hold = new SpotlightOverlay.Target(center); hold.circleRadius = smallR; hold.softness = 60f;
        _spot.SetTargets(hold);
    }

    /// <summary>拡縮で「バイン」と一定間隔で弾む（TAB 待ちの間ループ）。位置は動かさない。</summary>
    IEnumerator AttentionBounce(RectTransform rt) {
        if (rt == null) yield break;
        Vector3 baseS = rt.localScale;
        float t = 0f;
        while (true) {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Repeat(t, 0.9f);
            float boing = Mathf.Exp(-x * 6f) * Mathf.Sin(x * 22f);   // 区間頭でバインと減衰振動
            rt.localScale = baseS * (1f + 0.22f * boing);
            yield return null;
        }
    }

    /**=======================================================================
      既存 RigthUp ポップアップの前面化(true)／復元(false)
    =======================================================================**/
    void SetupCompanionPopup(bool on) {
        if (companionPopup == null || companionPopupRoot == null) return;
        if (on) {
            companionPopup.SetActive(true);
            _popupOrigPos   = companionPopupRoot.localPosition;
            _popupOrigScale = companionPopupRoot.localScale;

            _popupCanvas = companionPopupRoot.GetComponent<Canvas>();
            if (_popupCanvas == null) {
                _popupCanvas = companionPopupRoot.gameObject.AddComponent<Canvas>();
                _popupRay    = companionPopupRoot.gameObject.AddComponent<GraphicRaycaster>();
                _addedCanvas = true;
            }
            _popupCanvas.overrideSorting = true;
            _popupCanvas.sortingOrder    = popupSortingOrder;   // 暗転より前面

            if (companionText != null) {
                var trt = companionText.rectTransform;
                _textOrigScale = trt.localScale;
                _textOrigColor = companionText.color;
                _textAncMin = trt.anchorMin; _textAncMax = trt.anchorMax;
                _textOffMin = trt.offsetMin; _textOffMax = trt.offsetMax;
                // 吹き出しいっぱいに広げて「縦長・はみ出し」を解消（元は 200x50・y=3 スケールで縦に伸びていた）
                trt.localScale = Vector3.one;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                companionText.enableAutoSizing = true;
                // 左右の寄せ・フォントサイズは ApplyTextLayout（反転対応）で設定する
                companionText.fontStyle = FontStyles.Bold;
                companionText.alignment = TextAlignmentOptions.Center;
                // 折り返しを切り、改行(\n)の位置だけで行が決まるようにする。狭い枠でも
                // 「TABキーを押して／シール帳を開こう！」等をきっちり2行で表示（勝手な3行化を防ぐ）。
                companionText.textWrappingMode = TextWrappingModes.NoWrap;
                companionText.color = Color.white;                                     // セリフは白（縁取りは AttachTo 後に付与）
                companionText.maxVisibleCharacters = 0;
            }
            _faceSlotImg = companionFaceSlot != null ? companionFaceSlot.GetComponent<Image>() : null;
            if (_faceSlotImg != null) { _faceSlotImgWasEnabled = _faceSlotImg.enabled; _faceSlotImg.enabled = false; }

            // 反転(左下吹き出し)の復元用に、オトモ顔・チェック画像の元位置/スケールを控える
            _sayRect = companionPopupRoot.Find("BookComment_Say") as RectTransform;
            if (companionFaceSlot != null) { _iconOrigPos = companionFaceSlot.anchoredPosition; _iconOrigScale = companionFaceSlot.localScale; }
            if (_sayRect != null)          { _sayOrigPos  = _sayRect.anchoredPosition;          _sayOrigScale  = _sayRect.localScale; }
        } else {
            if (companionText != null) {
                var trt = companionText.rectTransform;
                companionText.text = "";
                companionText.maxVisibleCharacters = int.MaxValue;
                trt.localScale = _textOrigScale;
                trt.anchorMin = _textAncMin; trt.anchorMax = _textAncMax;
                trt.offsetMin = _textOffMin; trt.offsetMax = _textOffMax;
                companionText.color = _textOrigColor;
            }
            var bubbleR = companionPopupRoot.Find("BookComment_BG") as RectTransform;
            if (bubbleR != null) { var bs = bubbleR.localScale; bs.x = Mathf.Abs(bs.x); bubbleR.localScale = bs; }   // 反転を戻す
            // オトモ顔・チェック画像のミラーを元へ戻す
            if (companionFaceSlot != null) { companionFaceSlot.anchoredPosition = _iconOrigPos; companionFaceSlot.localScale = _iconOrigScale; }
            if (_sayRect != null)          { _sayRect.anchoredPosition = _sayOrigPos;          _sayRect.localScale = _sayOrigScale; }
            if (_faceSlotImg != null) _faceSlotImg.enabled = _faceSlotImgWasEnabled;
            companionPopupRoot.localPosition = _popupOrigPos;
            companionPopupRoot.localScale    = _popupOrigScale == Vector3.zero ? Vector3.one : _popupOrigScale;
            if (_popupCanvas != null) {
                if (_addedCanvas) { if (_popupRay != null) Destroy(_popupRay); Destroy(_popupCanvas); }
                else _popupCanvas.overrideSorting = false;
            }
            companionPopup.SetActive(false);
        }
    }

    /**=======================================================================
      ハンガー枠が「チャカチャカ」出現 → その上に服シールが貼られる
    =======================================================================**/
    IEnumerator RevealAliceHanger(RectTransform cell) {
        var art = cell.Find("Art") as RectTransform;
        Image artImg = art != null ? art.GetComponent<Image>() : null;
        if (artImg != null) artImg.enabled = false;

        Quaternion baseRot = cell.localRotation;
        cell.localScale = Vector3.zero;
        float t = 0f, dur = 0.85f;                                     // ゆっくり出現
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            cell.localScale = Vector3.one * EaseOutBack(p);
            float env = Mathf.Exp(-3.2f * p);
            cell.localRotation = baseRot * Quaternion.Euler(0, 0, Mathf.Sin(p * 42f) * 11f * env);
            yield return null;
        }
        cell.localScale = Vector3.one;
        t = 0f; float shakeDur = 0.7f;                                 // チャカチャカ長め
        while (t < shakeDur) {
            t += Time.unscaledDeltaTime;
            float p = t / shakeDur;
            float env = Mathf.Exp(-4f * p) * (1f - p);
            cell.localRotation = baseRot * Quaternion.Euler(0, 0, Mathf.Sin(p * 40f) * 9f * env);
            yield return null;
        }
        cell.localRotation = baseRot;
        yield return new WaitForSecondsRealtime(0.35f);               // 少し間を置いて見せる

        if (artImg != null) {
            artImg.enabled = true;
            // イントロと同じ「シールを貼る」モーション（UIStickerPaste）で貼付。ページのマスク内なので
            // 飛来距離は短め。単一スプライト(FullRect想定)なので角折り(_Peel)も効かせる。art は上で取得済み。
            yield return UIStickerPaste.PasteLive(artImg, art, art.localPosition, new Vector2(-0.5f, 1f),
                new UIStickerPaste.Options {
                    duration = 0.9f, holdPeel = 0.45f, overshoot = 1.16f, tiltDeg = 10f,
                    shimmer = 1.3f, exitDist = 180f, useUnscaledTime = true
                });
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }

    // ====================================================================
    //  暗転より前面へ持ち上げる（overrideSorting）汎用ヘルパー
    // ====================================================================
    class LiftToken { public RectTransform rt; public Canvas canvas; public GraphicRaycaster ray; public bool added; public bool prevOverride; public int prevOrder; }

    LiftToken Lift(RectTransform rt, int order) {
        if (rt == null) return null;
        var lt = new LiftToken { rt = rt };
        lt.canvas = rt.GetComponent<Canvas>();
        if (lt.canvas == null) {
            lt.canvas = rt.gameObject.AddComponent<Canvas>();
            lt.ray    = rt.gameObject.AddComponent<GraphicRaycaster>();
            lt.added  = true;
        } else { lt.prevOverride = lt.canvas.overrideSorting; lt.prevOrder = lt.canvas.sortingOrder; }
        lt.canvas.overrideSorting = true;
        lt.canvas.sortingOrder    = order;
        _lifts.Add(lt);
        return lt;
    }
    void Unlift(LiftToken lt) {
        if (lt == null) return;
        _lifts.Remove(lt);
        if (lt.canvas == null) return;
        if (lt.added) { if (lt.ray != null) Destroy(lt.ray); Destroy(lt.canvas); }
        else { lt.canvas.overrideSorting = lt.prevOverride; lt.canvas.sortingOrder = lt.prevOrder; }
    }
    void UnliftAll() { for (int i = _lifts.Count - 1; i >= 0; i--) Unlift(_lifts[i]); }

    // ====================================================================
    GameObject MakeOverlayCanvas(string name, int order) {
        var go = new GameObject(name);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    static float EaseOutCubic(float x)   => 1f - Mathf.Pow(1f - x, 3f);
    static float EaseInOutCubic(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

    static float EaseOutBack(float x) {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
