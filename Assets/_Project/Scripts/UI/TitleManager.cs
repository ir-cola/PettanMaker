/**===========================================================================

 File   : タイトル管理クラス [TitleManager.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------

 ===========================================================================**/
using UnityEngine;
using UnityEngine.SceneManagement;

/**===========================================================================
  タイトル管理クラス
===========================================================================**/
public class TitleManager : MonoBehaviour {
    /* スタート */
    public void OnClickStart() {
        // シールを剥がす遷移演出で StageSelect へ
        StickerTransition.Play("StageSelect");
    }

    /* オプション */
    public void OnClickOption() {
        Debug.Log("Option clicked (未実装)");
    }

    /* ゲーム終了 */
    public void OnClickEnd() {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

}
