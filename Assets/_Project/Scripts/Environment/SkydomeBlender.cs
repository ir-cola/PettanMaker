using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2つのスカイドームをディザ（ディゾルブ）クロスフェードで切り替えるコンポーネント。
///
/// 各 Skydome シェーダーに仕込んだ <c>_Dissolve</c>（スクリーンスペース Bayer ディザ）を
/// 使い、退出側ドームを手前でディゾルブ消去しながら進入側ドームを背面から透けさせる。
/// 不透明シェーダーのまま、しかも <b>異なるシェーダー同士</b>でもクロスフェードできる。
///
/// 使い方:
///   - シーンに現行ドームと切り替え先ドームの GameObject を両方置く（先は非アクティブでOK）。
///   - このコンポーネントを任意の GameObject（マネージャや現行ドーム）にアタッチし、
///     currentSkydome / targetSkydome を割り当てる。
///   - B1_BossRoomTrigger などから <see cref="Blend()"/> を呼ぶ。
/// </summary>
public class SkydomeBlender : MonoBehaviour
{
    [Header("Skydomes")]
    [Tooltip("開始時に表示されているスカイドーム。")]
    [SerializeField] private GameObject currentSkydome;

    [Tooltip("切り替え先のスカイドーム。開始時は非アクティブでも可（自動でアクティブ化）。")]
    [SerializeField] private GameObject targetSkydome;

    [Header("Blend")]
    [Tooltip("クロスフェードにかける秒数。")]
    [SerializeField] private float duration = 2.0f;

    [Tooltip("ディゾルブ進行カーブ（0→1）。")]
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private static readonly int DissolveID = Shader.PropertyToID("_Dissolve");

    // スカイドームの描画順（Background 帯: 1000〜1999 の範囲に収める）
    private const int QueueBehind = 1000; // 進入側（背面）
    private const int QueueFront  = 1100; // 退出側（手前）＝ディゾルブする方

    private Coroutine blendRoutine;

    /// <summary>
    /// Inspector で設定した <see cref="targetSkydome"/> へクロスフェードする。
    /// UnityEvent や他コンポーネントから引数なしで呼べる。
    /// </summary>
    public void Blend()
    {
        BlendTo(targetSkydome, duration);
    }

    /// <summary>
    /// 指定ドームへ指定秒数でクロスフェードする。
    /// </summary>
    public void BlendTo(GameObject next, float blendDuration)
    {
        if (next == null)
        {
            Debug.LogWarning("[SkydomeBlender] 切り替え先ドームが未設定です。", this);
            return;
        }
        if (next == currentSkydome)
        {
            return; // すでに表示中
        }

        if (blendRoutine != null)
        {
            StopCoroutine(blendRoutine);
        }
        blendRoutine = StartCoroutine(CrossfadeRoutine(currentSkydome, next, Mathf.Max(0.0001f, blendDuration)));
    }

    private IEnumerator CrossfadeRoutine(GameObject outgoing, GameObject incoming, float blendDuration)
    {
        // 進入側：背面・完全表示で有効化
        var incomingMats = GetMaterials(incoming);
        incoming.SetActive(true);
        SetDissolve(incomingMats, 0f);
        SetRenderQueue(incomingMats, QueueBehind);

        // 退出側：手前でディゾルブ（outgoing が無い初回などは進入側のフェードインで対応）
        List<Material> outgoingMats = null;
        if (outgoing != null && outgoing != incoming)
        {
            outgoingMats = GetMaterials(outgoing);
            SetRenderQueue(outgoingMats, QueueFront);
            SetDissolve(outgoingMats, 0f);
        }
        else
        {
            // 退出側が無い場合は、進入側自体をディゾルブ・インさせる
            SetRenderQueue(incomingMats, QueueFront);
        }

        float elapsed = 0f;
        while (elapsed < blendDuration)
        {
            elapsed += Time.deltaTime;
            float k = curve != null
                ? curve.Evaluate(Mathf.Clamp01(elapsed / blendDuration))
                : Mathf.Clamp01(elapsed / blendDuration);

            if (outgoingMats != null)
            {
                SetDissolve(outgoingMats, k);          // 退出側を消していく
            }
            else
            {
                SetDissolve(incomingMats, 1f - k);     // 進入側を出していく
            }
            yield return null;
        }

        // 退出側を片付けて状態をリセット
        if (outgoingMats != null)
        {
            SetDissolve(outgoingMats, 1f);
            outgoing.SetActive(false);
            SetDissolve(outgoingMats, 0f);
            SetRenderQueue(outgoingMats, QueueBehind);
        }

        SetDissolve(incomingMats, 0f);
        SetRenderQueue(incomingMats, QueueBehind);

        currentSkydome = incoming;
        blendRoutine = null;
    }

    /// <summary>
    /// GameObject 配下の Renderer のマテリアルインスタンスを収集する。
    /// （sharedMaterial ではなく material を使い、アセットを破壊しないようインスタンス化）
    /// </summary>
    private static List<Material> GetMaterials(GameObject go)
    {
        var result = new List<Material>();
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            result.AddRange(r.materials); // インスタンス化された配列
        }
        return result;
    }

    private static void SetDissolve(List<Material> mats, float value)
    {
        foreach (var m in mats)
        {
            if (m.HasProperty(DissolveID))
            {
                m.SetFloat(DissolveID, value);
            }
        }
    }

    private static void SetRenderQueue(List<Material> mats, int queue)
    {
        foreach (var m in mats)
        {
            m.renderQueue = queue;
        }
    }

    private void Reset()
    {
        // 自身がスカイドームなら現行として自動設定
        if (currentSkydome == null && GetComponent<Renderer>() != null)
        {
            currentSkydome = gameObject;
        }
    }
}
