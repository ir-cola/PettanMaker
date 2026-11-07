/**===========================================================================

 File   : 移動コンポーネント契約 [IPlayerMover.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 移動・接地・ジャンプを担うコンポーネントの公開契約。
 ===========================================================================**/

using UnityEngine;

public interface IPlayerMover {
    bool IsGrounded { get; }
    float PlanarSpeed01 { get; }
    Vector3 MoveWorldDir { get; }
    void CheckGround();
    void Move(Vector2 input, float speedMul = 1.0f, bool rotate = true, bool snap = false,
              bool notifyCameraTrail = true);
    void Jump();
    void ApplyGravity();
    void FaceCameraInstant();
    void FaceCamera();
    void SetFixedCameraMove(bool on);
    void SetUIMode(bool uiMode);
}
