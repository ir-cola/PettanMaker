/*=============================================================================

 File   : [B2_SpringBone.cs]
 Desc   : 髪・スカート用の自作スプリングボーン。1チェーン=1コンポーネントで、
          チェーンのルートボーンに付ける。外部アセット(DynamicBone等)を
          導入せずに済ませるための最小実装。

          ■ なぜ LateUpdate なのか
            Animator は Update と LateUpdate の間でボーンのポーズを書き込む。
            Update で回しても直後にアニメで上書きされて何も起きないため、
            揺れの計算は必ず Animator の後(LateUpdate)に行う必要がある。
            さらに [DefaultExecutionOrder(150)] を付けて、同じ LateUpdate 内
            でも他の演出スクリプトより後になるようにしている。

          ■ なぜ毎フレーム「元の localRotation」に戻してから計算するのか
            このスクリプトは最終的にボーンの rotation を上書きする。戻さずに
            計算すると、前フレームで自分が加えた回転の上にさらに回転が積まれ、
            チェーンがどんどん巻き付いて発散する。毎フレーム基準姿勢へ戻して
            から1フレーム分だけ揺らすことで、アニメの姿勢を土台にできる。
            ※基準姿勢は Awake 時点の localRotation。髪/スカートのボーンに
              アニメのキーが打たれている場合はそちらと競合するので、
              その場合はアニメ側のキーを削るのが前提。

          ■ なぜ ResetToRest() が要るのか
            B2_SmokeBurst の登場演出で女王は21m上空から一瞬でドロップする。
            瞬間移動はスプリングから見ると「1フレームで巨大な速度が乗った」
            のと区別が付かず、髪とスカートが爆発する。ワープを検知したら
            自動でリセットするほか、演出側から明示的にも呼べるようにした。

 ------------------------------------------------------------------------------

 Date   : 2026/07/17
 Author : akatu

===============================================================================*/

using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    /// <summary>
    /// 髪/スカート1房分の揺れ。チェーンのルートボーンに付ける。
    /// </summary>
    [DefaultExecutionOrder(150)]   // Animator がポーズを書いた後に動かす
    public class B2_SpringBone : MonoBehaviour
    {
        // 60fps を基準にパラメータを決めているので、他のフレームレートでも
        // 同じ揺れ方になるよう係数をこの基準へ換算する
        private const float ReferenceFps = 60f;
        // 処理落ちで dt が跳ねると1フレームの移動量が大きくなりすぎて暴れる。
        // 30fps 相当で頭打ちにして、遅くなっても壊れないほうを優先する
        private const float MaxDeltaTime = 1f / 30f;
        // 末端ボーンには子が無く長さを測れない。1つ前のボーン長を借り、
        // それも無い(単独ボーン)ときだけこの値を使う
        private const float FallbackBoneLength = 0.1f;

        [Header("対象")]
        [Tooltip("揺らすボーン列(親→子の順)。空なら Awake で自分から一直線に伸びる子を自動収集する")]
        [SerializeField] private Transform[] bones;

        [Header("揺れの質感")]
        [Tooltip("元のポーズへ戻ろうとする強さ。大きいほど硬い。髪=0.2 / スカート=0.1 あたりが目安")]
        [SerializeField, Range(0f, 1f)] private float stiffness = 0.15f;
        [Tooltip("速度の減衰。大きいほど早く止まる。小さすぎるといつまでも揺れ続ける")]
        [SerializeField, Range(0f, 1f)] private float damping = 0.75f;
        [Tooltip("毎フレーム先端にかかる重力。60fps時の1フレームぶんの移動量なので値は小さくてよい")]
        [SerializeField] private Vector3 gravity = new Vector3(0f, -0.02f, 0f);

        [Header("破綻防止")]
        [Tooltip("各ボーンが元の向きから離れてよい最大角度(度)。スカートが裏返るのを防ぐための簡易コーン制限")]
        [SerializeField] private float maxAngle = 60f;
        [Tooltip("1フレームでルートがこの距離(m)以上動いたらワープとみなして自動リセットする。登場時の落下(21m)対策")]
        [SerializeField] private float teleportThreshold = 2f;

        // Awake 時の基準姿勢。毎フレームここへ戻してから1フレーム分だけ揺らす
        private Quaternion[] restLocalRot;
        // ボーンのローカル空間で見た「頭から先端へのベクトル」。長さも兼ねる。
        // ワールドの先端位置は (bone.position + bone.rotation * tipLocalOffset) で毎回作り直せる
        private Vector3[] tipLocalOffset;
        private float[] boneLength;

        // Verlet 積分の状態(ワールド座標)。速度は currTip - prevTip として暗黙に持つ
        private Vector3[] currTipPos;
        private Vector3[] prevTipPos;

        private Vector3 prevRootPos;
        private bool ready;

        private void Awake()
        {
            if (bones == null || bones.Length == 0)
            {
                bones = CollectStraightChain();
            }

            Initialize();
        }

        private void OnEnable()
        {
            // 非表示→表示のあいだにボスが移動していることがある。
            // 復帰直後の1フレームを前回位置と比較させないようリセットしておく
            ResetToRest();
        }

        /// <summary>
        /// 配列を確保して基準姿勢とボーン長を測る。bones の中身が変わったら呼び直す。
        /// </summary>
        private void Initialize()
        {
            ready = false;
            if (bones == null || bones.Length == 0) return;

            int n = bones.Length;
            restLocalRot = new Quaternion[n];
            tipLocalOffset = new Vector3[n];
            boneLength = new float[n];
            currTipPos = new Vector3[n];
            prevTipPos = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Transform b = bones[i];
                if (b == null)
                {
                    Debug.LogWarning($"[B2_SpringBone] bones[{i}] が未設定です: {name}", this);
                    return;
                }

                restLocalRot[i] = b.localRotation;

                Transform tip = GetTipBone(i);
                if (tip != null)
                {
                    // スケールの影響を受けたくないので rotation だけで逆変換する
                    Vector3 offset = Quaternion.Inverse(b.rotation) * (tip.position - b.position);
                    boneLength[i] = offset.magnitude;
                    tipLocalOffset[i] = offset;
                }

                // 子が無い(=末端)、または子が頭と同じ位置にある場合は長さを測れない
                if (tip == null || boneLength[i] <= Mathf.Epsilon)
                {
                    boneLength[i] = (i > 0 && boneLength[i - 1] > Mathf.Epsilon)
                        ? boneLength[i - 1]
                        : FallbackBoneLength;
                    // 向きの手がかりが無いので自分の forward を仮の骨方向とする
                    tipLocalOffset[i] = Vector3.forward * boneLength[i];
                }

                Vector3 tipWorld = b.position + b.rotation * tipLocalOffset[i];
                currTipPos[i] = tipWorld;
                prevTipPos[i] = tipWorld;
            }

            prevRootPos = transform.position;
            ready = true;
        }

        private void LateUpdate()
        {
            if (!ready) return;

            float dt = Time.deltaTime;
            // ポーズ中(timeScale=0)や初回フレームでは 0 になりうる。
            // 0除算とゼロ距離の Verlet を避けるため何もしない
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, MaxDeltaTime);

            // 落下出現やテレポートは「1フレームで超高速移動した」のと同じに見える。
            // 揺れとして処理すると発散するので、動きを捨てて姿勢を作り直す
            Vector3 rootPos = transform.position;
            if ((rootPos - prevRootPos).sqrMagnitude > teleportThreshold * teleportThreshold)
            {
                ResetToRest();
                return;
            }
            prevRootPos = rootPos;

            // ここが肝。まずチェーン全体をアニメの基準姿勢へ戻す。
            // これをしないと前フレームの自分の回転の上に積み重なって巻き付く
            RestoreRestPose();

            // 60fps基準の係数。fps が変わっても同じ収束の速さになるよう指数で換算する
            float frames = dt * ReferenceFps;
            float dampFactor = Mathf.Pow(Mathf.Clamp01(1f - damping), frames);
            float stiffFactor = 1f - Mathf.Pow(Mathf.Clamp01(1f - stiffness), frames);

            // 親から順に処理する。親を回すと子のワールド位置が動くので、
            // 子は「親が揺れた後の位置」を基準にできる
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                if (b == null) continue;

                Vector3 head = b.position;
                Vector3 restDir = b.rotation * tipLocalOffset[i];
                float len = boneLength[i];
                if (len <= Mathf.Epsilon) continue;

                Vector3 restTip = head + restDir;
                Vector3 curr = currTipPos[i];
                Vector3 prev = prevTipPos[i];

                // Verlet: 前フレームとの差分が速度そのもの。減衰と重力を足す
                Vector3 next = curr + (curr - prev) * dampFactor + gravity * frames;
                // 元のポーズへ引き戻す。これが無いと重力で垂れ下がったまま戻らない
                next += (restTip - next) * stiffFactor;

                // 骨は伸び縮みしないので、頭からの距離を必ず元の長さに直す
                Vector3 dir = next - head;
                if (dir.sqrMagnitude <= Mathf.Epsilon)
                {
                    // 先端が頭に重なると向きが決まらない。基準姿勢に逃がす
                    dir = restDir;
                }
                dir = dir.normalized;

                // 基準の向きから離れすぎないよう円錐内に閉じ込める。
                // スカートが裏返って足が見えるのを防ぐのが主目的
                Vector3 restDirN = restDir.normalized;
                if (maxAngle < 180f && Vector3.Angle(restDirN, dir) > maxAngle)
                {
                    dir = Vector3.RotateTowards(restDirN, dir, maxAngle * Mathf.Deg2Rad, 0f);
                }

                next = head + dir * len;

                prevTipPos[i] = curr;
                currTipPos[i] = next;

                // 「基準の向き」から「今フレームの向き」への回転を、
                // 基準姿勢の上に乗せる。これで子ボーンもまとめて付いてくる
                b.rotation = Quaternion.FromToRotation(restDirN, dir) * b.rotation;
            }
        }

        /// <summary>
        /// 全ボーンを基準姿勢に戻し、速度を消す。
        /// 女王をテレポート/ドロップさせる直前や直後に呼ぶと髪が暴れない。
        /// (B2_SmokeBurst の21m落下のように、こちらが検知できないほど大きい移動をする場合に使う)
        /// </summary>
        public void ResetToRest()
        {
            if (!ready) return;

            RestoreRestPose();

            // 姿勢を戻した後の先端位置を「現在=前回」として入れ直す。
            // 差分が0になるので速度がゼロクリアされる
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                if (b == null) continue;

                Vector3 tipWorld = b.position + b.rotation * tipLocalOffset[i];
                currTipPos[i] = tipWorld;
                prevTipPos[i] = tipWorld;
            }

            prevRootPos = transform.position;
        }

        /// <summary>チェーン全体の localRotation を Awake 時の値へ戻す。</summary>
        private void RestoreRestPose()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null) bones[i].localRotation = restLocalRot[i];
            }
        }

        /// <summary>
        /// i番目のボーンの先端にあたる Transform を返す。無ければ null。
        /// </summary>
        private Transform GetTipBone(int i)
        {
            if (i + 1 < bones.Length) return bones[i + 1];
            // bones 配列がチェーンの途中で終わっていても、実際の子があればそれを先端に使える
            return bones[i].childCount > 0 ? bones[i].GetChild(0) : null;
        }

        /// <summary>
        /// 自分から一直線(子が1つだけ)に伸びるボーンを集める。
        /// 枝分かれしたら止める。房が分かれる場合は房ごとにコンポーネントを付ける想定。
        /// </summary>
        private Transform[] CollectStraightChain()
        {
            List<Transform> list = new List<Transform>();
            Transform t = transform;
            while (t != null)
            {
                list.Add(t);
                t = t.childCount == 1 ? t.GetChild(0) : null;
            }

            // 末端ボーンは対象から外す。末端は「その先」が無いので GetTipBone が null になり、
            // 骨方向を実測できずに Vector3.forward の仮軸で曲がってしまう(下の Initialize 参照)。
            // 外しても末端ボーンは親に付いて回るため、見た目は変わらない。
            if (list.Count > 1) list.RemoveAt(list.Count - 1);
            return list.ToArray();
        }

        /// <summary>ボーンを付け替えた後などに、Inspector から手動で収集し直す。</summary>
        [ContextMenu("ボーン列を再収集")]
        private void RecollectBones()
        {
            bones = CollectStraightChain();
            Debug.Log($"[B2_SpringBone] {bones.Length}本のボーンを収集しました: {name}", this);

            // 再生中に呼ばれたら基準姿勢も測り直す(停止中は Awake で測るので不要)
            if (Application.isPlaying) Initialize();
        }
    }
}
