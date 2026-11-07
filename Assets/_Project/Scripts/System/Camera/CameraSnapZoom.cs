/**===========================================================================

 File   : 汎用スナップズーム [CameraSnapZoom.cs]

 Author : Hasegawa Jin
 Date   : 2026/7/17
 -----------------------------------------------------------------------------
 咆哮・命中などの瞬間に FOV を一瞬狭めて戻す「パンチイン」。CameraShake と
 同じ流儀で静的 Play から Camera.main へ自動アタッチして動作する。
 TPSCameraController / CinemachineBrain が毎フレーム FOV を上書きする環境でも
 動くよう、基準値を持たず「今フレームの FOV に差分を足す」加算方式にする。
 ヒットストップと同時に使う想定なので時間は unscaled で進める。
 ===========================================================================**/

using UnityEngine;

[DefaultExecutionOrder(101)] // CameraShake(100) より後 = Brain/TPS の書き込み後に足す
public class CameraSnapZoom : MonoBehaviour {
    public static CameraSnapZoom Instance { get; private set; }

    private Camera cam;
    private float appliedOffset;   // 前フレームに足した差分(次フレーム冒頭で取り除く)
    private float lastWrittenFov;  // 足した直後の FOV(他所が上書きしたかの判定用)

    private bool  playing;
    private float amount;          // パンチインの深さ(度)。正の値で狭める
    private float inDuration;
    private float holdDuration;
    private float outDuration;
    private float elapsed;

    private void Awake() {
        if (Instance == null) Instance = this;
        cam = GetComponent<Camera>();
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    /**=======================================================================
      スナップズーム開始 (静的ヘルパ)
      fovDelta   : 狭める角度(度)。2〜5 あたりが目安
      inDuration : 狭めるまでの時間(秒)。0.03〜0.06 で「スナップ」感が出る
      holdDuration: 狭めたまま保持する時間(秒)
      outDuration: 元へ戻す時間(秒)。in より長めにすると余韻が出る
    =======================================================================**/

    public static void Play(float fovDelta, float inDuration = 0.04f,
                            float holdDuration = 0.06f, float outDuration = 0.28f) {
        if (Instance == null) {
            Camera main = Camera.main;
            if (main != null) main.gameObject.AddComponent<CameraSnapZoom>();
        }
        if (Instance != null) Instance.Snap(fovDelta, inDuration, holdDuration, outDuration);
    }

    public void Snap(float fovDelta, float inDur, float holdDur, float outDur) {
        if (fovDelta <= 0f) return;
        amount       = fovDelta;
        inDuration   = Mathf.Max(0f, inDur);
        holdDuration = Mathf.Max(0f, holdDur);
        outDuration  = Mathf.Max(0.0001f, outDur);
        elapsed      = 0f;
        playing      = true;
    }

    private void LateUpdate() {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;

        // 前フレーム分の差分を取り除く。他所(TPS/Brain)が FOV を書き換えた後なら
        // すでに基準値へ戻っているので触らない。
        if (appliedOffset != 0f && Mathf.Approximately(cam.fieldOfView, lastWrittenFov)) {
            cam.fieldOfView -= appliedOffset;
        }
        appliedOffset = 0f;
        if (!playing) return;

        elapsed += Time.unscaledDeltaTime;

        float k; // 0〜1 のパンチイン量
        if (elapsed < inDuration) {
            float t = elapsed / inDuration;
            k = 1f - (1f - t) * (1f - t);              // easeOut: 一気に寄る
        } else if (elapsed < inDuration + holdDuration) {
            k = 1f;
        } else if (elapsed < inDuration + holdDuration + outDuration) {
            float t = (elapsed - inDuration - holdDuration) / outDuration;
            k = 1f - t * t * (3f - 2f * t);            // smoothstep で滑らかに戻す
        } else {
            playing = false;
            return;
        }

        appliedOffset = -amount * k; // 負 = FOV を狭める(パンチイン)
        cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + appliedOffset, 1f, 179f);
        lastWrittenFov = cam.fieldOfView;
    }
}
