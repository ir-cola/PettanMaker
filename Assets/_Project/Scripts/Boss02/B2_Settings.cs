/*=============================================================================

 File   : [B2_Settings.cs]
 Desc   : Boss02の設定クラス
 ------------------------------------------------------------------------------

 Date   : 2026/05/13
 Author : Yokoyama Haruki
 
===============================================================================*/

using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Boss02
{
    public class B2_Settings : MonoBehaviour
    {
        // 土台オブジェクト
        [Header("土台オブジェクト")]
        [SerializeField] private List<GameObject> onBaseObjects;    // 土台ありの行動で使用するオブジェクト
        [Header("土台なし位置")]
        [SerializeField] private Transform offBaseTransform;        // 土台なしの行動で使用する位置
        public ref readonly List<GameObject> OnBaseObjects => ref onBaseObjects;
        public ref readonly Transform OffBaseTransform => ref offBaseTransform;

        // ========================
        // ギミック(スイッチ/シャンデリア)設定
        // ========================
        [Header("スタン設定")]
        [Tooltip("土台が壊れたときの大幅スタンの時間(秒)。ここだけ変えればスタンの長さを調整できる。\n" +
                 "土台破壊の起点は「HPがBaseHealthRatioを下回る」か「シャンデリア着弾」の2つ。\n" +
                 "スタンが解けると土台なし状態へ移行する。\n" +
                 "※ これは『バク宙で着地してから』の時間。バク宙中もスタン扱いなので、\n" +
                 "   状態としての合計は BackflipDuration + この値になる")]
        [SerializeField] private float stunDuration = 7f;

        [Header("土台破壊時のバク宙")]
        [Tooltip("土台が壊れてから後ろへバク宙して着地するまでの時間(秒)")]
        [SerializeField, Min(0.01f)] private float backflipDuration = 0.9f;
        [Tooltip("バク宙で後ろへ下がる距離(m)")]
        [SerializeField] private float backflipBackDistance = 3f;
        [Tooltip("バク宙の弧の高さ(m)。開始位置からどれだけ跳ね上がるか")]
        [SerializeField] private float backflipApexHeight = 2f;
        [Tooltip("バク宙の回転数。1で一回転")]
        [SerializeField] private float backflipSpins = 1f;

        public float BackflipDuration    => backflipDuration;
        public float BackflipBackDistance => backflipBackDistance;
        public float BackflipApexHeight  => backflipApexHeight;
        public float BackflipSpins       => backflipSpins;
        [Header("シャンデリア直撃の効果")]
        [Tooltip("土台がまだ無事なときの分岐点。現在HPが最大HPのこの割合『以上』なら\n" +
                 "High Hp Target まで、下回るなら Mid Hp Target まで一気に削る")]
        [SerializeField, Range(0f, 1f)] private float chandelierHighHpThreshold = 0.75f;
        [Tooltip("HPが分岐点『以上』のときの削り先(最大HPに対する割合)。既定 0.5 = 半分まで減らす")]
        [SerializeField, Range(0f, 1f)] private float chandelierHighHpTarget = 0.5f;
        [Tooltip("HPが分岐点『未満』のときの削り先(最大HPに対する割合)。既定 0.25 = 1/4 まで減らす")]
        [SerializeField, Range(0f, 1f)] private float chandelierMidHpTarget = 0.25f;
        [Tooltip("土台が壊れた後に直撃したときのダメージ。『その時点の現在HP』に対する割合。\n" +
                 "既定 0.2 = 現在HPの1/5を削る(最大HP基準ではないので、減るほど威力も落ちる)")]
        [SerializeField, Range(0f, 1f)] private float chandelierPostBreakDamageRatio = 0.2f;
        [Tooltip("土台が壊れた後に直撃したときのスタン時間(秒)。土台破壊時の StunDuration とは別枠")]
        [SerializeField, Min(0f)] private float chandelierPostBreakStun = 5f;

        public float StunDuration => stunDuration;
        public float ChandelierHighHpThreshold    => chandelierHighHpThreshold;
        public float ChandelierHighHpTarget       => chandelierHighHpTarget;
        public float ChandelierMidHpTarget        => chandelierMidHpTarget;
        public float ChandelierPostBreakDamageRatio => chandelierPostBreakDamageRatio;
        public float ChandelierPostBreakStun      => chandelierPostBreakStun;

        // ========================
        // アクショングループ設定
        // ========================

        // ボスの行動一覧
        public enum ActionType
        {
            Idle,
            SummonSoldier,
            NeedleMissile,
            SwordAttack,
            TrumpBombs,
            SummonHedgehog,
        }

        [System.Serializable]
        public class ActionGroup
        {
            public List<ActionType> actions;
            public float actionWeight;
        }

        // ----- inspecter変数 -----
        [Header("土台あり行動グループ")]
        [SerializeField] private List<ActionGroup> onActionGroup;
        [Header("土台なし行動グループ")]
        [SerializeField] private List<ActionGroup> offActionGroups;

        // ----- 読み取り専用プロパティ -----
        public ref readonly List<ActionGroup> OnBaseActionGroupList => ref onActionGroup;
        public ref readonly List<ActionGroup> OffBaseActionGroupList => ref offActionGroups;

        // ========================
        // アクション個別設定
        // ========================
        [System.Serializable]
        // 剣攻撃/杖攻撃の設定クラス
        public class SwordAttackSettings
        {
            public float chargeTime = 2.5f;    // 攻撃前のチャージ時間
            public float recoveryTime = 3f;  // 攻撃後の隙時間

            [Header("杖VFX")]
            [Tooltip("杖先のトレイル(B2_WandTrailVFX)。振っている間だけ出る。未設定なら何も出ない")]
            public B2_WandTrailVFX wandTrail;
            [Tooltip("チャージ中に杖へ出すエフェクト(VFX_Summon_002 など)。未設定なら何も出ない")]
            public GameObject chargeVfxPrefab;
            [Tooltip("チャージエフェクトを出す位置(杖先)。この子として生成され、杖に追従する")]
            public Transform chargeVfxPoint;
            [Tooltip("チャージエフェクトを消すまでの時間(秒)。0以下なら自動で消さない")]
            public float chargeVfxLifetime = 3f;

            [Header("杖SE")]
            public AudioSource seSource;
            [Tooltip("チャージ開始時のSE (Boss02_Magic など)")]
            public AudioClip chargeSe;
            [Tooltip("振り始めのSE (Boss02_Staff など)")]
            public AudioClip swingSe;
        }

        // ミサイル攻撃の設定クラス
        [System.Serializable]
        public class NeedleMissileSettings
        {
            public GameObject missilePrefab; // ミサイルのプレハブ

            public List<GameObject> missileList; // ミサイルのリスト
            public int activeMissileCount = 7;   // 同時に発射するミサイルの数
            public Transform player;         // プレイヤーのTransform（ミサイルの追尾に使用）
            public float recoveryTime;       // 攻撃後の隙時間

            [Header("引き抜き設定")]
            [Tooltip("引き抜き時の速度制御カーブ")]
            public AnimationCurve pullMoveCurve;
            public float pullMoveDuration = 2f;   // 引き抜きにかかる時間
            public float pullDistance = 5f;       // 引き抜きの距離

            [Header("待ち設定")]
            public float chargeTime = 1.5f;           // 待機する時間
            public float floatAmplitude = 0.3f;       // 浮遊の振幅
            public float floatFrequency = 5f;         // 浮遊の周波数

            [Header("チャージVFX")]
            [Tooltip("チャージ(待ち)開始時に出すエフェクト(VFX_needle_004)。未設定なら何も出ない")]
            public GameObject chargeVfxPrefab;
            [Tooltip("エフェクトを出す位置。この子として生成して動きに追従させるため、\n" +
                     "未設定だと追従先が無いので生成しない")]
            public Transform chargeVfxPoint;
            [Tooltip("エフェクトを消すまでの時間(秒)。0以下なら自動で消さない")]
            public float chargeVfxLifetime = 3f;

            [Header("攻撃設定")]
            public float nearDistance = 3f;   // プレイヤーに近づく距離
            public float speed = 5f;          // 速度
            public float shotDelay = 0.1f;    // 発射前の遅延時間
        }

        // トランプ爆弾の設定クラス
        [System.Serializable]
        public class TrumpBombsSettings 
        {
            public GameObject TrumpPrefab;  // トランプ爆弾のプレハブ
            public GameObject DummyPrefab;  // ダミーオブジェクトのプレハブ
            public int bombCount;           // 爆弾の数
            public int dummyCount;          // ダミーオブジェクトの数
            public Vector3 spawnPosition;   // 爆弾のスポーン位置
            public float spawnDuration;     // 爆弾をスポーンする時間
            public Vector2 spawnVelocityRange;          // 爆弾のスポーン時の速度の範囲
            public Vector2 spawnAngularVelocityRange;   // 爆弾のスポーン時の角速度の範囲
            public Vector2 spawnAngleRange;             // 爆弾のスポーン時の角度の範囲
            public float recoveryTime;          // 攻撃後の隙時間

#if UNITY_EDITOR
            [Tooltip("スポーン位置の可視化用")]
            public bool visualizeSpawnPosition;
#endif
        }

        // ハリネズミ召喚の設定クラス
        [System.Serializable]
        public class SummonHedgehogSettings
        {
            [Tooltip("フラミンゴをつかむ位置(空オブジェクトを作成してアタッチ)")]
            public Transform GrabPoint;
            [Tooltip("フラミンゴのルートオブジェクト(フラミンゴの位置や高さを補完するために使用)")]
            public Transform FlamingoRoot;
            [Tooltip("フラミンゴを持たせる手のボーン(Queen1:Hand_R など)。\n" +
                     "つかんだ瞬間から FlamingoRoot がこのボーンに追従するので、\n" +
                     "振り下ろしアニメにフラミンゴのカーブを入れなくても動きが同期する。\n" +
                     "※ FlamingoRoot に B2_FlamingoHold が必要。\n" +
                     "未設定なら追従しない(振っても置き去りになり、マレットの判定も出ない)")]
            public Transform HandBone;
            [Tooltip("0:開始高さ,1:GrabPointの高さで高さを補完")]
            public AnimationCurve GrabHeightCurve;
            [Tooltip("0:開始位置,1:GrabPointの位置で位置を補完")]
            public AnimationCurve GrabXZCurve;
            [Tooltip("フラミンゴをつかむ時間")]
            public float GrabDuration;
            [Tooltip("フラミンゴの回転速度の閾値")]
            public float AngleThresholdSpeed;
            [Tooltip("ホールド上限時間。マレットが接触しなかった場合の保険打ち出しまでの時間")]
            public float maxHoldTime = 2f;
            public float recoveryTime;          // 攻撃後の隙時間
            public GameObject hedgehogPrefab; // 召喚するハリネズミのプレハブ
            public Transform hedgehogSpawnPoint; // ハリネズミのスポーン位置

            [Header("フラミンゴVFX")]
            [Tooltip("フラミンゴのトレイル(B2_WandTrailVFX / VFX_Flamingo_Golf)。\n" +
                     "つかんでから離すまでの振っている間だけ出る。未設定なら何も出ない")]
            public B2_WandTrailVFX flamingoTrail;
        }

        [System.Serializable]
        public class SummonSoldierSettings
        {
            [Header("召喚設定")]
            public GameObject soldierPrefab;        // 召喚するトランプ兵のプレハブ
            public GameObject SummonEffect;         // 召喚時のエフェクトのプレハブ
            public int soldierCount = 5;            // 召喚するトランプ兵の数
            public int maxSoldierCount = 10;        // 最大召喚数
            public float startDelay = 2.0f;         // 召喚開始までの遅延時間
            public float summonDuration = 0.5f;     // 召喚間隔
            public float minSummonDistance = 5.0f;  // 召喚するトランプ兵の最小距離
            public float maxSummonDistance = 7.0f;  // 召喚するトランプ兵の最大距離
            public float summonHeightOffset = -1.0f;// 召喚するトランプ兵の初期位置の高さオフセット
            public float recoveryTime = 6.0f;       // 攻撃後の隙時間
            [Tooltip("召喚状態から強制的に抜けるまでの時間(秒)。保険。\n" +
                     "何らかの理由で召喚が完了扱いにならない場合でも、ここで必ずリカバリへ移行する")]
            public float maxSummonTime = 15.0f;

            [Header("召喚VFX")]
            [Tooltip("召喚時に女王の杖へ出すエフェクト(VFX_Summon_002)。未設定なら何も出ない")]
            public GameObject casterVfxPrefab;
            [Tooltip("エフェクトを出す位置(杖先)。この子として生成して杖の動きに追従させるため、\n" +
                     "未設定だと追従先が無いので生成しない")]
            public Transform casterVfxPoint;
            [Tooltip("エフェクトを消すまでの時間(秒)。0以下なら自動で消さない")]
            public float casterVfxLifetime = 3f;

            [Header("召喚兵の挙動設定")]
            [Tooltip("召喚兵が召喚にかかる時間")]
            public float summonTime = 1.0f;
            public float summonDelay = 0.5f;        // 召喚兵が召喚されて動き始めるまでの時間
            [Tooltip("召喚されてから上に上がる幅")]
            public float summondHeight = 2.0f;      // 召喚されてから上に上がる幅
        }

        // ----- inspecter変数 -----

        [Header("剣攻撃設定")]
        [SerializeField] private SwordAttackSettings swordAttackSettings;
        [Header("ミサイル攻撃設定")]
        [SerializeField] private NeedleMissileSettings needleMissileSettings;
        [Header("トランプ爆弾設定")]
        [SerializeField] private TrumpBombsSettings trumpBombsSettings;
        [Header("ハリネズミ召喚設定")]
        [SerializeField] private SummonHedgehogSettings summonHedgehogSettings;
        [Header("トランプ兵召喚設定")]
        [SerializeField] private SummonSoldierSettings summonSoldierSettings;
        // ----- 読み取り専用プロパティ -----
        public ref readonly SwordAttackSettings SwordSettings => ref swordAttackSettings;
        public ref readonly NeedleMissileSettings MissileSettings => ref needleMissileSettings;
        public ref readonly TrumpBombsSettings TrumpSettings => ref trumpBombsSettings;
        public ref readonly SummonHedgehogSettings HedgehogSettings => ref summonHedgehogSettings;
        public ref readonly SummonSoldierSettings SoldierSettings => ref summonSoldierSettings;

        private Transform bossCenter;

        // visualize* フラグは #if UNITY_EDITOR 内で宣言されているため、
        // 使用側もガードしないとプレイヤービルドがコンパイルエラーになる。
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {

            if (trumpBombsSettings.visualizeSpawnPosition)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(transform.position + trumpBombsSettings.spawnPosition, 0.5f);
            }

        }
#endif
    }
}
