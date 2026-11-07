/**===========================================================================

 File   : ステージ選択管理クラス [StageSelectManager.h]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 2026/7/6 ゲームパッド対応 (十字キー/スティックで本を選択、Aで決定、Bで戻る)

 ===========================================================================**/
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/**===========================================================================
  ステージ選択管理クラス
===========================================================================**/
public class StageSelectManager : MonoBehaviour {

    [Header("Coming Soon ポップアップ")]
    [SerializeField] private GameObject comingSoonPopup;

    [Header("ESC でタイトルへ戻る")]
    [Tooltip("入場確認ダイアログ。開いている間の ESC は閉じるだけにする。未設定なら自動検出")]
    [SerializeField] private EnterConfirmDialog confirmDialog;

    // ===== ゲームパッド選択 =====
    private StageBook[] books;       // 左から順に並べた本 (実行時収集)
    private int bookIndex = -1;      // -1 = パッド未使用 (マウスホバー優先)

    /**=======================================================================
      ESC / B: ポップアップ/ダイアログが開いていれば閉じる。
      何も開いていなければ、タイトル画面を左上→右下へ貼りながらタイトルへ戻る。
      ゲームパッド: 十字キー/左スティックで本を選択、A で決定。
    =======================================================================**/
    private void Update() {
        HandleGamepadBookSelect();

        var kb = Keyboard.current;
        var gp = Gamepad.current;
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame)
                 || (gp != null && gp.buttonEast.wasPressedThisFrame);
        if (!back) return;

        if (comingSoonPopup != null && comingSoonPopup.activeSelf) {
            comingSoonPopup.SetActive(false);
            return;
        }

        if (confirmDialog == null) confirmDialog = FindObjectOfType<EnterConfirmDialog>();
        if (confirmDialog != null && confirmDialog.IsOpen) {
            confirmDialog.Close();
            return;
        }

        StickerTransition.PlayApply("Title");
    }

    /**=======================================================================
      ゲームパッドでの本選択。十字キー/左スティック左右で選択、A で決定。
      ダイアログが開いている間は A=はい / B=Update側で閉じる。
    =======================================================================**/
    private void HandleGamepadBookSelect() {
        var gp = Gamepad.current;
        if (gp == null) return;

        // ダイアログ表示中: A ではい (B は Update の back 処理で閉じる)
        if (confirmDialog == null) confirmDialog = FindObjectOfType<EnterConfirmDialog>();
        if (confirmDialog != null && confirmDialog.IsOpen) {
            if (gp.buttonSouth.wasPressedThisFrame) confirmDialog.OnClickYes();
            return;
        }
        if (comingSoonPopup != null && comingSoonPopup.activeSelf) {
            if (gp.buttonSouth.wasPressedThisFrame) comingSoonPopup.SetActive(false);
            return;
        }

        if (books == null || books.Length == 0) {
            books = FindObjectsOfType<StageBook>();
            System.Array.Sort(books, (a, b) =>
                a.transform.position.x.CompareTo(b.transform.position.x));
            if (books.Length == 0) return;
        }

        int dir = 0;
        if (gp.dpad.left.wasPressedThisFrame) dir = -1;
        else if (gp.dpad.right.wasPressedThisFrame) dir = 1;
        else {
            // スティックは「倒した瞬間」だけ反応 (押しっぱなし連打防止)
            float x = gp.leftStick.ReadValue().x;
            bool held = Mathf.Abs(x) > 0.5f;
            if (held && !stickHeld) dir = x > 0f ? 1 : -1;
            stickHeld = held;
        }

        if (dir != 0) {
            int n = books.Length;
            int next = (bookIndex < 0) ? (dir > 0 ? 0 : n - 1)
                                       : ((bookIndex + dir) % n + n) % n;
            if (bookIndex >= 0 && bookIndex < n && books[bookIndex] != null) books[bookIndex].SetHovered(false);
            bookIndex = next;
            if (books[bookIndex] != null) books[bookIndex].SetHovered(true);   // 破棄済みの本を参照しても落ちないように
        }

        if (gp.buttonSouth.wasPressedThisFrame && bookIndex >= 0 && bookIndex < books.Length && books[bookIndex] != null) {
            books[bookIndex].Confirm();
        }
    }

    private bool stickHeld;

    /* ステージ１ */
    public void OnClickStage01() {
        SceneManager.LoadScene("Stage_1");
    }

    /* ステージ２ */
    public void OnClickStage02() {
        if (comingSoonPopup != null) {
            comingSoonPopup.SetActive(true);
        }
    }

    /* ポップアップを閉じる */
    public void OnClickClosePopup() {
        if (comingSoonPopup != null) {
            comingSoonPopup.SetActive(false);
        }
    }

}