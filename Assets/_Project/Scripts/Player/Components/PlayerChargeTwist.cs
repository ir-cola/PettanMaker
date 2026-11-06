/**===========================================================================

 File   : チャージ中の腰ひねり [PlayerChargeTwist.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 チャージ中に左右移動したとき、下半身 (腰/脚) を移動方向へひねりつつ、上半身は
 カメラ方向 (アイム) を保つ「トルソツイスト」。

 Animator が姿勢を書いた後 (LateUpdate) に:
   - Hips を移動方向へ +twist 回す          → 脚 (Hips の子) が移動方向を向く
   - 逆回し -twist を Spine 系へ「分散」して当てる → 上半身は徐々にアイムへ戻る (螺旋)
 1 関節に集中させると腰が鋭く折れて不自然になるため、Spine/Spine1/Spine2 へ均等配分する。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;
using VContainer;

public class PlayerChargeTwist : MonoBehaviour {

    [Header("チャージ中の腰ひねり")]
    [Tooltip("左右への最大ひねり角 (度)。大きいほど移動方向へ深く腰をひねる。")]
    [Range(0f, 90f)]
    [SerializeField] private float maxTwist = 40f;

    [Tooltip("腰の正面オフセット (度)。0 = カメラ正面が腰の正面。前進時の腰の向きを微調整。" +
             "+ で右寄り / − で左寄りに正面をずらす。")]
    [Range(-45f, 45f)]
    [SerializeField] private float neutralYaw = 0f;

    [Tooltip("ひねりの追従速度 (大きいほど機敏に追従。小さいほどぬるっと)。")]
    [SerializeField] private float twistSmooth = 12f;

    [Header("ボーン名 (通常は変更不要)")]
    [SerializeField] private string hipsBoneName = "Hips";
    // 逆回しを分散する脊椎ジョイント (親→子の順)。存在するものだけ使う。
    [SerializeField] private string[] spineBoneNames = {
        "Spine1", "Spine2", "Spine3"
    };

    private IPlayerContext ctx;
    private Transform hips;
    private Transform[] spines;
    private float currentTwist;

    [Inject]
    public void Construct(IPlayerContext ctx) {
        this.ctx = ctx;
    }

    private void Awake() {
        hips = FindDeep(transform, hipsBoneName);
        var list = new List<Transform>();
        foreach (string n in spineBoneNames) {
            Transform b = FindDeep(transform, n);
            if (b != null) list.Add(b);
        }
        spines = list.ToArray();
    }

    private void LateUpdate() {
        float target = ComputeTargetTwist();
        currentTwist = Mathf.LerpAngle(currentTwist, target, 1f - Mathf.Exp(-twistSmooth * Time.deltaTime));

        if (hips == null || spines == null || spines.Length == 0 || Mathf.Abs(currentTwist) < 0.1f) return;

        // Hips を移動方向へ回す (脚が移動方向を向く)。
        hips.rotation = Quaternion.AngleAxis(currentTwist, Vector3.up) * hips.rotation;

        // 逆回しを脊椎へ均等配分 (親→子)。合計 -twist で上半身はアイムへ戻り、腰の折れが分散する。
        float per = -currentTwist / spines.Length;
        Quaternion counter = Quaternion.AngleAxis(per, Vector3.up);
        for (int i = 0; i < spines.Length; i++) {
            spines[i].rotation = counter * spines[i].rotation;
        }
    }

    /// <summary>
    /// チャージ中 かつ 移動中なら「カメラの向き(=アイムの0基準)」から移動方向への符号付き角度。
    /// それ以外は 0。基準を体の向き(transform.forward)でなくカメラにすることで、アイムを 0 とした
    /// 純粋な入力方向ぶんだけ腰をひねる (カメラを回しても体の追従遅れに影響されない)。
    /// </summary>
    private float ComputeTargetTwist() {
        if (ctx == null || ctx.StateMachine == null || ctx.Mover == null) return 0f;
        if (!ctx.StateMachine.IsInActionState<StateAttack>()) return 0f;

        Vector3 md = ctx.Mover.MoveWorldDir; md.y = 0f;
        if (md.sqrMagnitude < 0.0001f) return 0f;

        // 基準 = カメラの向き (チャージ中のアイム=0)。カメラ未取得時のみ体の向きでフォールバック。
        Vector3 baseline = transform.forward;
        var cam = TPSCameraController.Instance;
        if (cam != null) {
            Vector3 f = cam.FlatForward;
            if (f.sqrMagnitude > 0.0001f) baseline = f;
        }

        float angle = Vector3.SignedAngle(baseline, md.normalized, Vector3.up);
        // sin で「横成分」に応じたひねりにする: 前(0°)=0 / 右(90°)=+max / 左(-90°)=-max / 後(±180°)=0。
        // ※ Clamp だと真後ろで角度が ±180° に振れて符号が不安定になり腰がガクガク往復していた。
        //   sin(±180°)=0 なので真後ろは自然にひねり0へ収束し、フリップしない。
        return neutralYaw + maxTwist * Mathf.Sin(angle * Mathf.Deg2Rad);
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
