/**===========================================================================

 File   : 入場確認ダイアログ [EnterConfirmDialog.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30
 -----------------------------------------------------------------------------

   本(ステージ)を選んだ際に「この世界に入りますか？」と確認を出す。
   「はい」で対応シーンへ遷移、「いいえ」で閉じる。

 ===========================================================================**/
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class EnterConfirmDialog : MonoBehaviour {

    [SerializeField] GameObject _window;                  // 表示/非表示する本体
    [SerializeField] TMPro.TextMeshProUGUI _titleText;    // 世界名(任意)
    [SerializeField] Button _yesButton;
    [SerializeField] Button _noButton;
    [SerializeField] bool _useStickerTransition = true;

    string _sceneName;

    void Awake() {
        if (_window != null) _window.SetActive(false);
        if (_yesButton != null) _yesButton.onClick.AddListener(OnClickYes);
        if (_noButton  != null) _noButton.onClick.AddListener(OnClickNo);
    }

    /* 確認を表示 */
    public void Show(string sceneName, string worldTitle) {
        _sceneName = sceneName;
        if (_titleText != null && !string.IsNullOrEmpty(worldTitle)) _titleText.text = worldTitle;
        transform.SetAsLastSibling(); // ホバーで最前面化した本より手前に出す
        if (_window != null) _window.SetActive(true);
    }

    /* はい → 遷移 */
    public void OnClickYes() {
        if (string.IsNullOrEmpty(_sceneName)) return;
        if (_window != null) _window.SetActive(false);
        if (_useStickerTransition) SceneLoader.Load(_sceneName); // ロード画面付き遷移
        else SceneManager.LoadScene(_sceneName);
    }

    /* いいえ → 閉じる */
    public void OnClickNo() {
        if (_window != null) _window.SetActive(false);
    }

    /* 開いているか (ESC ハンドリング用) */
    public bool IsOpen => _window != null && _window.activeSelf;

    /* 外部から閉じる (ESC 用) */
    public void Close() => OnClickNo();
}
