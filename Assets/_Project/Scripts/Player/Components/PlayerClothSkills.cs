/**===========================================================================

 File   : 服スキル制御 [PlayerClothSkills.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/13
 -----------------------------------------------------------------------------
 服(Cloth)ごとに「パッシブスキル(着ている間ずっと)」と「アクティブスキル
 (Q で発動・時限)」を持たせる基盤。

   ・パッシブ  : 着替えた瞬間に適用され、脱ぐ/着替えるまで持続
   ・アクティブ : インゲームで Q (ゲームパッド=Y/北) を押すと発動。一定時間効果、
                 その後クールダウン。メニュー/シール帳表示中(SetUIMode=true)は
                 PlayerInputHandler.IsSkillPress が false になり発動しない。

 いま何を着ているかは PlayerModelSwitcher.CurrentClothPrefab を毎フレーム見て検知
 (着せ替え経路が複数=ドラッグ/チュートリアル/ドロップ でも一括で拾える)。効果は
 PlayerSizeController(root スケール一元管理) に委譲する。

 アリス: パッシブ=Shrink 0.5(半分) / アクティブ=Giant 3.0・8秒・CD12秒。
 他の服は entries 未登録 = スキル無し(中身は後で追加)。
 ===========================================================================**/

using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerClothSkills : MonoBehaviour {

    public enum PassiveSkill { None, Shrink }   // Shrink = 着ると縮む
    public enum ActiveSkill  { None, Giant }    // Giant  = 時限で巨大化

    /// <summary>現在の体格モード。移動速度と攻撃(コンボ/チャージ/スタンプ)の分岐に使う。
    /// Small = Shrink 服を着ている(通常時) / Giant = 巨大化アクティブ発動中 / None = それ以外。</summary>
    public enum SizeMode { None, Small, Giant }

    /// <summary>服 1 着分のスキル定義。</summary>
    [Serializable]
    public class ClothSkillEntry {
        [Tooltip("対象の服 prefab (EquipCloth / CHANGE カタログに渡すものと同一参照)。例: Cloth_Alice")]
        public GameObject clothPrefab;
        [Tooltip("インスペクタ可読用ラベル(任意)")]
        public string label;

        [Header("パッシブ (着ている間ずっと)")]
        public PassiveSkill passive = PassiveSkill.None;
        [Tooltip("Shrink 時の体格倍率 (0.5 = 半分)")]
        public float passiveSizeMul = 0.5f;
        [Tooltip("小さいとき(パッシブ)の絶対移動速度。<=0 で上書きなし=既定速度。アリス=7")]
        public float passiveMoveSpeed = 0f;
        [Tooltip("巨大化中(アクティブ)の絶対移動速度。<=0 で上書きなし=既定速度。アリス=10")]
        public float activeMoveSpeed = 0f;
        [Tooltip("小さいとき(パッシブ)のジャンプ力倍率 (1=既定のまま / 0.5=半分 / 2=2倍)")]
        public float passiveJumpMul = 1f;
        [Tooltip("巨大化中(アクティブ)のジャンプ力倍率 (1=既定 / 1.5=1.5倍高く跳ぶ)")]
        public float activeJumpMul = 3f;

        [Header("アクティブ (Q で発動・時限)")]
        public ActiveSkill active = ActiveSkill.None;
        [Tooltip("Giant 時の体格倍率 (3 = 通常の約3倍)。発動中はパッシブを上書きする")]
        public float activeSizeMul = 3f;
        [Tooltip("効果の持続時間(秒)")]
        public float activeDuration = 8f;
        [Tooltip("再発動までのクールダウン(秒)")]
        public float activeCooldown = 12f;
        [Tooltip("発動時にプレイヤー中心で再生する任意エフェクト(CFXR Magic Poof 等・null 可)")]
        public GameObject activateVfxPrefab;
        [Tooltip("エフェクトの自動破棄秒")]
        public float activateVfxLifetime = 3f;
        [Tooltip("エフェクトの基準スケール(プレイヤーに追従。1=プレイヤー等倍相当)")]
        public float activateVfxBaseScale = 1f;
        [Tooltip("エフェクトをプレイヤーの子にして体格の動的変化(縮小/巨大化)にサイズを追従させる")]
        public bool activateVfxParentToPlayer = true;

        [Header("SE")]
        [Tooltip("アクティブスキル発動時に鳴らすSE (アリス大きくなる 等)。")]
        public AudioClip activateSe;
        [Tooltip("この服を着た瞬間に鳴らすSE (アリス小さくなる 等のパッシブ着用音)。")]
        public AudioClip equipSe;
        [Range(0f, 1f)]
        public float seVolume = 1f;
    }

    [Header("服ごとのスキル定義 (アリスのみ登録・他は空でOK)")]
    [SerializeField] private List<ClothSkillEntry> entries = new List<ClothSkillEntry>();

    [Header("参照 (未設定は自動解決)")]
    [Tooltip("装備中の服を知るための PlayerModelSwitcher。未設定なら子から自動取得")]
    [SerializeField] private PlayerModelSwitcher modelSwitcher;
    [Tooltip("体格スケール制御。未設定なら同じ GameObject から取得/自動追加")]
    [SerializeField] private PlayerSizeController sizeController;
    [Tooltip("スキルゲージ。未設定なら同じ GameObject から取得/自動追加")]
    [SerializeField] private SkillGauge gauge;

    private PlayerLifetimeScope _player;    // Q 入力 (IsSkillPress) の取得元
    private GameObject          _lastCloth; // 直近の装備服 (変化検知)
    private ClothSkillEntry     _current;   // 現装備服のスキル定義 (無ければ null)

    // ---- サイズモード (攻撃・移動速度の分岐用。毎フレーム更新) ----
    /// <summary>現在の体格モード。Small=縮小服着用中 / Giant=巨大化中 / None=それ以外。</summary>
    public SizeMode Mode { get; private set; } = SizeMode.None;

    // ---- HUD 等から参照する状態 ----
    public bool  HasActiveSkill    => _current != null && _current.active != ActiveSkill.None;
    public bool  IsActiveRunning   => sizeController != null && sizeController.IsActive;
    /// <summary>スキルゲージが満タンで発動できる状態か。</summary>
    public bool  IsSkillReady      => HasActiveSkill && gauge != null && gauge.IsFull && !IsActiveRunning;
    /// <summary>ゲージの溜まり 0..1 (無し=0)。</summary>
    public float GaugeValue01      => gauge != null ? gauge.Value01 : 0f;
    /// <summary>アクティブの残り 0..1 (未発動=0)。</summary>
    public float ActiveRemaining01 {
        get {
            if (_current == null || sizeController == null || !sizeController.IsActive || _current.activeDuration <= 0f) return 0f;
            return Mathf.Clamp01(sizeController.ActiveRemaining / _current.activeDuration);
        }
    }

    private void Awake() {
        _player = GetComponent<PlayerLifetimeScope>();
        if (_player == null) _player = GetComponentInParent<PlayerLifetimeScope>();
        if (modelSwitcher == null) modelSwitcher = GetComponentInChildren<PlayerModelSwitcher>(true);
        if (sizeController == null) {
            sizeController = GetComponent<PlayerSizeController>();
            if (sizeController == null) sizeController = gameObject.AddComponent<PlayerSizeController>();
        }
        if (gauge == null) {
            gauge = GetComponent<SkillGauge>();
            if (gauge == null) gauge = gameObject.AddComponent<SkillGauge>();
        }
        gauge.OnDrainEmpty += HandleGaugeEmpty;
    }

    private void OnDestroy() {
        if (gauge != null) gauge.OnDrainEmpty -= HandleGaugeEmpty;
    }

    // ゲージが尽きた = 制限時間切れ。持続スキル(巨大化)を終了させる。
    private void HandleGaugeEmpty() {
        if (sizeController != null) sizeController.CancelActive();
    }

    private void Update() {
        // 装備服の変化を検知してパッシブを適用
        GameObject cloth = modelSwitcher != null ? modelSwitcher.CurrentClothPrefab : null;
        if (cloth != _lastCloth) {
            _lastCloth = cloth;
            OnClothChanged(cloth);
        }

        // Q(=IsSkillPress。メニュー中は false)でアクティブ発動
        if (_player != null && _player.Input != null && _player.Input.IsSkillPress) {
            TryActivate();
        }

        // サイズモードの確定 + 移動速度の上書き (小=7 / 巨大=10 / それ以外=既定)。
        UpdateSizeModeAndSpeed();

        // ゲージの充填許可: スキル服着用中 & 未発動 & ロード外のときだけ溜まる。
        if (gauge != null)
            gauge.SetChargingAllowed(HasActiveSkill && !IsActiveRunning && !SkinInset.IsBusy);
    }

    /// <summary>現在の装備服とアクティブ状態から体格モードを確定し、移動速度の上書きを毎フレーム反映する。</summary>
    private void UpdateSizeModeAndSpeed() {
        bool shrinkCloth = _current != null && _current.passive == PassiveSkill.Shrink;
        bool giant = _current != null && _current.active == ActiveSkill.Giant
                  && sizeController != null && sizeController.IsActive;

        Mode = giant ? SizeMode.Giant : (shrinkCloth ? SizeMode.Small : SizeMode.None);

        if (_player == null) return;

        // 速度は <=0 で上書きなし(絶対値)、ジャンプは倍率(既定1)。ともにシーンの entry で調整する。
        float overrideSpeed = -1f;
        float jumpMul = 1f;
        if (_current != null) {
            if (Mode == SizeMode.Small) {
                if (_current.passiveMoveSpeed > 0f) overrideSpeed = _current.passiveMoveSpeed;
                jumpMul = _current.passiveJumpMul;
            }
            else if (Mode == SizeMode.Giant) {
                if (_current.activeMoveSpeed > 0f) overrideSpeed = _current.activeMoveSpeed;
                jumpMul = _current.activeJumpMul;
            }
        }
        _player.SetSizeSpeedOverride(overrideSpeed);
        _player.SetSizeJumpMul(jumpMul);
    }

    private void OnClothChanged(GameObject cloth) {
        _current = FindEntry(cloth);

        // 着用音 (アリス小さくなる 等のパッシブ着用音)
        if (_current != null && _current.equipSe != null)
            AudioSource.PlayClipAtPoint(_current.equipSe, transform.position, _current.seVolume);

        // アクティブは服が変わったら打ち切る (アリスの巨大化中に脱いだら戻す)
        if (sizeController != null) {
            sizeController.CancelActive();
            float passiveMul = (_current != null && _current.passive == PassiveSkill.Shrink)
                             ? _current.passiveSizeMul : 1f;
            sizeController.SetPassiveScale(passiveMul);
        }
        // 服が変わったらゲージをリセット (新しい服のスキルとして 0 から溜め直す)
        if (gauge != null) gauge.ResetGauge();
    }

    private void TryActivate() {
        if (sizeController == null) return;
        if (_current == null || _current.active == ActiveSkill.None) return; // スキル無し服 = 何もしない
        if (SkinInset.IsBusy) return;                 // ロード中は不発
        if (sizeController.IsActive) return;          // 発動中は再発動しない
        if (gauge != null && !gauge.IsFull) {         // ゲージが満タンでないと発動できない
            Debug.Log("[PlayerClothSkills] ゲージ未充填 (" + (gauge.Value01 * 100f).ToString("0") + "%)");
            return;
        }

        // 現状のアクティブは Giant のみ。ゲージを「制限時間」として満タンから消費する。
        sizeController.TriggerActive(_current.activeSizeMul, _current.activeDuration);
        if (gauge != null) gauge.BeginDrain(_current.activeDuration);
        SpawnActivateVfx();
        if (_current.activateSe != null)
            AudioSource.PlayClipAtPoint(_current.activateSe, transform.position, _current.seVolume);
        Debug.Log("[PlayerClothSkills] アクティブ発動: " +
                  (string.IsNullOrEmpty(_current.label) ? _current.active.ToString() : _current.label) +
                  " (x" + _current.activeSizeMul + " / " + _current.activeDuration + "s)");
    }

    private void SpawnActivateVfx() {
        if (_current == null || _current.activateVfxPrefab == null) return;

        // 生成位置 = プレイヤーの中心 (BookPanel ビューポートの着せ替えと同じ胴中央)。
        // CharacterController.center をワールドへ変換して足元でなく体の中心に出す。
        CharacterController cc = GetComponent<CharacterController>();
        Vector3 centerWorld = cc != null ? transform.TransformPoint(cc.center) : transform.position;

        GameObject fx = Instantiate(_current.activateVfxPrefab, centerWorld, transform.rotation);
        float baseScale = Mathf.Max(0.0001f, _current.activateVfxBaseScale);

        if (_current.activateVfxParentToPlayer) {
            // プレイヤーの子にして体格の動的変化(縮小/巨大化)にサイズを追従させる。
            fx.transform.SetParent(transform, worldPositionStays: false);
            fx.transform.localPosition = cc != null ? cc.center : Vector3.zero;
            fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale    = Vector3.one * baseScale;
            // ワールドシミュレーションのパーティクルもルートスケールへ追従させる。
            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++) {
                ParticleSystem.MainModule main = systems[i].main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }
        else {
            fx.transform.localScale = fx.transform.localScale * baseScale;
        }

        Destroy(fx, Mathf.Max(0.1f, _current.activateVfxLifetime));
    }

    // 服 prefab に対応するスキル定義を探す (参照一致、無ければ名前一致)
    private ClothSkillEntry FindEntry(GameObject cloth) {
        if (cloth == null || entries == null) return null;
        for (int i = 0; i < entries.Count; i++) {
            var e = entries[i];
            if (e == null || e.clothPrefab == null) continue;
            if (e.clothPrefab == cloth) return e;
            if (string.Equals(e.clothPrefab.name, cloth.name, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }
}
