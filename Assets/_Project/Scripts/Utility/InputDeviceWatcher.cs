/**===========================================================================

 File   : 入力デバイス監視 [InputDeviceWatcher.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 「最後に使われた入力デバイス」がゲームパッドかどうかを判定する静的ヘルパー。
 チュートリアルビルボードのアイコン切替 (キーボード⇔XBOX) などに使う。

 ・パッドのスティック/ボタンが動いたら GamepadActive = true
 ・キーボード/マウスの入力があったら false
 ・判定はフレームごとに1回だけ (何度読んでも軽い)

===========================================================================**/

using UnityEngine;
using UnityEngine.InputSystem;

/**===========================================================================
  入力デバイス監視
===========================================================================**/
public static class InputDeviceWatcher {

    static int lastFrame = -1;
    static bool gamepadActive;

    /** 最後に使われたデバイスがゲームパッドか */
    public static bool GamepadActive {
        get { Refresh(); return gamepadActive; }
    }

    static void Refresh() {
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        var g = Gamepad.current;
        if (g != null) {
            bool used = g.leftStick.ReadValue().sqrMagnitude  > 0.04f
                     || g.rightStick.ReadValue().sqrMagnitude > 0.04f
                     || g.dpad.ReadValue().sqrMagnitude       > 0.1f
                     || g.buttonSouth.isPressed  || g.buttonEast.isPressed
                     || g.buttonWest.isPressed   || g.buttonNorth.isPressed
                     || g.leftTrigger.isPressed  || g.rightTrigger.isPressed
                     || g.leftShoulder.isPressed || g.rightShoulder.isPressed
                     || g.startButton.isPressed  || g.selectButton.isPressed;
            if (used) { gamepadActive = true; return; }
        }

        var k = Keyboard.current;
        if (k != null && k.anyKey.isPressed) { gamepadActive = false; return; }
        var m = Mouse.current;
        if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed
                       || m.delta.ReadValue().sqrMagnitude > 4f)) {
            gamepadActive = false;
        }
    }
}
