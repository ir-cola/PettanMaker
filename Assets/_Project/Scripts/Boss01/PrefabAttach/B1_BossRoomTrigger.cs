using UnityEngine;

/// <summary>
/// プレイヤーがエリアに入ったら戦闘を開始するトリガー。
/// Collider (Is Trigger = true) をアタッチした GameObject に置く。
/// </summary>
public class B1_BossRoomTrigger : MonoBehaviour
{
    [SerializeField] private B1_Boss boss;

    [Header("BGM")]
    [Tooltip("プレイヤーがボス部屋へ入った瞬間から流すボス戦BGM。未割り当てなら現在のBGMを継続する。")]
    [SerializeField] private AudioClip bossBgm;

    [Tooltip("入室時にスカイドームをブレンド切り替えする（任意）。")]
    [SerializeField] private SkydomeBlendSystem skydomeBlendSystem;

    [Header("ゲート（任意）")]
    [Tooltip("入室時にゲートを上げて壁コリジョンを有効化する。撃破時の解除はコントローラ側が自動で行う。")]
    [SerializeField] private B1_RoomGateController gateController;

    private bool triggered;
    private bool startedBgm;

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<B1_Boss>();
        if (boss == null) boss = FindObjectOfType<B1_Boss>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;
        if (boss == null)
        {
            Debug.LogWarning("[B1_BossRoomTrigger] Boss is not assigned, so combat intro cannot start.", this);
            return;
        }

        triggered = true;
        PlayBossBgm();
        gateController?.Activate();

        // B1_Entrance_Map(マップ演出)があれば先に再生 → 完了後に B1_Entrance_Boss(ボス出現)へ。
        // カメラの受け渡し(TPS⇔CinemachineBrain)は B1_CameraDirector が一元管理する。
        if (boss.CameraDirector != null)
            boss.CameraDirector.PlayEntranceMap(StartCombat);
        else
            StartCombat();
    }

    /// <summary>Inspector・Timeline・UnityEvent からも呼べるボスBGMの再生口。</summary>
    [ContextMenu("ボスBGMを流す")]
    public void PlayBossBgm()
    {
        if (bossBgm == null) return;

        BGMManager.SetOverride(bossBgm);
        startedBgm = true;
    }

    // ボス出現(カメラ演出 + スカイドーム切替)。登場SE は咆哮ビートで B1_CameraDirector が鳴らす。
    private void StartCombat()
    {
        boss.StateMachine.EnterCombat();

        if (skydomeBlendSystem != null)
        {
            skydomeBlendSystem.Blend();
        }
    }

    private void Reset()
    {
        // Inspector でセットし忘れ防止：親に B1_Boss があれば自動取得
        boss = GetComponentInParent<B1_Boss>();
    }

    private void OnDestroy()
    {
        // BGMManager はシーンをまたいで残るため、このトリガーが開始した上書きだけ解除する。
        if (startedBgm) BGMManager.ClearOverride();
    }
}
