/**===========================================================================

 File   : プレイヤー走行時の土煙 [PlayerRunDust.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 走っている間、足元に土煙 (Resources/VFX/VFXStylizedSmoke_001, VFX Graph) を
 流す。接地かつ水平速度がしきい値以上で Play、離れたら Stop (残煙は自然消滅)。
 開始/停止はヒステリシス付きでチラつきを防ぐ。
 煙は「進行方向の逆」へたなびかせる (エフェクトの噴出が前方=進行方向へ出てしまう
 問題の対策として、土煙ルートを -進行方向へ向ける)。
 ※ VFXStylizedSmoke_001 は Local シミュレーション空間なので、放出器は Player の子にしない。
   方向転換時は古い放出器を停止してその場に残し、新しい向きの放出器へ切り替えることで、
   生成済みの煙が Player の回転に追従しないようにする。
 PlayerAttackVFX.Ensure 経由でランタイム装着される (シーン配置不要)。
 プレハブが無ければ何もしない (警告1回のみ)。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public class PlayerRunDust : MonoBehaviour {

    private const string DustPrefabPath = "VFX/VFXStylizedSmoke_001";
    // 歩き/走りは「スティックの倒し量」だけで決まる (PlayerMover.SpeedMultiplier:
    //   PlanarSpeed01 = walkFloor(0.3) + 0.7 × 入力量 → 歩き=0.30 / 全開の走り=1.0)。
    // 煙は「走りだけ」で出したいので、しきい値は歩きの 0.30 より十分上に置く。
    // 停止側も 0.30 より上にしないと、走り→歩きへ緩めても煙が出続けてしまう。
    private const float StartSpeed01 = 0.75f;   // これ以上 = 走り。煙を開始 (入力量 ~0.64 以上)
    private const float StopSpeed01  = 0.60f;   // これ以下で停止 (ヒステリシス。歩き 0.30 より上)
    private const float FootLift     = 0.05f;   // 足元の床埋まり対策
    private const float DirectionSwitchAngle = 20f; // この角度以上曲がったら新しい放出器へ切り替える
    private const float MinSwitchInterval = 0.1f;   // 急旋回時の放出器生成数を抑える
    private const float MaxLinger = 2.5f;            // 粒子寿命 (最大1.5秒) 後の破棄猶予

    private PlayerMover mover;
    private GameObject dustPrefab;
    private VisualEffect vfx;
    private Transform dustTf;   // 現在放出中の土煙ルート
    private readonly List<GameObject> emitters = new List<GameObject>();
    private bool emitting;
    private bool loadFailed;
    private float nextSwitchTime;

    /// <summary>プレイヤーに装着済みの本コンポーネントを返す。無ければ追加する。</summary>
    public static PlayerRunDust Ensure(GameObject playerGO) {
        var d = playerGO.GetComponent<PlayerRunDust>();
        if (d == null) d = playerGO.AddComponent<PlayerRunDust>();
        return d;
    }

    private void Awake() {
        mover = GetComponent<PlayerMover>();
    }

    private void Update() {
        if (mover == null) return;
        if (!TryLoadPrefab()) return;
        CleanupEmitters();

        float speed = mover.PlanarSpeed01;
        bool onGround = mover.IsGrounded && mover.VerticalVelocity <= 0.01f;
        bool want = onGround && (emitting ? speed > StopSpeed01 : speed >= StartSpeed01);
        if (!want) {
            if (emitting) {
                emitting = false;
                RetireEmitter();
            }
            return;
        }

        Vector3 move = mover.MoveWorldDir;
        move.y = 0f;
        Vector3 backward = move.sqrMagnitude > 0.0001f ? -move.normalized : -transform.forward;

        if (!emitting || vfx == null) {
            emitting = SpawnEmitter(backward);
            return;
        }

        dustTf.position = FootPosition;

        // Local 空間の生存粒子を回さない。曲がった時は古い放出器をその場に残して停止し、
        // 現在位置に新しい向きの放出器を作る。
        if (Time.time >= nextSwitchTime &&
            Vector3.Angle(dustTf.forward, backward) >= DirectionSwitchAngle) {
            RetireEmitter();
            emitting = SpawnEmitter(backward);
        }
    }

    private Vector3 FootPosition => transform.position + Vector3.up * FootLift;

    private bool TryLoadPrefab() {
        if (loadFailed) return false;
        if (dustPrefab != null) return true;
        dustPrefab = Resources.Load<GameObject>(DustPrefabPath);
        if (dustPrefab == null) {
            Debug.LogWarning("[PlayerRunDust] Resources/" + DustPrefabPath + " が見つかりません (土煙なし)");
            loadFailed = true;
            return false;
        }
        return true;
    }

    private bool SpawnEmitter(Vector3 backward) {
        GameObject go = Instantiate(dustPrefab);
        go.name = "RunDust";
        go.transform.SetPositionAndRotation(
            FootPosition,
            Quaternion.LookRotation(backward, Vector3.up));

        VisualEffect nextVfx = go.GetComponentInChildren<VisualEffect>();
        if (nextVfx == null) {
            Debug.LogWarning("[PlayerRunDust] " + DustPrefabPath + " に VisualEffect がありません");
            Destroy(go);
            loadFailed = true;
            return false;
        }

        dustTf = go.transform;
        vfx = nextVfx;
        emitters.Add(go);
        vfx.Stop();
        vfx.Play();
        nextSwitchTime = Time.time + MinSwitchInterval;
        return true;
    }

    private void RetireEmitter() {
        if (vfx != null) vfx.Stop(); // スポーンだけ止め、出ている煙は自然に消える
        if (dustTf != null) Destroy(dustTf.gameObject, MaxLinger);
        vfx = null;
        dustTf = null;
    }

    private void CleanupEmitters() {
        for (int i = emitters.Count - 1; i >= 0; i--) {
            if (emitters[i] == null) emitters.RemoveAt(i);
        }
    }

    private void OnDisable() {
        emitting = false;
        RetireEmitter();
    }

    private void OnDestroy() {
        for (int i = 0; i < emitters.Count; i++) {
            if (emitters[i] != null) Destroy(emitters[i]);
        }
        emitters.Clear();
    }
}
