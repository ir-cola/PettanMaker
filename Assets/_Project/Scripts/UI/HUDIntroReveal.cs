/**===========================================================================

 File   : HUD イントロ演出 [HUDIntroReveal.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/7 (同日 飛来方式を廃止し、シールトランジション同様の
                    「キャプチャ→ペリッと貼る」方式へ全面変更)
 -----------------------------------------------------------------------------
 ステージ入場イントロの間 HUD (Canvas/HUD) を非表示にし、
 「シロウサギをつかまえろ！」バナー (StampTextBanner) が消えたタイミングで

   1. HUD の各グループ (LeftDown / RightDown / RightMiddle / RigthUp) を
      シールとして画面へ「ペリッと貼る」。StickerTransition.PlayApply と
      同じ方式: 各グループを RenderTexture にキャプチャし、全画面 RawImage +
      UI/StickerPasteHUD (型抜き対応の折り返しシェーダー) で _Peel 1→0。
   2. その後、HPハートだけ左から1枚ずつ小さなシールとして
      「ペタペタペタッ」と貼っていく。

 キャプチャは HUD の親 Canvas を一時的に ScreenSpaceCamera + RT カメラへ
 切り替えて行う (画面には映らない)。各要素の分離は CanvasRenderer.SetAlpha
 (階層に影響せず OnEnable も発火しない) で行う。

 StageIntroSequence.EnsureSetup から HideForIntro() で起動する。
 シーンへの配置は不要。コールアウトが無い構成でもイントロ終了で必ず表示に戻る。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class HUDIntroReveal : MonoBehaviour {

    // ---- 調整値 (実行時生成のため定数) ----
    const float GroupPasteTime = 0.9f;    // 1グループの貼り付け時間 (秒)
    const float GroupStagger   = 0f;      // グループごとの時差 (0=全シール同時に貼る)
    const float HeartPasteTime = 0.34f;   // ハート1枚の貼り付け時間
    const float HeartStagger   = 0.08f;   // ハート1枚ごとの時差 (パラララ感)

    static readonly int PeelId = Shader.PropertyToID("_Peel");

    CanvasGroup _group;
    bool _hidden;
    bool _played;
    bool _manual;   // true = バナー監視による自動復帰をせず、RevealNow() を待つ

    /**=======================================================================
      イントロ開始時に呼ぶ。HUD を即座に非表示にし、復帰タイミングを監視する。
    =======================================================================**/
    public static void HideForIntro() {
        GameObject hud = FindHudRoot();
        if (hud == null) return;
        var r = hud.GetComponent<HUDIntroReveal>();
        if (r == null) r = hud.AddComponent<HUDIntroReveal>();
        r.HideNow(false);
    }

    /**=======================================================================
      ボス登場演出など、StageIntroSequence / StampTextBanner を伴わない
      場面から使う入口。自動復帰の監視を行わず、RevealNow() が呼ばれるまで
      隠したままにする。
      (HideForIntro をそのまま使うと、バナーも StageIntroSequence も無い構成では
       Update の noBannerFallback が即成立して、その場で貼り付けが始まってしまう)
    =======================================================================**/
    public static void HideUntilRevealed() {
        GameObject hud = FindHudRoot();
        if (hud == null) return;
        var r = hud.GetComponent<HUDIntroReveal>();
        if (r == null) r = hud.AddComponent<HUDIntroReveal>();
        r.HideNow(true);
    }

    /**=======================================================================
      HideUntilRevealed() で隠した HUD を、今から貼り付け演出で出す。
      ボス演出の完了時に呼ぶ。
    =======================================================================**/
    public static void RevealNow() {
        GameObject hud = FindHudRoot();
        if (hud == null) return;
        var r = hud.GetComponent<HUDIntroReveal>();
        if (r == null) return;
        r.PlayNow();
    }

    void PlayNow() {
        if (_played || !_hidden) return;
        _played = true;
        StartCoroutine(RevealSequence());
    }

    static GameObject FindHudRoot() {
        var go = GameObject.Find("Canvas/HUD");
        if (go != null) return go;
        foreach (var canvas in FindObjectsOfType<Canvas>()) {
            var t = canvas.transform.Find("HUD");
            if (t != null) return t.gameObject;
        }
        return null;
    }

    void HideNow(bool manual) {
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _hidden = true;
        _played = false;
        _manual = manual;
    }

    void Update() {
        if (!_hidden || _played) return;
        if (_manual) return;   // 手動復帰モード: RevealNow() が来るまで何もしない

        // バナーが消えた瞬間に貼り付け開始。バナー無し構成はイントロ終了で開始。
        bool bannerVanished = StampTextBanner.AnyShownYet && StampTextBanner.ActiveCount == 0;
        bool noBannerFallback = !StageIntroSequence.InProgress && !StampTextBanner.AnyShownYet;
        if (bannerVanished || noBannerFallback) {
            _played = true;
            StartCoroutine(RevealSequence());
        }
    }

    /**=======================================================================
      本体: キャプチャ → グループを順に貼る → ハートをパラララと貼る
    =======================================================================**/
    IEnumerator RevealSequence() {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) { _group.alpha = 1f; yield break; }

        // --- 対象収集 ---
        // シール1枚の単位 = "Sticker_UI_*" (ユーザーがシーンでグルーピング。番号順に貼る)。
        // 見つからない構成では HUD 直下の子をシール単位とするフォールバック。
        var groups = new List<RectTransform>();
        foreach (var tr in GetComponentsInChildren<RectTransform>(true))
            if (tr.gameObject.activeInHierarchy && tr.name.StartsWith("Sticker_UI")) groups.Add(tr);
        groups.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (groups.Count == 0)
            foreach (Transform ch in transform)
                if (ch.gameObject.activeSelf && ch is RectTransform rt) groups.Add(rt);

        var hearts = new List<RectTransform>();
        var hp = GetComponentInChildren<HPDisplay>(true);
        Transform heartsRoot = (hp != null) ? hp.transform : null;
        if (heartsRoot != null) {
            foreach (Transform h in heartsRoot) {
                var img = h.GetComponent<Image>();
                if (img != null && img.enabled && h.gameObject.activeSelf && h is RectTransform hrt)
                    hearts.Add(hrt);
            }
            hearts.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
        }

        // 切り出し矩形は「Overlay モードのうちに」確定する。
        // ScreenSpaceCamera へ切替えた後だと UI のワールド座標系が変わり
        // (カメラ錐台に合わせて縮む)、矩形が豆粒サイズに壊れる。
        int totalCount = groups.Count + hearts.Count;
        var rects = new Rect[totalCount];
        for (int i = 0; i < groups.Count; i++) rects[i] = ContentScreenRect(groups[i]);
        for (int i = 0; i < hearts.Count; i++) rects[groups.Count + i] = ContentScreenRect(hearts[i]);

        // --- キャプチャ準備: 親 Canvas を一時的に RT カメラ描画へ切替 (画面には映らない) ---
        // 中断（例外・シーン遷移・HUD破棄）されても Canvas モード・一時カメラ・RT を必ず
        // 復元/破棄するため try/finally で囲う（ScreenSpaceCamera のまま残ると HUD が映らなくなる）。
        var prevMode = rootCanvas.renderMode;
        var prevCam  = rootCanvas.worldCamera;
        GameObject camGo = null;
        RenderTexture sharedRT = null;
        Graphic[] allGraphics = null;
        Texture2D[] stickers = null;
        try {
        camGo = new GameObject("HUDIntroReveal_CaptureCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << rootCanvas.gameObject.layer;
        cam.depth = -100f;
        // 透明部分がシールで「真っ黒」になるのを防ぐ。クリア色 (α=0) を RT に
        // そのまま焼きたいが、URP のポストプロセス (UberPost) は出力 α を 1 に潰す。
        // HDR / MSAA も中間テクスチャ経由で α を壊し得るため、全て無効化して素通しにする。
        cam.allowHDR  = false;
        cam.allowMSAA = false;
        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null) {
            camData.renderPostProcessing = false;
            camData.antialiasing         = AntialiasingMode.None;
            camData.renderShadows        = false;
            camData.requiresColorOption  = CameraOverrideOption.Off;
            camData.requiresDepthOption  = CameraOverrideOption.Off;
            camData.volumeLayerMask      = 0;   // シーンの Volume (ポスト) を一切拾わない
        }

        rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        rootCanvas.worldCamera = cam;
        rootCanvas.planeDistance = 1f;

        _group.alpha = 1f;   // RT に描くため表示状態へ (Overlay ではないので画面には出ない)

        // 分離用: HUD 配下の全 Graphic (CanvasRenderer.SetAlpha は階層非依存・副作用なし)
        allGraphics = GetComponentsInChildren<Graphic>(true);

        int total = totalCount;

        // 共有のフルスクリーン RT に1枚ずつ描き、シール部分だけ小テクスチャへ GPU コピー。
        // (札ごとにフルスクリーン RT を確保すると数十MB の確保スタッターでカクつく)
        sharedRT = new RenderTexture(Screen.width, Screen.height, 16, RenderTextureFormat.ARGB32);
        cam.targetTexture = sharedRT;
        stickers = new Texture2D[total];

        for (int i = 0; i < total; i++) {
            bool isHeart = i >= groups.Count;
            Transform target = isHeart ? (Transform)hearts[i - groups.Count] : groups[i];

            SetGraphicsAlpha(allGraphics, 0f);
            foreach (var g in target.GetComponentsInChildren<Graphic>(true))
                g.canvasRenderer.SetAlpha(1f);
            // グループ札にハートは含めない (ハートはフェーズ2で1枚ずつ貼る)
            if (!isHeart && heartsRoot != null && heartsRoot.IsChildOf(target))
                foreach (var g in heartsRoot.GetComponentsInChildren<Graphic>(true))
                    g.canvasRenderer.SetAlpha(0f);

            var rc = rects[i];
            int x = Mathf.Clamp((int)rc.x, 0, sharedRT.width - 2);
            int y = Mathf.Clamp((int)rc.y, 0, sharedRT.height - 2);
            int w = Mathf.Clamp((int)rc.width, 2, sharedRT.width - x);
            int hgt = Mathf.Clamp((int)rc.height, 2, sharedRT.height - y);
            rects[i] = new Rect(x, y, w, hgt);

            // URP はカメラ→RT で α を 1 に潰す (post を切っても残る) ため α チャンネルは信用しない。
            // 黒背景と白背景で2回焼き、RGB の差から α を復元する:
            //   黒背景: rgb = α·色 (プリマルチプライド)   白背景: rgb = α·色 + (1-α)
            //   ⇒ (白 - 黒) = (1-α) なので α = 1 - (白-黒)。URP が α に何を書こうと無関係。
            cam.backgroundColor = Color.black;                  // (0,0,0,1)
            yield return new WaitForEndOfFrame();
            var texBlack = ReadRegion(sharedRT, x, y, w, hgt);

            cam.backgroundColor = Color.white;                  // (1,1,1,1)
            yield return new WaitForEndOfFrame();
            var texWhite = ReadRegion(sharedRT, x, y, w, hgt);

            var cb = texBlack.GetPixels();
            var cw = texWhite.GetPixels();
            for (int k = 0; k < cb.Length; k++) {
                float oneMinusA = Mathf.Clamp01(((cw[k].r - cb[k].r)
                                               + (cw[k].g - cb[k].g)
                                               + (cw[k].b - cb[k].b)) / 3f);
                cb[k].a = 1f - oneMinusA;      // premul RGB は黒背景キャプチャ、α だけ差分から復元
            }
            var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
            tex.SetPixels(cb);
            tex.Apply(false);
            Destroy(texBlack);
            Destroy(texWhite);
            stickers[i] = tex;
        }

        // --- キャプチャ終了: Canvas を元へ戻し、実UIはグループ単位で隠して待機 ---
        cam.targetTexture = null;
        }
        finally {
            // 正常・中断どちらでも Canvas を元のモードへ戻し、一時カメラ/RT を必ず破棄する。
            if (allGraphics != null) SetGraphicsAlpha(allGraphics, 1f);
            if (rootCanvas != null) {
                rootCanvas.renderMode = prevMode;
                rootCanvas.worldCamera = prevCam;
            }
            if (camGo != null) Destroy(camGo);
            if (sharedRT != null) { sharedRT.Release(); Destroy(sharedRT); }
        }

        var groupCgs = new List<CanvasGroup>();
        foreach (var g in groups) groupCgs.Add(EnsureCg(g.gameObject, 0f));
        var heartCgs = new List<CanvasGroup>();
        foreach (var h in hearts) heartCgs.Add(EnsureCg(h.gameObject, 0f));
        CanvasGroup heartsRootCg = (heartsRoot != null) ? EnsureCg(heartsRoot.gameObject, 1f) : null;

        // --- 貼り付け表示用の最前面 Canvas ---
        var stickerCanvasGo = new GameObject("HUDIntroReveal_Stickers");
        var stickerCanvas = stickerCanvasGo.AddComponent<Canvas>();
        stickerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        stickerCanvas.sortingOrder = 120;

        Material baseMat = Resources.Load<Material>("M_StickerPasteHUD");
        Shader pasteShader = (baseMat != null) ? null : Shader.Find("Sticker/UI");

        // 貼り付け SE (シールトランジションと同じ音)
        var assets = Resources.Load<SceneLoaderAssets>("SceneLoaderAssets");

        // --- フェーズ1: 各シールが画面外から画面中央 (定位置) へ飛んできて貼られる ---
        PlayPasteSe(assets);
        Vector2 screenCenter = new Vector2(Screen.width, Screen.height) * 0.5f;
        for (int i = 0; i < groups.Count; i++) {
            int idx = i;
            Vector2 outward = rects[idx].center - screenCenter;
            if (outward.sqrMagnitude < 1f) outward = Vector2.up;
            StartCoroutine(PasteSticker(stickerCanvas.transform, stickers[idx], baseMat, pasteShader,
                                        rects[idx], outward.normalized,
                                        idx * GroupStagger, GroupPasteTime,
                                        () => groupCgs[idx].alpha = 1f));
        }
        float phase1 = (groups.Count - 1) * GroupStagger + GroupPasteTime + 0.06f;
        yield return new WaitForSecondsRealtime(phase1);

        // --- フェーズ2: ハートが画面下の外から左から1枚ずつパラララッ ---
        if (hearts.Count > 0) PlayPasteSe(assets);
        for (int i = 0; i < hearts.Count; i++) {
            int idx = i;
            StartCoroutine(PasteSticker(stickerCanvas.transform, stickers[groups.Count + idx], baseMat, pasteShader,
                                        rects[groups.Count + idx], Vector2.down,
                                        idx * HeartStagger, HeartPasteTime,
                                        () => heartCgs[idx].alpha = 1f));
        }
        float phase2 = (Mathf.Max(1, hearts.Count) - 1) * HeartStagger + HeartPasteTime + 0.06f;
        yield return new WaitForSecondsRealtime(phase2);

        Destroy(stickerCanvasGo);
    }

    // 要素コンテンツのスクリーン座標バウンディングボックス (px)。
    // Overlay Canvas では RectTransform の world 座標 ≒ スクリーン座標として扱える。
    // 札をこの矩形にクロップすることで、折り返し(フラップ)や影がシールの
    // サイズ相応になる (全画面札だと鏡像ゴーストが画面中に出て汚い)。
    Rect ContentScreenRect(Transform target) {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        foreach (var g in target.GetComponentsInChildren<Graphic>(true)) {
            g.rectTransform.GetWorldCorners(corners);
            for (int k = 0; k < 4; k++) {
                min = Vector2.Min(min, corners[k]);
                max = Vector2.Max(max, corners[k]);
            }
        }
        if (min.x > max.x) {   // Graphic 無し: 自身の矩形
            if (target is RectTransform rt) {
                rt.GetWorldCorners(corners);
                for (int k = 0; k < 4; k++) {
                    min = Vector2.Min(min, corners[k]);
                    max = Vector2.Max(max, corners[k]);
                }
            } else return new Rect(0, 0, Screen.width, Screen.height);
        }
        const float pad = 14f;   // カール/影がはみ出さない余白
        min -= Vector2.one * pad;
        max += Vector2.one * pad;
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /**=======================================================================
      1枚のシール貼り (画面外→画面中央):
        角が浮いた状態 (_Peel 保持) のまま画面外から定位置へ飛んできて、
        着地に重なりながら浮いた角が _Peel→0 で押さえられて「ペタッ」。
      飛来と押さえを1本のタイムラインで重ねる (2段に分けると着地で一瞬止まって
      カクついて見える)。札は切り出し済みの小テクスチャ (uvRect は全域)。
      貼り終わったら実UI (onDone) を出して札を破棄する。
    =======================================================================**/
    IEnumerator PasteSticker(Transform parent, Texture2D tex, Material baseMat, Shader fallback,
                             Rect screenRect, Vector2 outwardDir,
                             float delay, float duration, System.Action onDone) {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        var go = new GameObject("Sticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        // シールのバウンディングボックスに一致する小さな札 (アンカー=左下, ピクセル指定)
        r.anchorMin = r.anchorMax = Vector2.zero;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(screenRect.width, screenRect.height);
        r.anchoredPosition = screenRect.center;

        Material mat = (baseMat != null) ? new Material(baseMat)
                     : (fallback != null) ? new Material(fallback) : null;
        var raw = go.GetComponent<RawImage>();
        raw.raycastTarget = false;
        raw.texture = tex;

        const float HoldPeel = 0.42f;    // 飛来中に浮かせておく角の量
        if (mat != null) {
            // 浮く角 = 進行方向の後ろ側 (外側)。先に中央側の角が密着する
            mat.SetFloat("_DirX", outwardDir.x);
            mat.SetFloat("_DirY", outwardDir.y);
            mat.SetFloat(PeelId, HoldPeel);
            raw.material = mat;
        }

        Vector3 homePos = r.localPosition;
        float exitDist = Mathf.Max(Screen.width, Screen.height) * 1.1f;
        Vector3 startPos = homePos + (Vector3)(outwardDir * exitDist);
        float tilt = (outwardDir.x >= 0f ? -1f : 1f) * 9f;   // 進行方向へ傾けて持ってくる

        // 飛来と角の押さえを1本の連続タイムラインに溶け込ませ、途中で動きが止まる「引っ掛かり」を無くす。
        // モーションの心臓部は UIStickerPaste へ抽象化し、生UI貼付(AliceUnlockTutorial 等)と共有する。
        yield return UIStickerPaste.RunPasteMotion(r, mat, startPos, homePos, tilt, duration, HoldPeel, true);

        onDone?.Invoke();
        Destroy(go);
        if (mat != null) Destroy(mat);
        if (tex != null) Destroy(tex);
    }

    static void SetGraphicsAlpha(Graphic[] graphics, float a) {
        foreach (var g in graphics)
            if (g != null) g.canvasRenderer.SetAlpha(a);
    }

    // RT の指定矩形を CPU 側テクスチャへ読み出す (ReadPixels の原点は左下 = rects と同じ)。
    // CopyTexture と違い CPU から GetPixels できるので α 復元の合成に使える。
    static Texture2D ReadRegion(RenderTexture rt, int x, int y, int w, int h) {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(x, y, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        return tex;
    }

    static CanvasGroup EnsureCg(GameObject go, float alpha) {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = alpha;
        return cg;
    }

    void PlayPasteSe(SceneLoaderAssets assets) {
        if (assets == null || assets.transitionPasteSe == null) return;
        var src = GetComponent<AudioSource>();
        if (src == null) {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
        }
        src.PlayOneShot(assets.transitionPasteSe, assets.transitionSeVolume * 0.8f);
    }
}
