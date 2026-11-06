/**===========================================================================

 File   : カスタムカーソル [CustomCursor.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/9
 -----------------------------------------------------------------------------

 起動時に OS カーソルを指定テクスチャ(ピンクの矢印)へ差し替える。
 Cursor.SetCursor はグローバル状態なので一度設定すればシーンをまたいで持続する。
 DontDestroyOnLoad で常駐し、シーン読み込み後にも再適用(念のため)。
 ※ ホットスポットは矢印の尖った先端(左上)を指定。ビルド用に Player 設定の
   Default Cursor / Hotspot も別途設定済み。

 ===========================================================================**/
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class CustomCursor : MonoBehaviour {

    [Tooltip("カーソルにするテクスチャ (Cursor タイプ・64px 程度)")]
    [SerializeField] private Texture2D cursorTexture;

    [Tooltip("クリック位置となる先端。テクスチャ左上からのピクセル")]
    [SerializeField] private Vector2 hotspot = new Vector2(8f, 8f);

    [Tooltip("ハードウェア優先(Auto)。大きく出したい/映らない時は ForceSoftware")]
    [SerializeField] private CursorMode mode = CursorMode.Auto;

    [Tooltip("シーンをまたいで常駐させる")]
    [SerializeField] private bool persistAcrossScenes = true;

    private static CustomCursor _instance;

    private void Awake() {
        if (persistAcrossScenes) {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        Apply();
    }

    private void OnDestroy() {
        if (_instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply();

    public void Apply() {
        if (cursorTexture != null) Cursor.SetCursor(cursorTexture, hotspot, mode);
    }
}
