/**===========================================================================

 File   : プレイヤー入力ハンドラ [PlayerInputHandler.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 プレイヤー入力ハンドラ。
 ボタン状態 (Press / Hold / Release) は pure getter で Unity Input System を
 その場でクエリしキャッシュしない。stateful なジャンプバッファのみ Update で管理。
 命名: IsAttackPress=押した瞬間 / IsAttackHold=押し続け / IsAttackRelease=離した瞬間。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler {

    private float jumpBuffer;
    private bool isUIMode = false;
    private readonly IPlayerConfig config;
    private readonly IPlayerMover mover;

    public PlayerInputHandler(IPlayerConfig config, IPlayerMover mover) {
        this.config = config;
        this.mover = mover;
    }

    public void SetUIMode(bool uiMode) {
        isUIMode = uiMode;
        if (uiMode) jumpBuffer = 0f;
    }

    // --- 入力状態 (pure getter / キャッシュなし) ---

    public Vector2 Move {
        get {
            if (isUIMode) return Vector2.zero;
            Vector2 v = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null) {
                v = new Vector2(
                    (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                    (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            }
            // キー入力が無ければ左スティック (デッドゾーンは Input System 既定)
            if (v == Vector2.zero && Gamepad.current != null)
                v = Gamepad.current.leftStick.ReadValue();
            return v;
        }
    }

    // 攻撃 = 左クリック / RT(右トリガー)。ダッシュ = 左Shift / B(東)。
    public bool IsAttackPress   => !isUIMode && ((Mouse.current?.leftButton.wasPressedThisFrame ?? false)
                                              || (Gamepad.current?.rightTrigger.wasPressedThisFrame ?? false));
    public bool IsAttackHold    => !isUIMode && ((Mouse.current?.leftButton.isPressed ?? false)
                                              || (Gamepad.current?.rightTrigger.isPressed ?? false));
    public bool IsAttackRelease => !isUIMode && ((Mouse.current?.leftButton.wasReleasedThisFrame ?? false)
                                              || (Gamepad.current?.rightTrigger.wasReleasedThisFrame ?? false));
    public bool IsDodgePress    => !isUIMode && ((Keyboard.current?.leftShiftKey.wasPressedThisFrame ?? false)
                                              || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false));

    // 服のアクティブスキル発動 = Q / Y(北)。メニュー/シール帳表示中(isUIMode)は発動しない。
    public bool IsSkillPress    => !isUIMode && ((Keyboard.current?.qKey.wasPressedThisFrame ?? false)
                                              || (Gamepad.current?.buttonNorth.wasPressedThisFrame ?? false));

    // --- ジャンプバッファ (stateful) ---

    public void Update() {
        bool jumpPressed = (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
                        || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);   // A
        if (!isUIMode && jumpPressed && mover.IsGrounded) {
            jumpBuffer = config.BufferTime;
        }
        jumpBuffer = Mathf.Max(0f, jumpBuffer - Time.deltaTime);
    }

    public bool ConsumeJump() {
        if (jumpBuffer > 0f) {
            jumpBuffer = 0f;
            return true;
        }
        return false;
    }
}
