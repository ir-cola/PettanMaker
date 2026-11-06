/*=============================================================================

 File   : [B2_BossBgm.cs]
 Desc   : ボス戦BGMの再生を制御する。BossScene の空GO(Boss2Bgm)にアタッチする。

          ■ なぜ BGMAssets / BGMManager をいじらないのか
            BGMManager.SelectClip() はシーン名が "Stage" で始まるかで選曲する。
            このシーンは "BossScene" なのでどの分岐にも当たらず、放っておくと
            無音のままになる。かといって判定側に BossScene を足したり
            BGMAssets にボス用スロットを増やしたりすると、Title/StageSelect/
            Stage_* など他シーンも通る共有コードを書き換えることになる。
            BGMManager には SetOverride(clip) という「シーン既定より優先して
            流す」静的APIが既にあるので、シーン側の本コンポーネントから
            それを叩くだけにして、共有コードには一切触れない。

          ■ 呼ばれ方
            PlayBossBgm は B2_BossHPBarReveal.onRevealBegan から Inspector 経由で
            呼ばれる(= HPバーがフェードで出始めるのと同時に鳴り始める)。
            どちらもシーン内オブジェクトなので参照を Inspector で解決できる。

 ------------------------------------------------------------------------------

 Date   : 2026/07/17
 Author : akatu

===============================================================================*/

using UnityEngine;

namespace Boss02
{
    /// <summary>ボス戦BGMを BGMManager の上書き指定で鳴らす。</summary>
    public class B2_BossBgm : MonoBehaviour
    {
        [Header("クリップ")]
        [Tooltip("戦闘開始から流すBGM (Stage_1_Boss_0)")]
        [SerializeField] private AudioClip bossBgm;
        [Tooltip("第二形態(土台無し状態)へ移行したら差し替えるBGM (Stage_1_Boss_1)。\n" +
                 "未割り当てなら切り替えを行わず、そのまま bossBgm を流し続ける")]
        [SerializeField] private AudioClip phase2Bgm;

        /// <summary>ボスBGMを流し始める。HPバーのフェード開始と同時に呼ばれる。</summary>
        [ContextMenu("ボスBGMを流す")]
        public void PlayBossBgm()
        {
            BGMManager.SetOverride(bossBgm);
        }

        /// <summary>第二形態のBGMへ差し替える。未割り当てなら何もしない(曲が消えるのを防ぐ)。</summary>
        public void SwitchToPhase2()
        {
            if (phase2Bgm != null) BGMManager.SetOverride(phase2Bgm);
        }

        /// <summary>撃破演出へ入る際にBGMを止める。null 上書きでシーン既定(=BossSceneは無音)へ戻る。</summary>
        public void StopForDefeat()
        {
            BGMManager.SetOverride(null);
        }

        // シーンを出たら通常選曲へ戻す。BGMManager は DontDestroyOnLoad で残るため、
        // 明示的に外さないと次のシーンまでボスBGMを引きずってしまう。
        private void OnDestroy()
        {
            BGMManager.ClearOverride();
        }
    }
}
