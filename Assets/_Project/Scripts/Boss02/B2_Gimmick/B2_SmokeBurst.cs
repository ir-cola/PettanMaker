/*=============================================================================

 File   : [B2_SmokeBurst.cs]
 Desc   : 「煙と共にドンと出現/着地」を1呼び出しで行う演出部品。
          Play() を CinemaTrack の Triggers に配線すると、その進捗地点で
            revealObjects を出現(SetActive) + 煙 + カメラシェイク + SE
          が同時に起こる。

          使い回す想定:
            - 土台の出現 : revealObjects = 土台オブジェクト群
            - 女王の着地 : revealObjects = 空(煙とシェイクだけ)

          ■ シェイクについて
            既存の汎用 CameraShake(PerlinNoise/Camera.main へ自動アタッチ)を使う。
            CameraShake は [DefaultExecutionOrder(100)] の LateUpdate で
            オフセットを「加算」する作りなので、CinemaTrack がカメラ位置を
            毎フレーム置き直しても競合せず、累積もしない。
            CameraShake.Shake() は引数3つで UnityEvent から直接配線できないため、
            この Play() が単一の入口になる。

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    /// <summary>煙＋シェイク＋SEと同時にオブジェクトを出現させる演出部品。</summary>
    public class B2_SmokeBurst : MonoBehaviour
    {
        [Header("出現させるもの")]
        [Tooltip("Play() で SetActive(true) にするオブジェクト(土台など)。空でもよい")]
        [SerializeField] private List<GameObject> revealObjects = new();
        [Tooltip("ONなら開始時に revealObjects を隠しておく(演出で初めて出す)")]
        [SerializeField] private bool hideOnAwake = true;
        [Tooltip("設定すると Play() の瞬間に revealObjects をこの位置へ移動させてから出す。\n" +
                 "落下させたい場合はこれではなく下の Drop Height を使うこと")]
        [SerializeField] private Transform teleportTo;

        [Header("落下出現")]
        [Tooltip("0より大きいと、Play() の瞬間に revealObjects を『今の位置からこの高さだけ上』に\n" +
                 "持ち上げて出現させ、元の位置へ落とす。\n" +
                 "土台や女王の『上から降ってきて着地』はこれで行う。\n" +
                 "※ CinemaTrack の Mover は水平移動専用(経路のYを捨てて現在のYを維持する)ため、\n" +
                 "   落下には使えない。この機能はその代替")]
        [SerializeField] private float dropHeight = 0f;
        [Tooltip("落下にかける時間(秒)")]
        [SerializeField] private float dropDuration = 0.5f;
        [Tooltip("落下の緩急。0-1の時間に対する進捗。直線なら等速、後半を立てると加速して落ちる")]
        [SerializeField] private AnimationCurve dropCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("落下させる対象。未設定なら revealObjects を落とす。\n" +
                 "女王のように『表示はメッシュ(Queen/WandModel)を切り替えるが、\n" +
                 "動かすのはボーン(Root)』というズレがある場合に指定する")]
        [SerializeField] private List<GameObject> dropObjects = new();

        [Header("煙")]
        [Tooltip("煙のプレハブ。未設定なら煙は出ない")]
        [SerializeField] private GameObject smokePrefab;
        [Tooltip("煙を出す位置。未設定ならこの Transform")]
        [SerializeField] private Transform smokePoint;
        [Tooltip("煙を消すまでの時間(秒)。0以下なら消さない")]
        [SerializeField] private float smokeLifetime = 4f;

        [Header("カメラシェイク")]
        [Tooltip("揺れ時間(秒)。0以下ならシェイクしない")]
        [SerializeField] private float shakeDuration = 0.4f;
        [Tooltip("揺れ幅(m)。0.05〜0.3 が目安")]
        [SerializeField] private float shakeIntensity = 0.25f;
        [Tooltip("揺れ速度。20〜40 が目安")]
        [SerializeField] private float shakeFrequency = 30f;

        [Header("SE")]
        [SerializeField] private AudioSource seSource;
        [SerializeField] private AudioClip se;

        [Header("イベント")]
        [Tooltip("出現と同時に呼びたいものがあれば配線する")]
        public UnityEvent onPlay;

        private Transform Point => smokePoint != null ? smokePoint : transform;

        private void Awake()
        {
            if (hideOnAwake) SetRevealed(false);
        }

        /// <summary>
        /// 煙・シェイク・SEと同時に revealObjects を出現させる。
        /// CinemaTrack の Triggers(atProgress) から呼ぶ。
        /// </summary>
        [ContextMenu("出現させる")]
        public void Play()
        {
            // 「出現しない」系の不具合調査用。原因が掴めたら消してよい
            Debug.Log($"[B2_SmokeBurst] {name}.Play() 呼び出し。reveal={revealObjects.Count}個 drop高さ={dropHeight}", this);

            // 出す前に移動する(出してから動かすと1フレーム元の場所に見える)。
            // 位置だけ動かす: 回転まで上書きすると、元の向きを持つオブジェクト
            // (女王や土台)の姿勢が壊れるため。
            if (teleportTo != null)
            {
                foreach (GameObject go in revealObjects)
                {
                    if (go != null) go.transform.position = teleportTo.position;
                }
            }

            SetRevealed(true);

            // 落下出現: 今の位置(=着地点)を覚えてから持ち上げ、元の位置へ落とす。
            // 着地点をハードコードしないので、対象を動かしても壊れない。
            if (dropHeight > 0f) StartCoroutine(DropRoutine());

            if (smokePrefab != null)
            {
                GameObject smoke = Instantiate(smokePrefab, Point.position, Point.rotation);
                if (smokeLifetime > 0f) Destroy(smoke, smokeLifetime);
            }

            if (shakeDuration > 0f && shakeIntensity > 0f)
                CameraShake.Play(shakeDuration, shakeIntensity, shakeFrequency);

            if (seSource != null && se != null) seSource.PlayOneShot(se);

            onPlay?.Invoke();
        }

        /// <summary>出現前の状態に戻す(やり直し用)。</summary>
        public void Hide() => SetRevealed(false);

        // revealObjects 全員を dropHeight だけ持ち上げ、元の位置へ同時に落とす。
        // 複数オブジェクト(土台本体+飾り等)がそれぞれの位置関係を保ったまま落ちる。
        private IEnumerator DropRoutine()
        {
            var targets = new List<Transform>();
            var landing = new List<Vector3>();
            List<GameObject> source = dropObjects.Count > 0 ? dropObjects : revealObjects;
            foreach (GameObject go in source)
            {
                if (go == null) continue;
                targets.Add(go.transform);
                landing.Add(go.transform.position);
                go.transform.position += Vector3.up * dropHeight;
            }
            if (targets.Count == 0)
            {
                Debug.LogWarning($"[B2_SmokeBurst] {name}: 落下対象がありません(参照切れ?)", this);
                yield break;
            }
            Debug.Log($"[B2_SmokeBurst] {name}: 落下開始 {targets[0].name} y={targets[0].position.y:F2} -> {landing[0].y:F2}", this);

            float dur = Mathf.Max(0.01f, dropDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = dropCurve != null ? Mathf.Clamp01(dropCurve.Evaluate(Mathf.Clamp01(t / dur)))
                                            : Mathf.Clamp01(t / dur);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] == null) continue;
                    targets[i].position = landing[i] + Vector3.up * (dropHeight * (1f - k));
                }
                yield return null;
            }
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) targets[i].position = landing[i];
            }
        }

        private void SetRevealed(bool on)
        {
            foreach (GameObject go in revealObjects)
            {
                if (go != null) go.SetActive(on);
            }
        }
    }
}
