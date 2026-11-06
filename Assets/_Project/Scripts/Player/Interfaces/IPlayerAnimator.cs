/**===========================================================================

 File   : アニメーションコンポーネント契約 [IPlayerAnimator.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 Animator パラメータ操作を担うコンポーネントの公開契約。
 ===========================================================================**/

using UnityEngine;

public interface IPlayerAnimator {
    Animator UnityAnimator { get; }
    void SetFloat(int paramHash, float value);
    void SetFloat(int paramHash, float value, float dampTime, float deltaTime);
    void SetBool(int paramHash, bool value);
    void SetTrigger(int paramHash);
    void ResetTrigger(int paramHash);
    void SetInteger(int paramHash, int value);
    bool FinishedAnimation(int layerIndex = 0);
    bool FinishedAnimationWithTag(string tag, int layerIndex = 0);
}
