/**===========================================================================

 File   : 汎用カメラシェイク [CameraShake.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/28
 -----------------------------------------------------------------------------
 PerlinNoise ベースの汎用カメラシェイク。静的 Play で任意タイミングから
 揺れを重ねがけでき、Camera.main へ自動アタッチして動作する。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class CameraShake : MonoBehaviour {
    public static CameraShake Instance { get; private set; }

    private struct ShakeRequest {
        public float duration;
        public float intensity;
        public float frequency;
        public float elapsed;
        public float seed;
        public bool directional;     // true: worldOrigin から押される指向性インパルス
        public Vector3 worldOrigin;  // インパルスの発生源(ワールド座標)
    }

    private readonly List<ShakeRequest> active = new List<ShakeRequest>();

    private void Awake() {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    /**=======================================================================
      シェイク開始 (静的ヘルパ)
      Instance が無ければ Camera.main へ自動 AddComponent するため、
      手動アタッチ忘れでも動作する
    =======================================================================**/

    public static void Play(float duration, float intensity, float frequency = 30f) {
        if (Instance == null) {
            Camera cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<CameraShake>();
        }
        if (Instance != null) Instance.Shake(duration, intensity, frequency);
    }

    /**=======================================================================
      指向性インパルス開始 (静的ヘルパ)
      worldOrigin(咆哮・着地点など)から押し出される方向へ初撃が入り、
      減衰振動しながら収まる。無指向の Play より「衝撃の出どころ」が伝わる
    =======================================================================**/

    public static void PlayImpulse(Vector3 worldOrigin, float duration, float intensity, float frequency = 22f) {
        if (Instance == null) {
            Camera cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<CameraShake>();
        }
        if (Instance != null) Instance.ShakeImpulse(worldOrigin, duration, intensity, frequency);
    }

    /**=======================================================================
      シェイク開始
      duration : 揺れ時間(秒)
      intensity: 揺れ最大幅(m)。0.05〜0.3 あたりが目安
      frequency: 揺れ速度。20〜40 あたりが目安
    =======================================================================**/

    public void Shake(float duration, float intensity, float frequency = 30f) {
        if (duration <= 0f || intensity <= 0f) return;
        active.Add(new ShakeRequest {
            duration = duration,
            intensity = intensity,
            frequency = frequency,
            elapsed = 0f,
            seed = Random.Range(0f, 1000f)
        });
    }

    /**=======================================================================
      指向性インパルス開始
      worldOrigin: 衝撃の発生源(ワールド座標)。ここからカメラが押し出される
      duration   : 揺れ時間(秒)
      intensity  : 初撃の最大幅(m)。0.05〜0.3 あたりが目安
      frequency  : 揺り戻しの速さ(rad/秒)。15〜30 あたりが目安
    =======================================================================**/

    public void ShakeImpulse(Vector3 worldOrigin, float duration, float intensity, float frequency = 22f) {
        if (duration <= 0f || intensity <= 0f) return;
        active.Add(new ShakeRequest {
            duration = duration,
            intensity = intensity,
            frequency = frequency,
            elapsed = 0f,
            seed = Random.Range(0f, 1000f),
            directional = true,
            worldOrigin = worldOrigin
        });
    }

    /**=======================================================================
      全シェイク即時停止
    =======================================================================**/

    public void Stop() {
        active.Clear();
    }

    // 直前フレームで加えたオフセットの追跡（実行順に依存せず累積を防ぐため）
    private Vector3 _lastAppliedOffset = Vector3.zero;
    private Vector3 _lastResultPos;
    private bool _hasApplied;

    private void LateUpdate() {
        // 前フレームで加えたオフセットがまだ残っている（＝TPS 等が位置を上書きしていない）なら、
        // 二重加算で位置がドリフトしないよう、まず取り除いて素の位置へ戻す。
        // TPS が毎フレーム絶対座標を書き直している通常時は位置が一致しないので何もしない。
        if (_hasApplied && (transform.position - _lastResultPos).sqrMagnitude < 1e-8f)
            transform.position -= _lastAppliedOffset;
        _hasApplied = false;

        if (active.Count == 0) { _lastAppliedOffset = Vector3.zero; return; }

        Vector3 offset = Vector3.zero;
        for (int i = active.Count - 1; i >= 0; i--) {
            ShakeRequest s = active[i];
            s.elapsed += Time.unscaledDeltaTime;
            if (s.elapsed >= s.duration) {
                active.RemoveAt(i);
                continue;
            }

            float t = s.elapsed / s.duration;
            float decay = 1f - t; // 線形に減衰
            float time = s.elapsed * s.frequency + s.seed;

            // PerlinNoise は 0〜1 を返すので -1〜1 に正規化
            float nx = (Mathf.PerlinNoise(time, 0f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0f, time) - 0.5f) * 2f;

            if (s.directional) {
                // 発生源→カメラの押し出し方向を画面平面(right/up)へ投影。
                // cos で t=0 に初撃(押し出し)が入り、減衰しながら揺り戻す。
                Vector3 push = transform.position - s.worldOrigin;
                Vector2 dir = new Vector2(
                    Vector3.Dot(push, transform.right),
                    Vector3.Dot(push, transform.up));
                // 発生源が視線の真正面(投影がほぼゼロ)なら下方向へ落とす(着地の「ズン」)。
                dir = dir.sqrMagnitude < 0.0001f ? Vector2.down : dir.normalized;

                float kick = Mathf.Cos(s.elapsed * s.frequency) * s.intensity * decay * decay;
                // 直線的すぎないよう弱いノイズを重ねて有機的にする
                offset += new Vector3(dir.x * kick + nx * s.intensity * decay * 0.3f,
                                      dir.y * kick + ny * s.intensity * decay * 0.3f, 0f);
            } else {
                offset += new Vector3(nx, ny, 0f) * s.intensity * decay;
            }
            active[i] = s;
        }

        // カメラのローカル空間 (画面横/縦) にオフセット
        Vector3 worldOffset = transform.right * offset.x + transform.up * offset.y;
        transform.position += worldOffset;
        _lastAppliedOffset = worldOffset;        // 次フレーム冒頭で残留判定に使う（ワールド空間で保持）
        _lastResultPos = transform.position;
        _hasApplied = true;
    }
}