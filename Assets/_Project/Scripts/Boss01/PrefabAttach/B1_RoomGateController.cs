using System.Collections;
using UnityEngine;

/// <summary>
/// ボス部屋のゲート（Gate_Back / Gate_Front など）の昇降と、壁コリジョンの有効/無効を管理する。
///
/// 使い方:
///   1. 任意の GameObject（例: Boss01_Setup 直下）にアタッチ
///   2. Gates に Gate_Back / Gate_Front の Transform を登録し、Y オフセットを調整
///   3. Wall Collision Objects に壁コリジョンのルートを登録
///   4. B1_BossRoomTrigger の Gate Controller にこのコンポーネントを割り当てる
///
/// オフセットの基準はシーンに配置したままの位置。Offset 0 = シーンと全く同じ見た目。
///   - 起動前   : 配置位置 + DefaultOffsetY（0 なら一切動かさない）、壁コリジョンは無効
///   - トリガー侵入 : Activate() で 配置位置 + ActiveOffsetY へ上昇、壁コリジョンが有効化。
///                EntranceOrbit などの演出中 (timeScale 操作中) でも動いて見えるよう
///                unscaled time で補間する。
///   - ボス撃破 : B1_Health.onDied を購読して自動で Deactivate()（ゲート下降＋コリジョン無効）
/// </summary>
public class B1_RoomGateController : MonoBehaviour
{
    [System.Serializable]
    public class GateEntry
    {
        [Tooltip("昇降させるゲートの Transform（例: Geo/Wall/Gate_Back）")]
        public Transform gate;

        [Tooltip("通常時（戦闘前・撃破後）の Y オフセット。シーン配置位置からの相対値。0 = 配置のまま。")]
        public float defaultOffsetY = 0f;

        [Tooltip("起動時（戦闘中）の Y オフセット。シーン配置位置からの相対値。0 = 配置のまま。")]
        public float activeOffsetY = 3f;

        [System.NonSerialized] public float baseY;      // シーン配置時のローカル Y
        [System.NonSerialized] public bool  captured;
    }

    [Header("ゲート（Y 軸のみ昇降）")]
    [SerializeField] private GateEntry[] gates;

    [Header("壁コリジョン")]
    [Tooltip("戦闘中のみ有効化するコリジョンのルート。子階層の Collider をまとめて切り替える。\n" +
             "非アクティブな GameObject を登録した場合、起動時に SetActive(true) も行う。")]
    [SerializeField] private GameObject[] wallCollisionObjects;

    [Header("昇降アニメーション")]
    [SerializeField, Min(0f)] private float moveDuration = 1.5f;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("演出中は timeScale が操作されるため、EntranceOrbit カメラ中でも昇降が見えるよう " +
             "unscaled time で動かす。通常は ON のままでよい。")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("ボス撃破で自動解除")]
    [Tooltip("未設定ならシーンから B1_Health を自動検索する。撃破時に Deactivate() を呼ぶ。")]
    [SerializeField] private B1_Health bossHealth;

    private bool      active;
    private bool      baseCaptured;
    private bool[]    wallInitialActive;
    private Coroutine moveRoutine;

    /// <summary>戦闘中（ゲート上昇・コリジョン有効）かどうか。</summary>
    public bool IsActive => active;

    private void Awake()
    {
        EnsureCaptured();

        // 起動前の状態にする。DefaultOffsetY が 0 のゲートは一切動かさない（シーンのまま）。
        foreach (var entry in gates)
        {
            if (entry?.gate == null || Mathf.Approximately(entry.defaultOffsetY, 0f)) continue;
            SetGateY(entry, entry.defaultOffsetY);
        }
        SetWallCollision(false);
    }

    private void Start()
    {
        if (bossHealth == null) bossHealth = FindObjectOfType<B1_Health>();
        if (bossHealth != null) bossHealth.onDied.AddListener(OnBossDefeated);
        else Debug.LogWarning("[B1_RoomGateController] B1_Health が見つからないため、撃破時の自動解除が動きません。", this);
    }

    private void OnDestroy()
    {
        if (bossHealth != null) bossHealth.onDied.RemoveListener(OnBossDefeated);
    }

    // 配置位置と壁コリジョンの初期アクティブ状態を一度だけ記憶する。
    // Awake 前に Activate/ContextMenu が呼ばれても正しい基準で動くよう遅延キャプチャにしている。
    private void EnsureCaptured()
    {
        if (baseCaptured) return;
        baseCaptured = true;

        foreach (var entry in gates)
        {
            if (entry?.gate == null) continue;
            entry.baseY    = entry.gate.localPosition.y;
            entry.captured = true;
        }

        wallInitialActive = new bool[wallCollisionObjects.Length];
        for (int i = 0; i < wallCollisionObjects.Length; i++)
            wallInitialActive[i] = wallCollisionObjects[i] != null && wallCollisionObjects[i].activeSelf;
    }

    /// <summary>
    /// 戦闘開始：ゲートを ActiveOffsetY まで上昇させ、壁コリジョンを有効化する。
    /// B1_BossRoomTrigger / UnityEvent から呼ぶ。
    /// </summary>
    [ContextMenu("Activate（ゲート上昇＋コリジョン有効）")]
    public void Activate()
    {
        if (active) return;
        EnsureCaptured();
        active = true;
        SetWallCollision(true);
        StartMove(toActive: true);
    }

    /// <summary>
    /// 戦闘終了：ゲートを DefaultOffsetY まで下降させ、壁コリジョンを無効化する。
    /// ボス撃破時に自動で呼ばれるが、外部からも呼べる。
    /// </summary>
    [ContextMenu("Deactivate（ゲート下降＋コリジョン無効）")]
    public void Deactivate()
    {
        if (!active) return;
        active = false;
        SetWallCollision(false);
        StartMove(toActive: false);
    }

    private void OnBossDefeated() => Deactivate();

    private void StartMove(bool toActive)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveGates(toActive));
    }

    private IEnumerator MoveGates(bool toActive)
    {
        // 現在位置から目標オフセットへ Y のみ補間
        int count = gates.Length;
        var fromY = new float[count];
        var toY   = new float[count];
        for (int i = 0; i < count; i++)
        {
            var entry = gates[i];
            if (entry?.gate == null || !entry.captured) continue;
            fromY[i] = entry.gate.localPosition.y;
            toY[i]   = entry.baseY + (toActive ? entry.activeOffsetY : entry.defaultOffsetY);
        }

        if (moveDuration <= 0f)
        {
            ApplyY(toY);
            moveRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            // EntranceOrbit などの演出は timeScale を操作するため、unscaled で進める
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = moveCurve.Evaluate(Mathf.Clamp01(elapsed / moveDuration));
            for (int i = 0; i < count; i++)
            {
                var entry = gates[i];
                if (entry?.gate == null || !entry.captured) continue;
                Vector3 pos = entry.gate.localPosition;
                pos.y = Mathf.LerpUnclamped(fromY[i], toY[i], t);
                entry.gate.localPosition = pos;
            }
            yield return null;
        }
        ApplyY(toY);
        moveRoutine = null;
    }

    private void ApplyY(float[] targetY)
    {
        for (int i = 0; i < gates.Length; i++)
        {
            var entry = gates[i];
            if (entry?.gate == null || !entry.captured) continue;
            Vector3 pos = entry.gate.localPosition;
            pos.y = targetY[i];
            entry.gate.localPosition = pos;
        }
    }

    private void SetGateY(GateEntry entry, float offsetY)
    {
        Vector3 pos = entry.gate.localPosition;
        pos.y = entry.baseY + offsetY;
        entry.gate.localPosition = pos;
    }

    private void SetWallCollision(bool enabled)
    {
        for (int i = 0; i < wallCollisionObjects.Length; i++)
        {
            var obj = wallCollisionObjects[i];
            if (obj == null) continue;

            // 非アクティブのままだと Collider.enabled を立てても衝突しないため、
            // 起動時は GameObject ごと有効化する。解除時は元のアクティブ状態へ戻す。
            if (enabled) obj.SetActive(true);
            else if (wallInitialActive != null && i < wallInitialActive.Length && !wallInitialActive[i])
                obj.SetActive(false);

            foreach (var col in obj.GetComponentsInChildren<Collider>(true))
                col.enabled = enabled;
        }
    }
}
