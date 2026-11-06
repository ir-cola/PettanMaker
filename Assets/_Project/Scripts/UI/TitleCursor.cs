/**===========================================================================

 File   : タイトル・カーソル制御 [TitleCursor.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 タイトルのメニュー選択カーソル。

 - 入力: W / ↑ で上、S / ↓ で下 (長押しリピート)。マウスホバーでも選択追従、
         Enter / Space / 左クリックで決定 (Unity Input System を直接クエリ)。
 - 選択が変わると: ローラーが選択肢の左端へワープ → 左→右へ動いて線を引く。
   下のピンク筆跡 (TitleHighlight) を _Fill 0→1 で [左端, ローラー位置] に塗り広げる。
 - 筆跡は平行四辺形(左右の辺が斜め)で、左ほど太く・右ほど細いテーパー (形状はシェーダー側)。

 ※ ハイライトの位置/サイズ/角度は選択肢から毎フレーム同期。塗り進み(_Fill)と
   ローラー位置をアニメーションさせる。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteAlways]   // エディタでも帯/ローラーを選択肢に同期し、Play 時とサイズを一致させる
public class TitleCursor : MonoBehaviour {

    public enum MenuAction { Start, Option, End }

    [System.Serializable]
    public class MenuEntry {
        [Tooltip("選択肢の RectTransform (START / OPTION ...)。位置・サイズの基準。")]
        public RectTransform target;
        [Tooltip("決定時に呼ぶ TitleManager のアクション。")]
        public MenuAction action = MenuAction.Start;
    }

    [Header("参照")]
    [SerializeField] private TitleManager titleManager;
    [SerializeField] private TitleHighlight highlight;       // ピンク筆跡 (シェーダー描画)
    [SerializeField] private TitleHighlight shadowHighlight; // 黒の重ね影 (別オブジェクトで別々に揺れる)。任意
    [SerializeField] private RectTransform roller;           // ローラー (roller.png)
    [SerializeField] private MenuEntry[] menu;               // 上から順に

    [Header("重ね影")]
    [Tooltip("影のピクセルオフセット(x:右+ / y:下-)。")]
    [SerializeField] private Vector2 shadowPixelOffset = new Vector2(8f, -12f);

    [Header("ハイライト配置")]
    [Tooltip("選択肢サイズに対する余白(px)。筆跡の帯の大きさ(平行四辺形の傾き分だけ横は広め)。")]
    [SerializeField] private Vector2 highlightPadding = new Vector2(240f, 70f);
    [Tooltip("選択肢中心からのローカルオフセット(px)。")]
    [SerializeField] private Vector2 highlightOffset = new Vector2(0f, 0f);

    [Header("ローラー配置")]
    [Tooltip("帯の中心からローラーを上下にずらす量(px)。帯の上に乗る感じ。")]
    [SerializeField] private float rollerYOffset = 34f;
    [Tooltip("引いてる端からローラーをさらに右へずらす量(px)。")]
    [SerializeField] private float rollerEdgeGap = 12f;

    [Header("塗りアニメ (左ワープ→右へ引く)")]
    [Tooltip("左端から右端まで引き切る時間(秒)。小さいほど速い。")]
    [SerializeField] private float paintDuration = 0.28f;

    [Header("キー入力リピート")]
    [SerializeField] private float repeatDelay = 0.4f;
    [SerializeField] private float repeatInterval = 0.12f;

    [Header("ホバー判定")]
    [Tooltip("ホバー/クリック判定をテキスト矩形から広げる量(px)。ピンク帯の見た目 " +
        "(highlightPadding=240x70) に近づけるほど、カーソルが帯に触れた瞬間に反応する")]
    [SerializeField] private Vector2 hoverPadding = new Vector2(200f, 60f);

    [Header("SE (選択の貼り替え)")]
    [Tooltip("線を貼るときの SE。未設定ならエディタ実行時は Sticker_Paste を自動ロード")]
    [SerializeField] private AudioClip pasteSe;
    [Tooltip("前の線を剥がすときの SE。未設定ならエディタ実行時は Sticker_PeelOff_2 を自動ロード")]
    [SerializeField] private AudioClip peelSe;
    [Tooltip("SE 音量。1 超は増幅 (素材が小さいとき用。上げすぎると音割れ)")]
    [Range(0f, 2f)]
    [SerializeField] private float seVolume = 1.2f;
    [Tooltip("剥がし SE から貼り SE までの間 (秒)。0 で同時")]
    [SerializeField] private float pasteSeDelay = 0.02f;
    [Tooltip("剥がし SE の頭をスキップする秒数。mp3 先頭の無音で発音が遅れるときに上げる")]
    [SerializeField] private float peelSeTrim = 0f;

    // --- ランタイム状態 ---
    private int   selectedIndex = 0;
    private float paintT = 1f;        // 0..1 (塗り進行の素時間)
    private float fillCurrent = 1f;   // イージング後の _Fill
    private Camera uiCamera;          // ScreenSpaceOverlay なら null

    private float navTimer;
    private int   navDir;
    private Vector2 lastMousePos;
    private bool  initialized;
    private AudioSource seSource;
    private AudioSource peelSource;   // 剥がし SE の頭スキップ再生用

    private void Start() {
        if (!Application.isPlaying) return;   // エディタ同期は Update(SyncEditorPreview) が担当
        ResolveUICamera();
        if (menu == null || menu.Length == 0) {
            Debug.LogWarning("[TitleCursor] menu が未設定です。");
            enabled = false;
            return;
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, menu.Length - 1);
        // 初期表示は塗り済み (左端から右端まで)。
        paintT = 1f; fillCurrent = 1f;
        ApplyVisual(snap: true);
        if (Mouse.current != null) lastMousePos = Mouse.current.position.ReadValue();
        initialized = true;
    }

    private void Update() {
        if (!Application.isPlaying) { SyncEditorPreview(); return; }   // 非Play: 静的プレビュー
        if (!initialized) return;
        HandleNavigation();
        HandleMouseHover();
        HandleConfirm();
    }

    private void LateUpdate() {
        if (!Application.isPlaying || !initialized) return;
        // 先に現在の paintT を描画 → その後に進める。これで選択直後の paintT=0 (端=塗り0) を
        // 必ず1フレーム見せ、「一定サイズが一瞬で出る」のを防ぐ (端からニュッと伸びる)。
        ApplyVisual(snap: false);
        if (paintT < 1f) {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            paintT = Mathf.Min(1f, paintT + dt / Mathf.Max(0.01f, paintDuration));
        }
    }

    /// <summary>非Play時: 選択肢(既定0)に合わせて帯/ローラーを塗り済み状態で配置し、Play時と一致させる。</summary>
    private void SyncEditorPreview() {
        if (menu == null || menu.Length == 0 || highlight == null) return;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, menu.Length - 1);
        ResolveUICamera();
        paintT = 1f;
        ApplyVisual(snap: true);
    }

    // ===== 入力 =====

    private void HandleNavigation() {
        var kb = Keyboard.current;
        var gp = Gamepad.current;
        bool up   = (kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed));
        bool down = (kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed));
        if (gp != null) {
            up   |= gp.dpad.up.isPressed   || gp.leftStick.ReadValue().y >  0.5f;
            down |= gp.dpad.down.isPressed || gp.leftStick.ReadValue().y < -0.5f;
        }
        int dir = (down ? 1 : 0) - (up ? 1 : 0);

        if (dir == 0) { navDir = 0; navTimer = 0f; return; }
        if (dir != navDir) {            // 押した瞬間
            navDir = dir; navTimer = repeatDelay;
            MoveSelection(dir);
            return;
        }
        navTimer -= Time.unscaledDeltaTime;
        if (navTimer <= 0f) { navTimer = repeatInterval; MoveSelection(dir); }
    }

    private void HandleMouseHover() {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 mpos = mouse.position.ReadValue();
        if ((mpos - lastMousePos).sqrMagnitude < 1f) return;   // 動いていなければキー選択を尊重
        lastMousePos = mpos;
        for (int i = 0; i < menu.Length; i++) {
            var rt = menu[i].target;
            if (rt == null) continue;
            if (HoverContains(rt, mpos)) {
                if (i != selectedIndex) Select(i);
                break;
            }
        }
    }

    // テキスト矩形を hoverPadding ぶん広げて判定する。
    // 見た目のピンク帯はテキストより大きい (highlightPadding) ため、素の矩形だと
    // 「帯に重なっているのに反応しない」遅れが出る。
    private bool HoverContains(RectTransform rt, Vector2 screenPos) {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCamera, out Vector2 lp))
            return false;
        Rect r = rt.rect;
        r.xMin -= hoverPadding.x * 0.5f; r.xMax += hoverPadding.x * 0.5f;
        r.yMin -= hoverPadding.y * 0.5f; r.yMax += hoverPadding.y * 0.5f;
        return r.Contains(lp);
    }

    private void HandleConfirm() {
        // 左クリック: クリックした選択肢を選択し直して決定 (見えているハイライトと必ず一致させる)。
        // ※ シーンの Button は無効化済みで、決定の単一権限はこの TitleCursor が持つ。
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) {
            Vector2 mpos = mouse.position.ReadValue();
            for (int i = 0; i < menu.Length; i++) {
                var rt = menu[i].target;
                if (rt != null && HoverContains(rt, mpos)) {   // 判定はホバーと同じ拡張矩形
                    Select(i);
                    Confirm();
                    return;
                }
            }
        }
        // キー決定: 現在の選択を決定。ゲームパッドは A / Start。
        var kb = Keyboard.current;
        bool keyConfirm = kb != null &&
            (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
             kb.spaceKey.wasPressedThisFrame);
        var gp = Gamepad.current;
        if (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame))
            keyConfirm = true;
        if (keyConfirm) Confirm();
    }

    private void MoveSelection(int dir) {
        int n = menu.Length;
        Select(((selectedIndex + dir) % n + n) % n);   // ラップアラウンド
    }

    private void Select(int index) {
        if (index == selectedIndex) return;
        selectedIndex = index;
        // 塗り直し: ローラーを左端へワープ、左→右へ再度引く。
        paintT = 0f; fillCurrent = 0f;
        // SE: 前の選択肢の線を「ペリッ」と剥がし → 少し置いて新しい線を「ペタッ」と貼る
        PlaySelectSounds();
    }

    // ===== SE =====

    private void PlaySelectSounds() {
        EnsureAudio();
        if (seSource == null) return;

        if (peelSe != null) {
            // mp3 の頭に無音があると発音が遅れて聞こえる。trim 指定時はシーク再生で頭を飛ばす
            // (PlayOneShot はシークできないため専用ソースを使う)。
            if (peelSeTrim > 0f && peelSeTrim < peelSe.length) {
                if (peelSource == null) {
                    peelSource = gameObject.AddComponent<AudioSource>();
                    peelSource.playOnAwake = false;
                    peelSource.spatialBlend = 0f;
                }
                peelSource.clip = peelSe;
                peelSource.volume = seVolume;
                peelSource.time = peelSeTrim;
                peelSource.Play();
            }
            else {
                seSource.PlayOneShot(peelSe, seVolume);
            }
        }

        if (pasteSe != null) {
            if (pasteSeDelay <= 0f) seSource.PlayOneShot(pasteSe, seVolume);
            else StartCoroutine(PlayDelayed(pasteSe, pasteSeDelay));
        }
    }

    private System.Collections.IEnumerator PlayDelayed(AudioClip clip, float delay) {
        yield return new WaitForSecondsRealtime(delay);
        if (seSource != null && clip != null) seSource.PlayOneShot(clip, seVolume);
    }

    private void EnsureAudio() {
        if (seSource == null) {
            seSource = GetComponent<AudioSource>();
            if (seSource == null) seSource = gameObject.AddComponent<AudioSource>();
            seSource.playOnAwake = false;
            seSource.spatialBlend = 0f;   // 2D
        }
#if UNITY_EDITOR
        // Inspector 未設定でもエディタ実行では鳴るようにする (ビルドは Inspector 割り当てを推奨)
        if (pasteSe == null) pasteSe = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/_Project/Sound/SE/UI/Sticker_Paste.wav");
        if (peelSe == null) peelSe = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/_Project/Sound/SE/UI/Sticker_PeelOff_2.mp3");
#endif
    }

    private void Confirm() {
        if (titleManager == null) { Debug.LogWarning("[TitleCursor] titleManager 未設定。"); return; }
        switch (menu[selectedIndex].action) {
            case MenuAction.Start:  titleManager.OnClickStart();  break;
            case MenuAction.Option: titleManager.OnClickOption(); break;
            case MenuAction.End:    titleManager.OnClickEnd();    break;
        }
    }

    // ===== 描画反映 =====

    private void ApplyVisual(bool snap) {
        if (menu == null || selectedIndex < 0 || selectedIndex >= menu.Length) return;
        RectTransform opt = menu[selectedIndex].target;
        if (opt == null) return;

        float angle    = opt.localEulerAngles.z;
        Vector2 size   = opt.sizeDelta + highlightPadding;
        Vector2 center = opt.anchoredPosition + Rotate(highlightOffset, angle);

        // ハイライト帯を選択肢へ同期
        if (highlight != null) {
            RectTransform hlRT = highlight.rectTransform;
            hlRT.anchoredPosition = center;
            hlRT.sizeDelta        = size;
            hlRT.localEulerAngles = new Vector3(0f, 0f, angle);
            fillCurrent = snap ? paintT : SmoothStep(paintT);
            highlight.Fill = fillCurrent;
        } else {
            fillCurrent = snap ? paintT : SmoothStep(paintT);
        }

        // 黒の重ね影: ピンクと同じ矩形をオフセットして配置・同じ塗り進み。
        // 揺れ(頂点アニメ)は影オブジェクト側で独立して走る → ピンクと別々に動く。
        if (shadowHighlight != null) {
            RectTransform srt = shadowHighlight.rectTransform;
            srt.anchoredPosition = center + Rotate(shadowPixelOffset, angle);
            srt.sizeDelta        = size;
            srt.localEulerAngles = new Vector3(0f, 0f, angle);
            shadowHighlight.Fill = fillCurrent;
        }

        // ローラー = 引いてる右端 (左→右へ動いて線を引く)。
        // シェーダーの平行四辺形 inset と一致させるため [inset, 1-inset] を線形補間。
        if (roller != null) {
            float fullW = size.x;
            float inset = (highlight != null) ? 0.5f * Mathf.Abs(highlight.TailSlant) : 0f;
            float uvX = Mathf.Lerp(inset, 1f - inset, fillCurrent);
            float leadX = (uvX - 0.5f) * fullW + rollerEdgeGap;
            roller.anchoredPosition = center + Rotate(new Vector2(leadX, rollerYOffset), angle);
        }
    }

    // ===== ヘルパ =====

    // 初速ゼロ(端からニュッと立ち上がり)→ 中盤で速く → 終端でなめらかに止まる。
    private static float SmoothStep(float t) {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static Vector2 Rotate(Vector2 v, float degZ) {
        float r = degZ * Mathf.Deg2Rad;
        float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private void ResolveUICamera() {
        var canvas = highlight != null ? highlight.canvas
                   : (roller != null ? roller.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>());
        uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
    }
}
