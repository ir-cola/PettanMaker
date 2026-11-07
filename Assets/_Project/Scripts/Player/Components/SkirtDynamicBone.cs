/**===========================================================================

 File   : スカートのダイナミックボーン [SkirtDynamicBone.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/9
 -----------------------------------------------------------------------------

 スカート骨格 (Skirt_Root 配下の各チェーン) にばね物理を与えて揺らす。
 各セグメントを Verlet 粒子として、慣性・減衰・重力・復元力で親に遅れて追従
 させ、最後に親ボーンを子粒子の向きへ回転させて反映する。アニメーションで
 スカート骨は動かないが、これで歩行/回転/ジャンプに合わせて自然に揺れる。
   ・服はプレイヤーの Skirt_* 骨へバインドされる (PlayerModelSwitcher の下線無視
     マッチ) ので、この骨を揺らせば着ている服のスカートも揺れる。
   ・Skirt_Root は多子アンカーなので回さない (胴に剛体追従)。各チェーンのみ揺れる。
 Player_Model (Skirt_Root の祖先) にアタッチすること。

 ===========================================================================**/
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SkirtDynamicBone : MonoBehaviour {

    [Tooltip("スカート骨格の根ボーン名 (配下すべてを揺らす)")]
    [SerializeField] private string rootBoneName = "Skirt_Root";

    [Tooltip("スカート根を追従させる胴ボーン(既定 Hips)。Root直下だと歩行の腰振りに追従しないので付け替える。空で無効")]
    [SerializeField] private string attachBoneName = "Hips";

    [Header("揺れ")]
    [Tooltip("減衰。大きいほど早く止まる")]
    [Range(0f, 1f)] [SerializeField] private float damping = 0.30f;
    [Tooltip("復元力(硬さ)。大きいほど元の形に強く戻る")]
    [Range(0f, 1f)] [SerializeField] private float stiffness = 0.22f;
    [Tooltip("慣性。本体の移動をどれだけ引きずるか")]
    [Range(0f, 1f)] [SerializeField] private float inert = 0.55f;
    [Tooltip("重力(m/s^2 相当)。下向きで裾が垂れる")]
    [SerializeField] private Vector3 gravity = new Vector3(0f, -0.5f, 0f);
    [Tooltip("1ステップの最大秒。カク付き/暴れ防止")]
    [SerializeField] private float maxDelta = 1f / 45f;

    private class Particle {
        public Transform t;
        public int parent;
        public int childCount;
        public Vector3 pos, prev;
        public Vector3 initLocalPos;
        public Quaternion initLocalRot;
        public float restLen;
    }

    private readonly List<Particle> parts = new List<Particle>();
    // 骨ごとの「真の静止ローカル姿勢」を初回だけ控える。以後の Rebuild では毎回これへ
    // 戻してから初期値に使う。揺れ(verlet)で動いた姿勢を初期値に拾うと、着替えの度に
    // 基準がずれてスカートがどんどん崩れる(=着替え連発でぐちゃぐちゃ)ため。
    private readonly Dictionary<Transform, Vector3> _restLocalPos = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> _restLocalRot = new Dictionary<Transform, Quaternion>();
    private Vector3 objectPrevPos;
    private bool ready;

    /// <summary>世界停止(timeScale=0)中でも揺れ／整形を走らせる。チュートリアル等で使う。
    /// これを立てないと dt=0 で LateUpdate が即 return し、毎フレーム冒頭の「静止ローカル
    /// 姿勢へ戻す」処理が行われず、unscaled で回るアニメの生ポーズのままスカートが崩れて
    /// 見える（着替え演出中にアリスの服が崩れる不具合の原因）。</summary>
    public bool ForceUnscaledTime { get; set; }

    // 揺れ(verlet)が走る前・着替え前の「真の静止姿勢」をここで一度だけ控える。
    // 以後の Rebuild はこの値へ戻して初期値に使うので、着替えを繰り返しても基準がずれない。
    private void Awake() { CaptureRest(); }

    // Skirt_Root 配下すべての静止ローカル姿勢をキャッシュする。Awake 時点は揺れ前かつ
    // 着替え前なので、これが崩れていない本来の姿勢。Hips への付け替えも先に済ませる。
    private void CaptureRest() {
        Transform root = FindDeep(transform, rootBoneName);
        if (root == null) return;
        if (!string.IsNullOrEmpty(attachBoneName)) {
            Transform attach = FindDeep(transform, attachBoneName);
            if (attach != null && attach != root && root.parent != attach && !IsDescendantOf(attach, root))
                root.SetParent(attach, worldPositionStays: true);
        }
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0) {
            var b = stack.Pop();
            if (!_restLocalPos.ContainsKey(b)) {
                _restLocalPos[b] = b.localPosition;
                _restLocalRot[b] = b.localRotation;
            }
            for (int i = 0; i < b.childCount; i++) stack.Push(b.GetChild(i));
        }
    }

    private void Start() { Rebuild(); }

    /// <summary>骨格が変わったとき等に呼ぶと再構築する。</summary>
    public void Rebuild() {
        parts.Clear();
        ready = false;
        Transform root = FindDeep(transform, rootBoneName);
        if (root == null) return;

        // スカート根を胴(Hips)へ付け替える＝腰の動き(歩行の腰振り等)に追従させる。
        // 既定リグでは Skirt_Root が Root 直下(=腰と兄弟)で腰の動きに付いてこない。
        // worldPositionStays で見た目位置は保つのでバインドは崩れない。冪等。
        if (!string.IsNullOrEmpty(attachBoneName)) {
            Transform attach = FindDeep(transform, attachBoneName);
            if (attach != null && attach != root && root.parent != attach && !IsDescendantOf(attach, root))
                root.SetParent(attach, worldPositionStays: true);
        }

        Append(root, -1);
        for (int i = 0; i < parts.Count; i++) {
            var p = parts[i];
            p.pos = p.prev = p.t.position;
            if (p.parent >= 0) {
                p.restLen = Vector3.Distance(p.t.position, parts[p.parent].t.position);
                parts[p.parent].childCount++;
            }
        }
        objectPrevPos = transform.position;
        ready = parts.Count > 1;
    }

    private void Append(Transform b, int parentIdx) {
        // 初回に真の静止姿勢をキャッシュ。既知の骨は毎回そこへ戻してから初期値にする
        // (揺れで動いた姿勢を初期値に拾わない = 着替えを繰り返しても崩れない)。
        Vector3 ipos; Quaternion irot;
        if (_restLocalPos.TryGetValue(b, out ipos) && _restLocalRot.TryGetValue(b, out irot)) {
            b.localPosition = ipos;
            b.localRotation = irot;
        } else {
            ipos = b.localPosition; irot = b.localRotation;
            _restLocalPos[b] = ipos; _restLocalRot[b] = irot;
        }
        var p = new Particle {
            t = b, parent = parentIdx,
            initLocalPos = ipos,
            initLocalRot = irot,
        };
        int idx = parts.Count;
        parts.Add(p);
        for (int i = 0; i < b.childCount; i++) Append(b.GetChild(i), idx);
    }

    private void LateUpdate() {
        if (!ready) return;
        // 世界停止中は Time.deltaTime=0 で即 return してしまうため、強制 unscaled を許可する。
        float rawDt = ForceUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float dt = Mathf.Min(rawDt, maxDelta);
        if (dt <= 0f) return;

        // まず全骨を「静止ローカル姿勢」に戻す＝胴(Hips)に剛体追従した基準姿勢。
        // 回転だけでなく位置も戻すことで、Hips へ付け替えた後にアニメが誤って
        // スカート骨を駆動しても打ち消す。その上に下でばねの揺れを乗せる。
        for (int i = 0; i < parts.Count; i++) {
            parts[i].t.localRotation = parts[i].initLocalRot;
            parts[i].t.localPosition = parts[i].initLocalPos;
        }

        Vector3 objectMove = transform.position - objectPrevPos;
        objectPrevPos = transform.position;
        Vector3 force = gravity * (dt * dt);

        // 1) 積分 (root は基準姿勢のワールド位置へピン留め)
        for (int i = 0; i < parts.Count; i++) {
            var p = parts[i];
            if (p.parent < 0) { p.prev = p.pos; p.pos = p.t.position; continue; }
            Vector3 v = p.pos - p.prev;
            Vector3 rmove = objectMove * inert;
            p.prev = p.pos + rmove;
            p.pos += v * (1f - damping) + force + rmove;
        }

        // 2) 拘束: 復元(剛体静止位置へ) + 長さ固定
        for (int i = 1; i < parts.Count; i++) {
            var p = parts[i]; var p0 = parts[p.parent];
            Vector3 restPos = p0.pos + (p0.t.rotation * p.initLocalPos);   // 親の静止回転での剛体位置
            p.pos += (restPos - p.pos) * stiffness;
            Vector3 dir = p.pos - p0.pos;
            float len = dir.magnitude;
            if (len > 1e-6f) p.pos = p0.pos + dir * (p.restLen / len);
        }

        // 3) 反映: 単子の親ボーンを子粒子の向きへ回す (Skirt_Root=多子はアンカーで回さない)
        for (int i = 1; i < parts.Count; i++) {
            var p = parts[i]; var p0 = parts[p.parent];
            if (p0.childCount != 1) continue;
            Vector3 restDir = p.initLocalPos;
            if (restDir.sqrMagnitude < 1e-10f) continue;
            Vector3 worldDir = p.pos - p0.pos;
            if (worldDir.sqrMagnitude < 1e-10f) continue;
            Quaternion rot = Quaternion.FromToRotation(p0.t.rotation * restDir, worldDir);
            p0.t.rotation = rot * p0.t.rotation;
        }
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            var f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    private static bool IsDescendantOf(Transform t, Transform ancestor) {
        for (Transform p = t; p != null; p = p.parent) if (p == ancestor) return true;
        return false;
    }
}
