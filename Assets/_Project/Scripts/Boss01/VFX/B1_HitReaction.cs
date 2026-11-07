using UnityEngine;

/// <summary>
/// 被弾時にボスの肉体（モデルルート）をヒット方向へ少し仰け反らせる反応演出。
/// ボスのルート Transform は Locomotion が向きを制御するため、傾けるのはその子である
/// モデル（Animator を持つ Transform）とし、LateUpdate でアニメーションの上に加算する。
///
/// React(worldDir) を呼ぶと、水平方向のノックバック方向へ上体を maxAngle だけ倒し、
/// recover 秒かけて元へ戻る（当たった瞬間はスナップ、戻りはスプリング状）。
/// Time.timeScale ベースなので、全画面ヒットストップ中は仰け反りポーズがそのまま保持される。
/// </summary>
[DisallowMultipleComponent]
public class B1_HitReaction : MonoBehaviour
{
    [Tooltip("仰け反らせる対象。ボスのモデルルート（Animator を持つ Transform）を割り当てる。\n未設定なら Configure で渡された Animator の Transform を使う。")]
    [SerializeField] private Transform body;

    [Tooltip("被弾時に倒れる最大角度（度）。")]
    [SerializeField, Min(0f)] private float maxAngle = 12f;

    [Tooltip("元の姿勢へ戻りきるまでの時間（秒）。")]
    [SerializeField, Min(0.01f)] private float recover = 0.28f;

    private Vector3    axisLocal;    // 傾き軸（body の親ローカル）
    private float      angle;        // 現在の傾き角（度）
    private bool       resolved;
    private Quaternion appliedTilt = Quaternion.identity; // 前フレームに加算した傾き
    private Quaternion lastWrittenRot;                    // 前フレームに書き込んだ localRotation
    private bool       hasLastWritten;

    private void Awake() => Resolve();

    /// <summary>傾ける対象を外部（B1_Boss）から差し込む。null なら Inspector 割り当てを維持。</summary>
    public void Configure(Transform bodyTransform)
    {
        if (bodyTransform != null) body = bodyTransform;
        Resolve();
    }

    private void Resolve()
    {
        resolved       = body != null;
        appliedTilt    = Quaternion.identity;
        hasLastWritten = false;
    }

    /// <summary>worldDir（水平ノックバック方向）へ上体を仰け反らせる。strength で倒し量を 0〜1 で調整。</summary>
    public void React(Vector3 worldDir, float strength = 1f)
    {
        if (!resolved || body == null) return;

        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 1e-6f) return;

        // body の親ローカルへ変換してから傾き軸（水平・押される向きに直交）を求める。
        Vector3 dirLocal = body.parent != null ? body.parent.InverseTransformDirection(worldDir) : worldDir;
        dirLocal.y = 0f;
        if (dirLocal.sqrMagnitude < 1e-6f) return;
        dirLocal.Normalize();

        axisLocal = Vector3.Cross(Vector3.up, dirLocal).normalized;
        angle     = maxAngle * Mathf.Clamp01(strength);
    }

    private void LateUpdate()
    {
        if (!resolved || body == null) return;

        // 戻りきったら以後は触らない（前フレームまでの傾きも消化済み）
        if (angle <= 0.01f && appliedTilt == Quaternion.identity)
        {
            angle          = 0f;
            hasLastWritten = false;
            return;
        }

        // Awake 時の回転を書き戻すと、Locomotion 等が回した現在の向きが
        // 初期方向へスナップしてしまう。基準は常に「今フレームの素の姿勢」とし、
        // 自分が前フレームに加えた傾きだけを取り除いてから今回の傾きを乗せる。
        // (Animator が localRotation を上書きするボーンなら取り除きは不要なので、
        //  前フレームの書き込み値がそのまま残っている場合のみ巻き戻す)
        Quaternion current = body.localRotation;
        if (hasLastWritten && current == lastWrittenRot)
            current = Quaternion.Inverse(appliedTilt) * current;

        if (angle > 0.01f)
        {
            angle       = Mathf.MoveTowards(angle, 0f, (maxAngle / recover) * Time.deltaTime);
            appliedTilt = Quaternion.AngleAxis(angle, axisLocal);
        }
        else
        {
            angle       = 0f;
            appliedTilt = Quaternion.identity;
        }

        lastWrittenRot     = appliedTilt * current;
        body.localRotation = lastWrittenRot;
        hasLastWritten     = true;
    }
}
