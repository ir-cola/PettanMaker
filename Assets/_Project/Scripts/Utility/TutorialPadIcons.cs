/**===========================================================================

 File   : チュートリアルアイコン切替 [TutorialPadIcons.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 チュートリアルビルボードのアイコンを、最後に使われた入力デバイスに応じて
 キーボード用 (Key_*) ⇔ ゲームパッド用 (Pad_*) で切り替える。

===========================================================================**/

using UnityEngine;

/**===========================================================================
  チュートリアルアイコン切替
===========================================================================**/
public class TutorialPadIcons : MonoBehaviour {

    [Tooltip("キーボード/マウス操作時に表示するアイコン")]
    [SerializeField] private GameObject[] keyboardIcons;
    [Tooltip("ゲームパッド操作時に表示するアイコン")]
    [SerializeField] private GameObject[] gamepadIcons;

    private bool applied;
    private bool current;

    void OnEnable() {
        Apply(InputDeviceWatcher.GamepadActive, force: true);
    }

    void Update() {
        Apply(InputDeviceWatcher.GamepadActive, force: false);
    }

    void Apply(bool gamepad, bool force) {
        if (!force && applied && gamepad == current) return;
        applied = true;
        current = gamepad;
        if (keyboardIcons != null)
            foreach (var go in keyboardIcons) if (go != null) go.SetActive(!gamepad);
        if (gamepadIcons != null)
            foreach (var go in gamepadIcons) if (go != null) go.SetActive(gamepad);
    }
}
