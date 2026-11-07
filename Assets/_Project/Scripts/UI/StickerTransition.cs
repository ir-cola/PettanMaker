/**===========================================================================

 File   : シール貼り/剥がしシーン遷移 [StickerTransition.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30 (2026/7/4 貼り遷移 PlayApply + 遷移SE を追加)
 -----------------------------------------------------------------------------

   ・Play(scene)      : 現在の画面をキャプチャして1枚のシールに見立て、左下→右上へ
                        ペリッと剥がしながら次シーンへ遷移する。剥がれた下から
                        次のシーン(ロード済み)が現れる。
                        剥がし開始時に SceneLoaderAssets.transitionPeelSe を再生。
   ・PlayApply(scene) : 次シーンを裏でロードして RenderTexture に1フレーム描き、
                        その絵をシールとして「左上→右下」へペタッと貼りながら
                        遷移する (ステージ選択で ESC → タイトルへ戻る用)。
                        貼り開始時に SceneLoaderAssets.transitionPasteSe を再生。

   使い方: StickerTransition.Play("StageSelect");
           StickerTransition.PlayApply("Title");

 ===========================================================================**/
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StickerTransition : MonoBehaviour {

    const string PeelMaterialResource = "M_StickerPeel"; // Resources 配下
    const string PeelShaderName       = "Sticker/UI";
    static bool s_running;

    /* 既定の所要時間で剥がし遷移を開始 */
    public static void Play(string sceneName) {
        Play(sceneName, 1.2f);
    }

    /* 所要時間を指定して剥がし遷移を開始 */
    public static void Play(string sceneName, float duration) {
        var ctrl = Spawn(sceneName);
        if (ctrl != null) ctrl.StartCoroutine(ctrl.Run(sceneName, Mathf.Max(0.1f, duration)));
    }

    /* 既定の所要時間で貼り遷移を開始 (次シーンの絵を左上→右下へ貼る) */
    public static void PlayApply(string sceneName) {
        PlayApply(sceneName, 0.9f);
    }

    /* 所要時間を指定して貼り遷移を開始 */
    public static void PlayApply(string sceneName, float duration) {
        var ctrl = Spawn(sceneName);
        if (ctrl != null) ctrl.StartCoroutine(ctrl.RunApply(sceneName, Mathf.Max(0.1f, duration)));
    }

    static StickerTransition Spawn(string sceneName) {
        if (s_running || string.IsNullOrEmpty(sceneName)) return null;
        s_running = true;
        var go = new GameObject("StickerTransition");
        DontDestroyOnLoad(go);
        return go.AddComponent<StickerTransition>();
    }

    // ===== 共通部品 =====

    void MakeCanvas() {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
    }

    RawImage MakeFullRaw(string name) {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var raw = go.GetComponent<RawImage>();
        raw.raycastTarget = false;
        raw.enabled = false;
        return raw;
    }

    Material NewPeelMat() {
        var baseMat = Resources.Load<Material>(PeelMaterialResource);
        return (baseMat != null) ? new Material(baseMat) : new Material(Shader.Find(PeelShaderName));
    }

    void PlaySe(AudioClip clip, float volume) {
        if (clip == null) return;
        var src = GetComponent<AudioSource>();
        if (src == null) {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D
        }
        src.PlayOneShot(clip, volume);
    }

    /**=======================================================================
      剥がし遷移: 現画面をキャプチャ → 次シーンをロード → 左下→右上へ剥がす
    =======================================================================**/
    IEnumerator Run(string sceneName, float duration) {
        MakeCanvas();
        var raw = MakeFullRaw("Sticker");

        // フレーム描画後に現在の画面をキャプチャ(URPでも確実な AsTexture を使用。
        // UI オーバーレイ含む最終フレームを Texture2D として取得)
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();

        var mat = NewPeelMat();
        mat.SetFloat("_Peel", 0f);
        raw.texture  = shot;
        raw.material = mat;
        raw.enabled  = true; // 現在の画面と同一のスナップショットを最前面に表示

        // 次シーンを背後にロード(単一)。本オブジェクトは DontDestroyOnLoad で存続
        SceneManager.LoadScene(sceneName);
        yield return null; // 新シーンが描画されるまで待つ(その間スナップショットが全面を覆う)

        // 剥がし SE を鳴らして、左下→右上にぺリッと剥がす
        var assets = Resources.Load<SceneLoaderAssets>("SceneLoaderAssets");
        if (assets != null) PlaySe(assets.transitionPeelSe, assets.transitionSeVolume);

        float t = 0f;
        while (t < duration) {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / duration);
            float p = x * x; // 出だしはゆっくり(貼られた状態から)、最後にペラッと(イーズイン)
            mat.SetFloat("_Peel", p);
            yield return null;
        }

        // 後始末
        raw.enabled = false;
        if (shot != null) Destroy(shot);
        Destroy(mat);
        s_running = false;
        Destroy(gameObject);
    }

    /**=======================================================================
      貼り遷移: 旧画面のスナップショットで覆ったまま次シーンをロードし、
      次シーンをメインカメラ経由で RenderTexture に1フレーム描いてキャプチャ。
      その絵をシールとして左上→右下へ貼り、貼り終えたら生きた次シーンへ切替。
      ※ 次シーンの UI が Screen-Space-Camera ならカメラ経由で絵に含まれる
        (タイトルはこの方式)。Screen-Space-Overlay の UI は写らない点に注意。
    =======================================================================**/
    IEnumerator RunApply(string sceneName, float duration) {
        MakeCanvas();
        var bgRaw      = MakeFullRaw("OldScreen");   // 旧画面 (貼られるまで背景として見せる)
        var stickerRaw = MakeFullRaw("Sticker");     // 新画面のシール (上に貼っていく)

        // 旧画面をキャプチャして固定表示
        yield return new WaitForEndOfFrame();
        var oldShot = ScreenCapture.CaptureScreenshotAsTexture();
        bgRaw.texture = oldShot;
        bgRaw.enabled = true;

        // 次シーンを背後にロード (画面は旧画面スナップショットが覆っている)
        SceneManager.LoadScene(sceneName);
        yield return null;

        // 次シーンのメインカメラを1フレームだけ RenderTexture へ描かせてキャプチャ
        // (この間カメラは画面へ出力しないが、オーバーレイの旧画面が覆っているため見えない)
        var rt = new RenderTexture(Screen.width, Screen.height, 24);
        Camera cam = Camera.main;
        if (cam != null) {
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            yield return new WaitForEndOfFrame();
            cam.targetTexture = prev;
        }

        // シール(新画面の絵) を左上→右下へ貼る (SceneLoader.StickerApply と同じ向き)
        var mat = NewPeelMat();
        mat.SetFloat("_DirX", -1f);   // 左上(0,1)が最初に密着 → 右下(1,0)へ貼り広がる
        mat.SetFloat("_DirY",  1f);
        mat.SetFloat("_Peel",  1f);   // 1=未貼付(浮いている)
        stickerRaw.texture  = rt;
        stickerRaw.material = mat;
        stickerRaw.enabled  = true;

        var assets = Resources.Load<SceneLoaderAssets>("SceneLoaderAssets");
        if (assets != null) PlaySe(assets.transitionPasteSe, assets.transitionSeVolume);

        float t = 0f;
        while (t < duration) {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / duration);
            float e = x * x * (3f - 2f * x);          // なめらか (両端イーズ)
            mat.SetFloat("_Peel", 1f - e);            // 1→0 で貼り付いていく
            yield return null;
        }
        mat.SetFloat("_Peel", 0f);
        yield return null;   // 貼り切った絵を1フレーム見せてから生きたシーンへ

        // 後始末
        bgRaw.enabled = false;
        stickerRaw.enabled = false;
        if (oldShot != null) Destroy(oldShot);
        rt.Release();
        Destroy(rt);
        Destroy(mat);
        s_running = false;
        Destroy(gameObject);
    }
}
