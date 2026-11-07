using UnityEngine;

/// <summary>
/// Boss01 が指定フェーズへ突入したとき HealSticker(回復シール) を生成する。
/// B1_Boss と同じ GameObject もしくはその子・親に置き、Inspector で回復シールの
/// プレハブと生成位置を割り当てるだけで機能するアタッチ型コンポーネント。
/// フェーズ切り替えは B1_StateMachine.OnStateChanged を購読して検知する。
/// </summary>
public class B1_PhaseHealStickerSpawner : MonoBehaviour
{
    [SerializeField] private B1_Boss boss;

    [Header("生成するシール")]
    [Tooltip("フェーズ突入時に生成する回復シールのプレハブ (HealSticker)。")]
    [SerializeField] private GameObject healStickerPrefab;

    [Header("生成設定")]
    [Tooltip("生成位置・向き。未割り当てならこの GameObject の Transform を使う。")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("生成のトリガーとなる状態名。既定は Down (Phase2 前のダウン=攻撃チャンス)。")]
    [SerializeField] private string triggerStateName = "Down";

    [Tooltip("トリガーのフェーズにつき一度だけ生成する。")]
    [SerializeField] private bool spawnOnce = true;

    [Header("落下演出 (ひらひら)")]
    [Tooltip("ON なら SpawnPoint から地面まで舞い落ちる。OFF なら SpawnPoint に即配置する。")]
    [SerializeField] private bool flutterDrop = true;

    [Tooltip("落下にかける時間 (秒)。")]
    [SerializeField] private float fallDuration = 1.6f;

    [Tooltip("地面判定に使うレイヤー。SpawnPoint から真下へレイを飛ばして着地点を決める。")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("地面が見つからないときに落下する距離 (m)。")]
    [SerializeField] private float maxFallDistance = 20f;

    [Tooltip("左右の揺れ幅 (m)。")]
    [SerializeField] private float swayAmplitude = 0.6f;

    [Tooltip("揺れの速さ。")]
    [SerializeField] private float swayFrequency = 2.5f;

    [Tooltip("ひらひらの傾き角 (度)。")]
    [SerializeField] private float flutterAngle = 35f;

    private bool spawned;

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<B1_Boss>();
        if (boss == null) boss = FindObjectOfType<B1_Boss>();
    }

    // Phase2 突入はゲーム中盤以降に起きるため、全 Awake 完了後の Start で購読すれば取りこぼさない。
    private void Start()
    {
        if (boss == null || boss.StateMachine == null)
        {
            Debug.LogWarning("[B1_PhaseHealStickerSpawner] B1_Boss が見つからないため回復シールを生成できません。", this);
            enabled = false;
            return;
        }
        boss.StateMachine.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (boss != null && boss.StateMachine != null)
            boss.StateMachine.OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(IStatusState state)
    {
        if (state == null || state.StateName != triggerStateName) return;
        if (spawnOnce && spawned) return;
        Spawn();
    }

    /// <summary>Inspector やデバッグからも呼べる生成口。</summary>
    [ContextMenu("回復シールを生成")]
    public void Spawn()
    {
        if (healStickerPrefab == null)
        {
            Debug.LogWarning("[B1_PhaseHealStickerSpawner] HealSticker プレハブが未割り当てです。", this);
            return;
        }
        Transform t = spawnPoint != null ? spawnPoint : transform;
        GameObject instance = Instantiate(healStickerPrefab, t.position, t.rotation);
        spawned = true;

        if (!flutterDrop) return;

        // SpawnPoint から真下へレイを飛ばして着地点（地面）を決める。当たらなければ既定距離だけ落とす。
        Vector3 landPos = t.position + Vector3.down * maxFallDistance;
        if (Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, maxFallDistance,
                            groundMask, QueryTriggerInteraction.Ignore))
            landPos = hit.point;

        // 着地するまでは回収させない（空中で剥がされないように）。
        StickerPickup pickup = instance.GetComponent<StickerPickup>();
        if (pickup != null) pickup.enabled = false;

        B1_HealStickerFall fall = instance.AddComponent<B1_HealStickerFall>();
        fall.Begin(landPos, t.rotation, fallDuration, swayAmplitude, swayFrequency, flutterAngle, pickup);
    }

    private void Reset()
    {
        // Inspector でのセットし忘れ防止：親に B1_Boss があれば自動取得。
        boss = GetComponentInParent<B1_Boss>();
    }
}
