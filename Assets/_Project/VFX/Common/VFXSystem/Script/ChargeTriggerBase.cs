/**===========================================================================

 File   : チャージトリガ基底 [ChargeTriggerBase.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 納品VFXパッケージ (VFX_Charges) の ChargeMagicController が参照するトリガの
 基底クラス。パッケージに同梱されていなかったため、派生クラス
 (KeyboardChargeTrigger_InputSystem) と ChargeMagicController の使用箇所から
 API を復元したもの。ゲーム本編では PlayerAttackVFX が Controller の
 BeginCharge/ReleaseCharge/CancelCharge を直接呼ぶため、トリガは使わない。
 ===========================================================================**/

using UnityEngine;

public abstract class ChargeTriggerBase : MonoBehaviour {

    public event System.Action OnChargeStarted;
    public event System.Action OnChargeReleased;
    public event System.Action OnChargeCanceled;

    protected void RaiseChargeStarted()  => OnChargeStarted?.Invoke();
    protected void RaiseChargeReleased() => OnChargeReleased?.Invoke();
    protected void RaiseChargeCanceled() => OnChargeCanceled?.Invoke();
}
