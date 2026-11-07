/**===========================================================================

 File   : ロード画面付きシーン遷移 [SceneLoader.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/02
 -----------------------------------------------------------------------------

   不思議の国のアリス風ロード画面で次シーンへ遷移する。
   ・入場 : ロード画面(load.png)をシールのように「左上→右下」へ貼り付け、
            装飾(水玉/女王/NOW LOADING)をフェードイン。
   ・待機 : 次シーンを非同期ロード。統合を分散して入場フリーズ(SkinInset)が
            収まるまで覆い続ける。
   ・退場 : 画面をキャプチャし「左下→右上」へ剥がして In-Game を出す。

   使い方: SceneLoader.Load("Stage_1");

 ===========================================================================**/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SceneLoader : MonoBehaviour
{
    static bool s_running;
    static bool s_prewarmed;

    // ドメインリロード無効(高速Play)でも毎Play開始時に確実に初期化する。
    // s_running が前回Playのまま残っていると、ロード画面が出ずプリキャッシュの着替えが露出してしまう。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_running = false; s_prewarmed = false; OnComplete = null; }

    /// <summary>ロード画面が動作中か (遷移 or 直接起動の被せ中)。</summary>
    public static bool IsActive => s_running;

    /// <summary>剥がし完了(In-Game 露出)時に一度だけ呼ばれる。イントロ演出の起点。
    /// 発火時に自動で解除される(一回限り)。</summary>
    public static System.Action OnComplete;

    static void FireComplete()
    {
        var oc = OnComplete;
        OnComplete = null;
        if (oc != null) oc.Invoke();
    }

    public static void Load(string sceneName) { Load(sceneName, 1.6f); }

    public static void Load(string sceneName, float minShowSeconds)
    {
        if (s_running || string.IsNullOrEmpty(sceneName)) return;
        s_running = true;
        var go = new GameObject("SceneLoader");
        DontDestroyOnLoad(go);
        var loader = go.AddComponent<SceneLoader>();
        loader.StartCoroutine(loader.Run(sceneName, Mathf.Max(0.4f, minShowSeconds)));
    }

    // 最初のシーン読込後に一度だけ、ロード画面のシェーダー(StickerPeel/PolkaDotWave)と
    // TMPアトラス・テクスチャを裏で暖機しておく。初回入場時のコンパイル一撃(数フレーム
    // フリーズ)を、目立たない起動直後へ移す。エディタ専用のコンパイル停止対策。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoPrewarm() { Prewarm(); }

    public static void Prewarm()
    {
        if (s_prewarmed || s_running) return;
        s_prewarmed = true;
        var go = new GameObject("SceneLoaderPrewarm");
        DontDestroyOnLoad(go);
        var loader = go.AddComponent<SceneLoader>();
        loader.StartCoroutine(loader.PrewarmRoutine());
    }

    IEnumerator PrewarmRoutine()
    {
        BuildUI();
        // ほぼ透明で隠し描画（描画自体は走るのでシェーダーが確実にコンパイルされる）
        _group.alpha = 0.003f;
        _group.blocksRaycasts = false;
        _content.gameObject.SetActive(true);        // 水玉/TMP を描かせて暖機
        if (_loadingLabel != null) _loadingLabel.ForceMeshUpdate();

        // 退場と同じ StickerPeel マテリアルを一度描いて暖機（これが初回1.2秒の主因）
        var raw = (_applyTex != null) ? MakeStickerRaw("Warm", _applyTex) : null;
        Material mat = null;
        if (raw != null) { mat = NewPeelMat(); if (mat != null) { mat.SetFloat("_Peel", 0.5f); raw.material = mat; } }

        yield return null;   // 描画→コンパイル
        yield return null;
        yield return null;   // 念のため3フレーム

        if (mat != null) Destroy(mat);
        Destroy(gameObject);
    }

    // ---- 定数（1600x900 の参照解像度でのレイアウト） ----
    const float RefW = 1600f, RefH = 900f;
    const string PeelMaterialResource = "M_StickerPeel";   // Resources 配下
    const string PeelShaderName        = "Sticker/UI";
    const float QueenTiltLeft  = 18f;   // 中央キャラ 左傾き（+Z=反時計＝上が左へ。大きめ）
    const float QueenTiltRight = -9f;   // 中央キャラ 右傾き（控えめ）

    CanvasGroup _group;
    RectTransform _canvasRT;
    RectTransform _content;          // 生きたロード画面の中身(BG+装飾)。貼る/剥がす間に表示切替
    CanvasGroup   _decoGroup;        // 装飾(水玉/女王/ラベル)のフェード用
    RectTransform _queen;
    TMPro.TextMeshProUGUI _loadingLabel;
    Material _polkaMat;
    RectTransform _polkaRT;
    Texture _applyTex;               // 入場で貼るシールの絵(=load.png のテクスチャ)

    float _queenTimer;
    bool  _queenRight;
    bool  _exiting;
    Camera _holdCam;                 // カメラ全停止中に Display を埋めるダミー

    IEnumerator Run(string sceneName, float minShow)
    {
        BuildUI();

        // --- 入場: ステージ選択の上にロード画面を「左上→右下」へ貼り付ける ---
        _content.gameObject.SetActive(false);      // 貼り終わるまで生きた中身は隠す
        yield return StickerApply(0.55f);

        // 水玉のアスペクトを実サイズから設定（ドットを丸く保つ）
        if (_polkaMat != null && _polkaRT != null && _polkaRT.rect.height > 1f)
            _polkaMat.SetFloat("_Aspect", _polkaRT.rect.width / _polkaRT.rect.height);

        float t0 = Time.unscaledTime;              // ロード画面を見せ始めた時刻

        // --- 次シーンを非同期ロード。統合を分散してロード画面を滑らかに保つ ---
        var prevPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.Low; // 1フレーム約2msに制限
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op != null) op.allowSceneActivation = false;   // 起動を遅らせる（重要）

        // 読込完了(progress>=0.9)＋最低表示時間まで待つ。この間 Stage_1 はまだ「起動前」で
        // 重い Update/物理/描画が一切走らないので、ロード画面のアニメが滑らかに保たれる。
        // （auto起動だと読込直後に即フル稼働し、表示時間の大半がカクついていた）
        while (op != null && op.progress < 0.9f && Time.unscaledTime - t0 < 8f) yield return null;
        while (Time.unscaledTime - t0 < minShow) yield return null;

        // ここで初めて起動。Awake/Start/EquipCloth/SkinInset が一気に走る（＝入場フリーズ）。
        // 覆われている間は SkinInset を大きな予算で一気に走らせ、カクつく時間を短縮する。
        SkinInset.HoldBudgetMs = 8f;
        if (op != null) op.allowSceneActivation = true;
        while (op != null && !op.isDone) yield return null;

        // 起動直後の収束を覆って待つ。覆っている間はシーンのカメラを止めて描画コストを
        // 消し（Overlayのロード画面はカメラ無しで描画継続）、SkinInset(時間分割)の完了を待つ。
        var pausedCams = PauseSceneRendering();
        int smooth = 0;
        while (true)
        {
            yield return null;
            smooth = (Time.unscaledDeltaTime < 0.05f) ? smooth + 1 : 0;
            // プリキャッシュ保持中(HoldActive)は 8秒タイムアウトで閉じない(30秒は絶対安全上限)
            bool timeout = (Time.unscaledTime - t0 >= 8f && !SkinInset.HoldActive) || (Time.unscaledTime - t0 >= 30f);
            // HoldActive(プリキャッシュ保持)中は IsBusy が服の合間に一瞬 false になっても閉じない
            if ((!SkinInset.IsBusy && !SkinInset.HoldActive && smooth >= 4) || timeout) break;
        }
        SkinInset.HoldBudgetMs = 0f;                           // 通常予算へ戻す
        Application.backgroundLoadingPriority = prevPriority;  // 復元

        // 剥がす前にシーンの描画を戻し、1フレーム描かせてから捲る（下から In-Game が出る）
        ResumeSceneRendering(pausedCams);
        yield return null;

        // --- 退場: ロード画面を「左下→右上」へ剥がして In-Game を出す ---
        yield return StickerPeelExit(0.9f);

        s_running = false;
        FireComplete();          // 剥がし完了 → イントロ演出などへ通知
        Destroy(gameObject);
    }

    /**=======================================================================
      直接起動用: シーン遷移を挟まず、いま居るシーンをそのままロード画面で覆い、
      入場フリーズ(SkinInset 等)が収まってから剥がして In-Game を出す。
      ステージシーンを直接 Play したときでもロード画面を出すために使う。
      onRevealed は剥がし完了時に呼ばれる (OnComplete も同時に発火)。
    =======================================================================**/
    public static void CoverAndReveal(float minShowSeconds, System.Action onRevealed = null)
    {
        if (s_running) { onRevealed?.Invoke(); return; }   // 既に遷移側が覆っている
        s_running = true;
        var go = new GameObject("SceneLoader");
        DontDestroyOnLoad(go);
        var loader = go.AddComponent<SceneLoader>();
        loader.StartCoroutine(loader.RunCover(Mathf.Max(0.4f, minShowSeconds), onRevealed));
    }

    IEnumerator RunCover(float minShow, System.Action onRevealed)
    {
        // 先にシーンのカメラを止めて「素のシーンの一瞬のチラ見え」を防いでから被せる。
        var pausedCams = PauseSceneRendering();
        BuildUI();
        _content.gameObject.SetActive(true);       // 前シーンが無いので即座に全面を覆う
        if (_decoGroup != null) _decoGroup.alpha = 1f;
        if (_polkaMat != null && _polkaRT != null && _polkaRT.rect.height > 1f)
            _polkaMat.SetFloat("_Aspect", _polkaRT.rect.width / _polkaRT.rect.height);

        float t0 = Time.unscaledTime;

        // 入場フリーズ(SkinInset)が収まる(IsBusy=false)＋最低表示時間まで覆い続ける。
        SkinInset.HoldBudgetMs = 8f;
        int smooth = 0;
        while (true)
        {
            yield return null;
            smooth = (Time.unscaledDeltaTime < 0.05f) ? smooth + 1 : 0;
            // HoldActive(プリキャッシュ保持)中は IsBusy が服の合間に一瞬 false になっても settled にしない
            bool settled = (!SkinInset.IsBusy && !SkinInset.HoldActive && smooth >= 4) && (Time.unscaledTime - t0 >= minShow);
            // プリキャッシュ保持中(HoldActive)は 8秒タイムアウトで閉じない(30秒は絶対安全上限)
            bool timeout = (Time.unscaledTime - t0 >= 8f && !SkinInset.HoldActive) || (Time.unscaledTime - t0 >= 30f);
            if (settled || timeout) break;
        }
        SkinInset.HoldBudgetMs = 0f;

        // 描画を戻し、1フレーム描かせてから剥がす(下から In-Game が出る)
        ResumeSceneRendering(pausedCams);
        yield return null;

        yield return StickerPeelExit(0.9f);

        s_running = false;
        if (onRevealed != null) onRevealed.Invoke();
        FireComplete();
        Destroy(gameObject);
    }

    // ホールド中、シーンのカメラを止めて描画コストを消す（オーバーレイのロード画面は
    // カメラ無しで描画され続ける）。止めたカメラを返し、後で戻す。
    // ※ カメラが1つも無いと Game ビューに "Display 1 No cameras rendering" の警告が
    //   表示されるため、何も描かないダミーカメラ (cullingMask=0) で Display を埋める。
    List<Camera> PauseSceneRendering()
    {
        var paused = new List<Camera>();
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c != null && c.enabled && c != _holdCam) { c.enabled = false; paused.Add(c); }

        if (_holdCam == null)
        {
            var go = new GameObject("HoldCam");
            go.transform.SetParent(transform, false);
            _holdCam = go.AddComponent<Camera>();
            _holdCam.cullingMask = 0;                          // 何も描かない (コストほぼゼロ)
            _holdCam.clearFlags = CameraClearFlags.SolidColor;
            _holdCam.backgroundColor = Color.black;
            _holdCam.depth = -100f;
        }
        _holdCam.enabled = true;
        return paused;
    }

    void ResumeSceneRendering(List<Camera> paused)
    {
        if (_holdCam != null) _holdCam.enabled = false;
        if (paused == null) return;
        foreach (var c in paused) if (c != null) c.enabled = true;
    }

    void Update()
    {
        if (_exiting) return;
        if (_content == null || !_content.gameObject.activeSelf) return;

        // 水玉のアスペクトを常に実サイズに合わせてドットを真円に保つ
        if (_polkaMat != null && _polkaRT != null && _polkaRT.rect.height > 1f)
            _polkaMat.SetFloat("_Aspect", _polkaRT.rect.width / _polkaRT.rect.height);

        // 水玉の時間を非スケール時間で駆動する。シェーダー側の _Time.y はロード中の
        // フレーム落ちで maximumDeltaTime にクランプされ、波がカクつく/止まって見えるため。
        if (_polkaMat != null)
            _polkaMat.SetFloat("_ManualTime", Time.unscaledTime);

        // 中央キャラ：0.8 秒ごとに左右へパッと傾き切替
        if (_queen != null)
        {
            _queenTimer += Time.unscaledDeltaTime;
            if (_queenTimer >= 0.8f)
            {
                _queenTimer -= 0.8f;
                _queenRight = !_queenRight;
                _queen.localRotation = Quaternion.Euler(0f, 0f, _queenRight ? QueenTiltRight : QueenTiltLeft);
            }
        }

        // NOW LOADING... を1文字ずつバウンド
        BounceLabel();
    }

    void BounceLabel()
    {
        if (_loadingLabel == null) return;
        _loadingLabel.ForceMeshUpdate();
        var ti = _loadingLabel.textInfo;
        if (ti == null || ti.characterCount == 0) return;

        float t = Time.unscaledTime;
        for (int c = 0; c < ti.characterCount; c++)
        {
            var ci = ti.characterInfo[c];
            if (!ci.isVisible) continue;
            int vi = ci.vertexIndex;
            int mi = ci.materialReferenceIndex;
            var verts = ti.meshInfo[mi].vertices;
            float phase = c * 0.45f;
            float y = Mathf.Max(0f, Mathf.Sin(t * 6f - phase)) * 16f; // 上向きバウンド
            Vector3 off = new Vector3(0f, y, 0f);
            verts[vi + 0] += off; verts[vi + 1] += off; verts[vi + 2] += off; verts[vi + 3] += off;
        }
        for (int m = 0; m < ti.meshInfo.Length; m++)
        {
            ti.meshInfo[m].mesh.vertices = ti.meshInfo[m].vertices;
            _loadingLabel.UpdateGeometry(ti.meshInfo[m].mesh, m);
        }
    }

    // ---- 入場: load.png を1枚のシールに見立て「左上→右下」へ貼り付ける（StickerPeel の逆再生） ----
    IEnumerator StickerApply(float dur)
    {
        // 貼る絵が無ければ演出を省略して中身をそのまま表示
        if (_applyTex == null)
        {
            _content.gameObject.SetActive(true);
            if (_decoGroup != null) _decoGroup.alpha = 1f;
            yield break;
        }

        var raw = MakeStickerRaw("ApplySticker", _applyTex);
        var mat = NewPeelMat();
        if (mat != null)
        {
            mat.SetFloat("_DirX", -1f);   // 左上(0,1)が最初に密着 → 右下(1,0)へ貼り広がる
            mat.SetFloat("_DirY",  1f);
            mat.SetFloat("_Peel",  1f);   // 1=未貼付(浮いている)
            raw.material = mat;
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / dur);
            float e = x * x * (3f - 2f * x);       // なめらか（両端イーズ）
            if (mat != null) mat.SetFloat("_Peel", 1f - e); // 1→0 で貼り付いていく
            yield return null;
        }
        if (mat != null) mat.SetFloat("_Peel", 0f);

        // 生きた中身へ引き継ぐ（BG は load.png で同一なので継ぎ目なし。装飾のみフェードイン）
        if (_decoGroup != null) _decoGroup.alpha = 0f;
        _content.gameObject.SetActive(true);
        yield return null;                          // 中身を1枚描かせてからシールを外す
        Destroy(raw.gameObject);
        if (mat != null) Destroy(mat);

        float f = 0f;
        while (f < 0.25f)
        {
            f += Time.unscaledDeltaTime;
            if (_decoGroup != null) _decoGroup.alpha = Mathf.Clamp01(f / 0.25f);
            yield return null;
        }
        if (_decoGroup != null) _decoGroup.alpha = 1f;
    }

    // ---- 退場: 現在の画面(ロード画面)をキャプチャし「左下→右上」へ剥がして下のシーンを出す ----
    IEnumerator StickerPeelExit(float dur)
    {
        _exiting = true;
        yield return new WaitForEndOfFrame();
        Texture2D shot = CaptureColorAccurate();

        // 生きた中身を隠し、同じ絵のスナップショットを最前面へ（剥がすと背後の In-Game が出る）
        _content.gameObject.SetActive(false);
        var raw = MakeStickerRaw("PeelSticker", shot);
        var mat = NewPeelMat();
        if (mat != null)
        {
            mat.SetFloat("_DirX", 1f);   // 左下(0,0)が最初に剥がれる → 右上(1,1)へ（既定と同じ）
            mat.SetFloat("_DirY", 1f);
            mat.SetFloat("_Peel", 0f);
            raw.material = mat;
        }
        yield return null;

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / dur);
            float p = x * x;                        // 出だしゆっくり→最後にペラッ（イーズイン）
            if (mat != null) mat.SetFloat("_Peel", p);
            yield return null;
        }

        if (shot != null) Destroy(shot);
        if (mat != null) Destroy(mat);
    }

    // 全画面 RawImage を最前面に生成（貼る/剥がすシール本体）
    RawImage MakeStickerRaw(string name, Texture tex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();                     // 中身より手前へ
        var raw = go.GetComponent<RawImage>();
        raw.texture = tex; raw.raycastTarget = false;
        return raw;
    }

    // ピール用マテリアル（Resources 優先、無ければ Shader.Find）
    Material NewPeelMat()
    {
        var baseMat = Resources.Load<Material>(PeelMaterialResource);
        if (baseMat != null) return new Material(baseMat);
        var sh = Shader.Find(PeelShaderName);
        return (sh != null) ? new Material(sh) : null;
    }

    // 画面キャプチャ。Linear カラースペースだと ScreenCapture は sRGB データを
    // 「linear 扱い」のテクスチャで返し、UI で描くと二重ガンマで白っぽく浮く。
    // 生バイトを sRGB フラグのテクスチャへ移し替えて、ライブ画面と同じ色に揃える。
    Texture2D CaptureColorAccurate()
    {
        Texture2D raw = ScreenCapture.CaptureScreenshotAsTexture();
        if (raw == null || QualitySettings.activeColorSpace != ColorSpace.Linear) return raw;

        var fixedTex = new Texture2D(raw.width, raw.height, raw.format, false, false); // linear:false = sRGB
        fixedTex.LoadRawTextureData(raw.GetRawTextureData());
        fixedTex.Apply(false, false);
        Destroy(raw);
        return fixedTex;
    }

    void BuildUI()
    {
        var A = Resources.Load<SceneLoaderAssets>("SceneLoaderAssets");
        _applyTex = (A != null && A.background != null) ? A.background.texture : null;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // short 上限（32770 は overflow して背面化するので不可）
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = true;
        _group.interactable = false;
        _canvasRT = (RectTransform)transform;

        // 生きたロード画面の中身をまとめる容器（貼る/剥がす間に表示を切り替える）
        _content = NewStretchRT("Content", transform);

        // 背景（load.png 全面。不透明なので下のシーンを完全に覆う）。素材が無ければ黄色ベタ
        var bg = NewImage("BG", _content);
        if (A != null && A.background != null) { bg.sprite = A.background; bg.type = Image.Type.Simple; bg.preserveAspect = false; }
        else bg.color = new Color(0.96f, 0.95f, 0.70f, 1f);
        Stretch(bg.rectTransform);

        // 装飾（水玉/女王/ラベル）は入場時にフェードインさせるため CanvasGroup で束ねる
        var deco = NewStretchRT("Deco", _content);
        _decoGroup = deco.gameObject.AddComponent<CanvasGroup>();
        _decoGroup.blocksRaycasts = false;
        _decoGroup.alpha = 1f;

        // 波打つ水玉（黄色部分にだけ重ねる）
        var polka = NewImage("Polka", deco);
        polka.color = Color.white;
        var prt = polka.rectTransform;
        prt.anchorMin = new Vector2(0f, 0.11f); prt.anchorMax = new Vector2(1f, 0.90f);
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        _polkaRT = prt;
        if (A != null && A.polkaShader != null)
        {
            _polkaMat = new Material(A.polkaShader);
            // 位置は固定、色・サイズ・透明度を波打たせる（PolkaDotWave 参照）
            _polkaMat.SetColor("_DotColor",  new Color(0.49f, 0.64f, 0.83f, 0.42f)); // 青
            _polkaMat.SetColor("_DotColorB", new Color(0.90f, 0.62f, 0.74f, 0.42f)); // 桃
            _polkaMat.SetFloat("_Density", 9f);
            _polkaMat.SetFloat("_DotRadius", 0.30f); // 基準サイズ
            _polkaMat.SetFloat("_SizeAmp", 0.10f);   // サイズの波打ち量
            _polkaMat.SetFloat("_AlphaMin", 0.35f);  // 透明度の波打ち下限
            _polkaMat.SetFloat("_WaveFreq", 4.0f);
            _polkaMat.SetFloat("_WaveSpeed", 1.6f);
            _polkaMat.SetFloat("_ManualTime", Time.unscaledTime); // 以降 Update が毎フレーム更新
            polka.material = _polkaMat;
        }
        else polka.enabled = false;

        // 中央キャラ（女王スティッカー）
        if (A != null && A.character != null)
        {
            var q = NewImage("Queen", deco);
            q.sprite = A.character; q.preserveAspect = true;
            var qrt = q.rectTransform;
            qrt.anchorMin = qrt.anchorMax = qrt.pivot = new Vector2(0.5f, 0.5f);
            qrt.sizeDelta = new Vector2(340f, 340f);
            qrt.anchoredPosition = new Vector2(0f, 10f);
            q.raycastTarget = false;
            _queen = qrt;
            _queen.localRotation = Quaternion.Euler(0f, 0f, QueenTiltLeft);
        }

        // 左上「不思議の国のアリス」
        var alice = NewLabel("Alice", deco, A, "不思議の国のアリス", 46f, TMPro.TextAlignmentOptions.TopLeft);
        var art = alice.rectTransform;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0f, 1f);
        art.sizeDelta = new Vector2(760f, 90f);
        art.anchoredPosition = new Vector2(56f, -104f);

        // 右下「NOW LOADING...」
        _loadingLabel = NewLabel("Loading", deco, A, "NOW LOADING...", 54f, TMPro.TextAlignmentOptions.BottomRight);
        var lrt = _loadingLabel.rectTransform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        lrt.sizeDelta = new Vector2(760f, 90f);
        lrt.anchoredPosition = new Vector2(-56f, 118f);
    }

    RectTransform NewStretchRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        Stretch(rt);
        return rt;
    }

    TMPro.TextMeshProUGUI NewLabel(string name, Transform parent, SceneLoaderAssets A, string text, float size, TMPro.TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (A != null && A.font != null) tmp.font = A.font;
        else { var f = TMPro.TMP_Settings.defaultFontAsset; if (f != null) tmp.font = f; }
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = new Color(0.29f, 0.40f, 0.58f, 1f); // 青枠に馴染む濃い青
        tmp.raycastTarget = false;
        return tmp;
    }

    Image NewImage(string n, Transform parent)
    {
        var g = new GameObject(n, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        g.transform.SetParent(parent, false);
        var img = g.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
