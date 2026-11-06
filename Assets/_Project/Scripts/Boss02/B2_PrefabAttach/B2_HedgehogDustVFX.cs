/*=============================================================================

 File   : [B2_HedgehogDustVFX.cs]
 Desc   : ハリネズミの砂ぼこりVFX。既存の VFX_GroundDust プレハブ(VFX_Dust002.mat 付き)
          を生成し、接地点へレイキャストして Emit することで、
          転がり中のトレイルと着地バーストを再生する。
          RollingDustEmitter の地面projectionの考え方を流用しつつ、ハリネズミは
          Rigidbody を使わない(transform 直接移動)ため、速度は transform 差分で算出する。
          - 発生位置は毎フレーム真下の実際の接地点なので、球の自転に影響されず
            常に地面と接している箇所からのみ出る。
 ------------------------------------------------------------------------------

 Date   : 2026/07/09
 Author : akatu

===============================================================================*/

using UnityEngine;

/// <summary>
/// ハリネズミの砂ぼこりを再生する。
/// SetTrail(true/false) で転がり中のトレイル、PlayImpact(pos) で着地バースト。
/// エフェクトの見た目は VFX_GroundDust プレハブ(要割り当て)に依存する。
/// </summary>
public class B2_HedgehogDustVFX : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("砂煙エフェクト(VFX_GroundDust プレハブ)。未設定なら何も出ない")]
    [SerializeField] private GameObject dustPrefab;

    [Header("地面判定")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("接地判定レイの最大距離")]
    [SerializeField] private float raycastDistance = 5f;
    [Tooltip("レイ発射点を本体中心から上へ持ち上げる量")]
    [SerializeField] private float rayOriginHeight = 0.2f;
    [Tooltip("接地点から法線方向へ持ち上げる量(めり込み防止)")]
    [SerializeField] private float groundOffset = 0.03f;

    [Header("トレイル発生量")]
    [Tooltip("放出間隔(秒)")]
    [SerializeField] private float emitInterval = 0.03f;
    [SerializeField] private int trailMinEmit = 1;
    [SerializeField] private int trailMaxEmit = 4;
    [Tooltip("この速度で最大放出量になる")]
    [SerializeField] private float speedForMaxEmission = 6f;
    [Tooltip("これ未満の速度ではトレイルを出さない")]
    [SerializeField] private float minMoveSpeed = 0.2f;

    [Header("着地バースト")]
    [SerializeField] private int impactEmit = 20;

    private ParticleSystem dustPS;   // 生成した VFX_GroundDust 内の ParticleSystem
    private GameObject dustInstance;  // 生成したエフェクトのルート

    private bool trailActive;
    private float emitTimer;
    private Vector3 prevPos;
    private bool hasPrev;
    private float currentSpeed;
    private bool warned;

    void OnDestroy()
    {
        if (dustInstance != null) Destroy(dustInstance);
    }

    // エフェクトを一度だけ生成して準備する。プレハブ未設定なら false。
    private bool EnsurePS()
    {
        if (dustPS != null) return true;
        if (dustInstance != null) return dustPS != null;

        if (dustPrefab == null)
        {
            if (!warned)
            {
                Debug.LogWarning("[B2_HedgehogDustVFX] dustPrefab(VFX_GroundDust) が未設定です。砂煙は出ません。");
                warned = true;
            }
            return false;
        }

        // 本体の自転・スケールの影響を避けるため非親子で生成
        dustInstance = Instantiate(dustPrefab);
        dustPS = dustInstance.GetComponentInChildren<ParticleSystem>();
        if (dustPS == null)
        {
            Debug.LogWarning("[B2_HedgehogDustVFX] dustPrefab に ParticleSystem が見つかりません。");
            return false;
        }

        // 自然放出を止めて、こちらの Emit() のみで制御する
        var emission = dustPS.emission;
        emission.rateOverTime     = 0f;
        emission.rateOverDistance = 0f;

        var main = dustPS.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // 置き去りにしてトレイルにする
        main.playOnAwake     = false;

        dustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return true;
    }

    /// <summary>転がり中のトレイルを開始/停止する。</summary>
    public void SetTrail(bool on)
    {
        trailActive = on;
        if (on)
        {
            emitTimer = 0f;
            hasPrev = false; // 開始時の位置ワープで速度が跳ねないように
        }
    }

    /// <summary>着地位置で砂ぼこりのバーストを再生する。</summary>
    public void PlayImpact(Vector3 worldPos)
    {
        if (!EnsurePS()) return;

        Vector3 normal = Vector3.up;
        if (Physics.Raycast(worldPos + Vector3.up * rayOriginHeight, Vector3.down,
                out RaycastHit hit, raycastDistance, groundLayer, QueryTriggerInteraction.Ignore))
        {
            worldPos = hit.point;
            normal   = hit.normal;
        }
        MoveParticleSystemTo(worldPos + normal * groundOffset, normal);
        dustPS.Emit(impactEmit);
    }

    void LateUpdate()
    {
        // 速度算出(Rigidbody を使わないため transform 差分で)
        if (hasPrev && Time.deltaTime > 0f)
        {
            currentSpeed = (transform.position - prevPos).magnitude / Time.deltaTime;
        }
        else
        {
            currentSpeed = 0f;
        }
        prevPos = transform.position;
        hasPrev = true;

        if (!trailActive) return;
        if (!EnsurePS()) return;
        if (currentSpeed < minMoveSpeed) return;

        emitTimer -= Time.deltaTime;
        if (emitTimer > 0f) return;
        emitTimer = emitInterval;

        // 真下へレイキャストして実際の接地点を求める
        Vector3 rayOrigin = transform.position + Vector3.up * rayOriginHeight;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                raycastDistance, groundLayer, QueryTriggerInteraction.Ignore))
        {
            return; // 接地していないときは出さない
        }

        MoveParticleSystemTo(hit.point + hit.normal * groundOffset, hit.normal);
        dustPS.Emit(GetEmitCountBySpeed());
    }

    private int GetEmitCountBySpeed()
    {
        float t = Mathf.Clamp01(currentSpeed / speedForMaxEmission);
        return Mathf.RoundToInt(Mathf.Lerp(trailMinEmit, trailMaxEmit, t));
    }

    private void MoveParticleSystemTo(Vector3 position, Vector3 groundNormal)
    {
        dustPS.transform.position = position;
        dustPS.transform.rotation = Quaternion.FromToRotation(Vector3.up, groundNormal);
    }
}
