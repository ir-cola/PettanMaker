using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class BgmActionAccentConfig
{
    [Tooltip("攻撃アクションに合わせたBGM音量アクセントを有効にする。BGMクリップ自体は切り替えない。")]
    public bool Enabled = true;

    [Header("Rush / TailAttack の溜め")]
    [Range(0f, 1f)] public float AnticipationVolume = 0.78f;
    [Min(0f)] public float AnticipationFadeDuration = 0.2f;
    [Min(0f)] public float ReleaseDuration = 0.08f;

    [Header("TailRain の見下ろし連撃")]
    [Range(0f, 1f)] public float TailRainBedVolume = 0.9f;
    [Min(0f)] public float TailRainBedFadeDuration = 0.25f;
}

[Serializable]
public class AimCrouchActionConfig
{
    [Tooltip("Scratch・TailAttack・TailRainの直前に、猫が獲物を狙う低い構えを挟む。Chargeは専用の沈み込みを使用する。")]
    public bool Enabled = true;

    [Min(0f)]
    [Tooltip("通常姿勢から腰を落とすまでの時間（秒）。")]
    public float EnterDuration = 0.16f;

    [Min(0f)]
    [Tooltip("低い姿勢のままプレイヤーを狙う時間（秒）。")]
    public float HoldDuration = 0.1f;

    [Min(0f)]
    [Tooltip("構えを解放して攻撃へ移るまでの時間（秒）。")]
    public float ReleaseDuration = 0.08f;

    [Range(0f, 0.5f)]
    [Tooltip("腰を落とす量。0.18ならモデルのYスケールを最大18%潰す。")]
    public float CrouchAmount = 0.18f;

    [Range(1f, 1.5f)]
    [Tooltip("構え中に胴体を横へ広げる倍率。猫の踏ん張りを強調する。")]
    public float WidthScale = 1.08f;

    [Min(0f)]
    [Tooltip("構え中にプレイヤーへ正対する速度倍率。Boss共通のTurnSpeedへ乗算する。")]
    public float TurnSpeedMultiplier = 2f;
}

[Serializable]
public class IdleActionConfig
{
    [Tooltip("待機する時間（秒）。この時間が経過すると次の行動を選択する。")]
    public float Duration = 1.5f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;
}

[Serializable]
public class WalkActionConfig
{
    [Tooltip("歩き接近が候補になる最低距離（m）。プレイヤーがこの距離より遠いときだけ候補に入る。")]
    public float ActivateRange = 6f;

    [Tooltip("歩き接近を終了する距離（m）。プレイヤーがこの距離以下に近づいたら歩きを止める。")]
    public float StopRange = 6f;

    [Tooltip("歩き時の移動速度（m/s）。")]
    public float Speed = 3f;

    [Tooltip("歩きアクションの最大継続時間（秒）。到達前でもこの時間で強制終了する。")]
    public float MaxDuration = 3f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;
}

[Serializable]
public class RunActionConfig
{
    [Tooltip("走り接近が候補になる最低距離（m）。プレイヤーがこの距離より遠いときだけ候補に入る。")]
    public float ActivateRange = 12f;

    [Tooltip("走り接近を終了する距離（m）。プレイヤーがこの距離以下に近づいたら走りを止める。")]
    public float StopRange = 6f;

    [Tooltip("走り時の移動速度（m/s）。Walk.Speed より大きい値を推奨。")]
    public float Speed = 8f;

    [Tooltip("走りアクションの最大継続時間（秒）。到達前でもこの時間で強制終了する。")]
    public float MaxDuration = 3f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;
}

[Serializable]
public class BackStepActionConfig
{
    [Tooltip("後退が候補になる最大距離（m）。攻撃直後、プレイヤーがこの距離以内なら距離を取りにいく。")]
    public float TriggerRange = 8f;

    [Tooltip("後退する距離（m）。")]
    public float Distance = 5f;

    [Tooltip("後退の移動速度（m/s）。素早く引くため Walk.Speed より速め推奨。\nジャンプ滞空時間は Distance / Speed で決まる。")]
    public float Speed = 9f;

    [Tooltip("後退ジャンプの高さ（m）。BackStep_01(飛び上がり)〜03(着地) の放物線の頂点。")]
    public float JumpHeight = 2.5f;

    [Tooltip("円形マップの半径（m）。B1_Boss の MapCenter が設定されている場合のみ有効。\n真後ろが場外になる壁際では、マップ中心方向へ飛んで場内に戻る。")]
    public float MapRadius = 20f;

    [Tooltip("マップ端からの安全マージン（m）。着地点はこの内側に収める。")]
    public float EdgeMargin = 1.5f;

    [Tooltip("着地モーション後、次の行動に移るまでの秒数。")]
    public float LandRecovery = 0.35f;

    [Tooltip("跳ぶ前の振り向き速度（度/秒）。進行方向と反対（後退なら プレイヤー側）を向いてから跳ぶ。")]
    public float TurnSpeed = 540f;

    [Tooltip("後退アクションの最大継続時間（秒）。目標到達前でもこの時間で終了する。")]
    public float MaxDuration = 1.2f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;
}

[Serializable]
public class ScratchHitboxTiming
{
    [Range(0f, 1f)]
    [Tooltip("ヒットボックスが出現する正規化タイミング（0=クリップ先頭, 1=クリップ末尾）。\n爪が振り下ろされる直前に合わせる。")]
    public float EnterTime = 0.4f;

    [Range(0f, 1f)]
    [Tooltip("ヒットボックスが消える正規化タイミング（0=クリップ先頭, 1=クリップ末尾）。\nEnterTime より大きい値にすること。爪が引っ込むタイミングに合わせる。")]
    public float ExitTime = 0.7f;
}

[Serializable]
public class ScratchAnimationSpeedTiming
{
    [Range(0.05f, 2f)]
    [Tooltip("振りかぶり中のAnimator再生速度。1未満にすると攻撃前の溜めが強調される。")]
    public float AnticipationSpeed = 0.75f;

    [Range(0.05f, 3f)]
    [Tooltip("振り抜き中のAnimator再生速度。1より大きくすると爪を鋭く振る。")]
    public float SwingSpeed = 1.3f;

    [Range(0f, 1f)]
    [Tooltip("予備動作速度から振り抜き速度へ切り替える正規化タイミング（0=クリップ先頭, 1=クリップ末尾）。HitboxTimings.EnterTimeより少し前を推奨。")]
    public float SwingStartTime = 0.25f;
}

[Serializable]
public class ScratchActionConfig
{
    [Tooltip("ひっかきが行動候補になる最大距離（m）。プレイヤーがこの距離以内のときだけ選ばれる。")]
    public float AttackRange = 6f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;

    [Tooltip("攻撃終了後に硬直（その場待機）する時間（秒）。「避けたら殴れる」反撃猶予を作る。0 で即次行動。")]
    public float RecoveryDuration = 0.6f;

    [Tooltip("1連・2連・3連それぞれのダメージ量。index 0 = Scratch01、index 1 = Scratch02、index 2 = Scratch03。")]
    public int[] DamagePerHit = { 1, 1, 2 };

    [Tooltip("Player の被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int DamageType = 0;

    [Tooltip("1連・2連・3連それぞれのヒット時演出（ヒットストップ/カメラシェイク）。\n小 → 中 → 大 で重みを増すと気持ちよい。3連目だけ Boss 本体も止め、全画面ヒットストップで「ズドン」を出す。\nindex 0 = Scratch01、1 = Scratch02、2 = Scratch03。")]
    public DamageContext[] EffectsPerHit =
    {
        new DamageContext { targetHitstop = 0.05f, shakeDuration = 0.08f, shakeIntensity = 0.10f },
        new DamageContext { targetHitstop = 0.07f, shakeDuration = 0.10f, shakeIntensity = 0.14f },
        new DamageContext { targetHitstop = 0.10f, sourceHitstop = 0.10f, shakeDuration = 0.15f, shakeIntensity = 0.25f, globalHitstop = 0.06f, globalHitstopScale = 0.05f },
    };

    [Tooltip("1連・2連・3連攻撃それぞれの選択重み。\n例：{ 1, 1, 2 } なら3連が2倍選ばれやすい。値が大きいほど選ばれやすい。")]
    public float[] HitCountWeights = { 1f, 1f, 1f };

    [Tooltip("1連・2連・3連それぞれの踏み込み距離（m）。AdvanceDuration 秒かけて前方へ移動する。\nindex 0 = Scratch01、1 = Scratch02、2 = Scratch03。")]
    public float[] AdvancePerHit = { 0.5f, 0.5f, 0.8f };

    [Tooltip("踏み込みにかける時間（秒）。小さいほど鋭い踏み込みになる。")]
    public float AdvanceDuration = 0.3f;

    [Tooltip("Scratch01/02/03 それぞれのヒットボックス出現・消滅タイミング。\nindex 0 = Scratch01、index 1 = Scratch02、index 2 = Scratch03。")]
    public ScratchHitboxTiming[] HitboxTimings =
    {
        new ScratchHitboxTiming(),
        new ScratchHitboxTiming(),
        new ScratchHitboxTiming(),
    };

    [Tooltip("Scratch01/02/03それぞれの予備動作速度・振り抜き速度・切替タイミング。\nindex 0 = Scratch01、index 1 = Scratch02、index 2 = Scratch03。")]
    public ScratchAnimationSpeedTiming[] AnimationSpeedTimings =
    {
        new ScratchAnimationSpeedTiming { AnticipationSpeed = 0.75f, SwingSpeed = 1.25f, SwingStartTime = 0.22f },
        new ScratchAnimationSpeedTiming { AnticipationSpeed = 0.72f, SwingSpeed = 1.30f, SwingStartTime = 0.14f },
        new ScratchAnimationSpeedTiming { AnticipationSpeed = 0.62f, SwingSpeed = 1.45f, SwingStartTime = 0.14f },
    };
}

[Serializable]
public class ChargeActionConfig
{
    [Tooltip("タメ攻撃が候補になる最低距離（m）。プレイヤーがこの距離より近いと候補から外れる。\nScratch.AttackRange より大きい値にすること。")]
    public float MinRange = 6f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 0f;

    [Tooltip("タメ攻撃が候補になる最大距離（m）。プレイヤーがこの距離より遠いと候補から外れる。")]
    public float MaxRange = 18f;

    [Tooltip("タメ演出の時間（秒）。この秒数が経過すると突進（Rush）フェーズへ移行する。")]
    public float Duration = 2.5f;

    [Tooltip("タメ中のスケール最大値。1.3 なら通常サイズの1.3倍まで膨らむ。")]
    public float ScalePeak = 1.3f;

    [Tooltip("タメ開始時のスケール振動の速さ（Hz）。")]
    public float ScaleFrequency = 3f;

    [Tooltip("タメ完了直前のスケール振動の速さ（Hz）。ScaleFrequencyより大きくすると、溜めが進むほど震えが加速する。")]
    public float ScaleFrequencyEnd = 9f;

    [Range(0f, 0.5f)]
    [Tooltip("Charge開始時に体を低く沈める量。0=変化なし、0.2=Yスケールを20%潰す。")]
    public float CrouchAmount = 0.2f;

    [Min(0f)]
    [Tooltip("通常姿勢から低い構えへ移る時間（秒）。0なら即座に沈み込む。")]
    public float CrouchDuration = 0.2f;
}

[Serializable]
public class TailAttackActionConfig
{
    [Tooltip("テールアタックが候補になる最低距離（m）。近すぎると候補から外れる。")]
    public float MinRange = 2f;

    [Tooltip("テールアタックが候補になる最大距離（m）。遠すぎると候補から外れる。")]
    public float MaxRange = 12f;

    [Tooltip("ジャンプ前の短いタメ時間（秒）。この時間が経過するとジャンプに移行する。")]
    public float ChargeDuration = 0.6f;

    [Tooltip("TailAttackJumpの再生・ジャンプ時間（秒）。この時間内で上昇し、終了時には着地点のY座標へ戻る。")]
    public float JumpDuration = 0.7f;

    [Tooltip("TailAttackJump中の最高到達高度（m）。TailAttack開始時にはこの高さから地面へ戻っている。")]
    public float JumpHeight = 4f;

    [Header("空中TailAttackの進行")]
    [Min(0f)]
    [Tooltip("上空へ到達してTailAttackを再生してから、衝撃波とAnimationStopを発生させるまでの時間（秒）。")]
    public float ImpactDelay = 0.22f;

    [Min(0f)]
    [Tooltip("衝撃波発生時にTailAttackアニメーションを完全停止する時間（秒）。")]
    public float AnimationStopDuration = 0.2f;

    [Min(0f)]
    [Tooltip("AnimationStop解除後、TailAttackをループ再生して滞空する時間（秒）。")]
    public float TailAttackLoopDuration = 0.8f;

    [Min(0.05f)]
    [Tooltip("TailAttackLandを再生する時間（秒）。この区間ではBoss01の座標を変更しない。")]
    public float TailAttackLandDuration = 0.45f;

    [Tooltip("着地時の範囲攻撃の半径（m）。")]
    public float AoeRadius = 3.5f;

    [Tooltip("着地位置を基準にした範囲攻撃中心のローカルOffset。X=右、Y=上、Z=前。")]
    public Vector3 AoeOffset = Vector3.zero;

    [Tooltip("着地時のダメージ量。")]
    public int Damage = 3;

    [Tooltip("着地攻撃の Player 被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int DamageType = 1;

    [Tooltip("着地ダメージ時のヒットストップ・カメラシェイク設定。対象にヒットした場合のみ発生する。")]
    public DamageContext Effects;

    [Header("着地の重量感（ヒットの有無に関わらず必ず発生）")]
    [Tooltip("着地の瞬間に必ず入れるカメラシェイクの時間（秒）。0 で無効。\nEffects のシェイクは着弾がヒットしたときだけなので、外しても『地面が揺れた』手応えを出すために独立させている。")]
    public float LandingShakeDuration = 0.22f;

    [Tooltip("着地カメラシェイクの揺れ幅（m）。0.05〜0.3 が目安。")]
    public float LandingShakeIntensity = 0.18f;

    [Tooltip("着地カメラシェイクの揺れ速度。")]
    public float LandingShakeFrequency = 22f;

    [Header("振り抜きの緩急（Scratch の AnimationSpeedTimings と同じ考え方）")]
    [Range(0.05f, 2f)]
    [Tooltip("ジャンプ中（予備動作）の Animator 再生速度。1未満で滞空のタメが強調される。")]
    public float AnticipationSpeed = 0.7f;

    [Range(0.05f, 3f)]
    [Tooltip("着地の叩きつけ瞬間からの Animator 再生速度。1より大きくすると鋭く振り下ろす。")]
    public float SwingSpeed = 1.35f;

    [Tooltip("ダメージをドーナッツ状に外へ広がる波にする。\nOFF なら従来どおり着地と同時に AoeRadius 全範囲へ即ダメージ。")]
    public bool UseDamageWave = true;

    [Tooltip("波面が穴(WaveInnerRadius)から外周(AoeRadius)まで広がりきる時間（秒）。小さいほど速い波。")]
    public float WaveDuration = 0.4f;

    [Tooltip("ドーナッツの穴の半径（m）。ここより内側にいる対象は波に当たらない安全地帯。\n波はこの半径から始まり外へ広がる。0 で穴なし。")]
    public float WaveInnerRadius = 1f;

    [Tooltip("波の見た目リング(帯)の幅（m）。ダメージ自体は波面(帯の先端)が通過した瞬間に一度だけ入る。")]
    public float WaveThickness = 0.6f;

    [Tooltip("この高さ(m)以上ジャンプしていれば衝撃波が足元を通り抜けて回避できる。\n波の見た目もこの高さで山なりに立ち上がり、「頭を越えれば安全」を画で示す。\n0 で高さ無視（必ず当たる＝従来の平面）。プレイヤーのジャンプ到達高より少し低めが目安。")]
    public float JumpDodgeHeight = 1.2f;

    [Tooltip("波リングの発光色。頂点カラーとしてリングに乗る（先端が明るく後端が透明）。")]
    public Color WaveColor = new Color(1f, 0.55f, 0.15f, 1f);

    [Tooltip("円周上のトゲの深さ（帯幅に対する倍率）。0 で完全な真円の波面。")]
    public float WaveSpikeDepth = 0.9f;

    [Tooltip("波リングに貼るマテリアル。プロシージャル生成した地面リングメッシュへ適用する。\n未設定なら URP のパーティクル系シェーダで簡易表示。頂点カラー対応のアンリット/パーティクル系推奨。")]
    public Material WaveMaterial;

    [Tooltip("ダメージ波の波面がプレイヤーを通過する瞬間、画面を軽く歪ませる。")]
    public bool ScreenDistortOnWave = true;

    [Header("着弾予告サークル")]
    [Tooltip("ジャンプ中、着地地点に予告サークル（中心が透明→外へ赤のグラデーション円）を表示する。")]
    public bool UseTelegraph = true;

    [Tooltip("予告サークル兼、着弾瞬間の直接ダメージ判定の半径（m）。外側は AoeRadius まで衝撃波が広がる。")]
    public float TelegraphRadius = 3.5f;

    [Tooltip("予告サークルの外縁色。中心は透明で、外周へ向かってこの色が濃くなる。")]
    public Color TelegraphColor = new Color(1f, 0.12f, 0.08f, 1f);

    [Range(0f, 1f)]
    [Tooltip("ジャンプ開始直後（着地がまだ先）のときの濃さ。着地直前は常に 1（最大の濃さ）なので、\nこの値が小さいほど表示開始と着地直前の差が大きくなる。1 で差なし（常に最大の濃さ）。")]
    public float TelegraphFarAlpha = 0.25f;

    [Range(1f, 4f)]
    [Tooltip("着地直前で予告サークルの明滅を何倍速にするか。1 で明滅速度は変化しない。")]
    public float TelegraphPulseBoost = 2f;

    [Tooltip("予告サークルのマテリアル。未設定なら PettanMaker/Boss_01/TelegraphCircle シェーダで自動生成。\n（Shader.Find だけの参照はビルドで剥がされることがあるため、ビルドで確実に出すなら割り当てる）")]
    public Material TelegraphMaterial;

    [Header("マナバーストクロス（着地後、導火線式に走る魔力の地面爆発）")]
    [Tooltip("着地の衝撃波の後、着地点から予告サークルを並べ、中心から外へ導火線のように地面爆発を走らせる。\nボスはしっぽ立ちループ中のままでよく、追加モーション不要。")]
    public bool UseBurstCross = true;

    [Tooltip("爆発の並べ方（RandomizeBurstPattern が OFF のときの固定パターン）。\nCross=十字（1リング=1拍。斜め45°が安全地帯）\nSpiral=渦巻き（1円=1拍で「グルグルグルッ」。渦と逆回りに歩くと安全。BurstIntervalDecay は 0.92〜0.97 推奨）\nRing=同心円の波紋（1リング全周=1拍。リング間の隙間で避ける）\nPinwheel=風車（十字の腕が外へ進むごとに曲がる）\nScatter=ランダム散布（方角は毎回変わるが拍は内→外へ進む）")]
    public B1_ManaBurstCross.Pattern BurstPattern = B1_ManaBurstCross.Pattern.Cross;

    [Tooltip("ON にすると発動のたびに全パターンからランダムで選ぶ（直前と同じパターンは連続しない）。\nOFF なら常に BurstPattern を使う。")]
    public bool RandomizeBurstPattern = true;

    [Tooltip("怒りモード（激昂＝Phase2）中は回転系パターンに限定する。\nRandomizeBurstPattern が ON なら Spiral / Pinwheel から抽選、OFF なら常に Spiral。")]
    public bool EnragedUseSpiral = true;

    [Min(1)]
    [Tooltip("[Cross] 爆発の腕の本数。4で十字。")]
    public int BurstArmCount = 4;

    [Tooltip("腕全体の回転オフセット（度）。0 でボス正面基準の＋字、45 で×字。")]
    public float BurstAngleOffset = 0f;

    [Min(1)]
    [Tooltip("腕1本あたりの予告サークルの数。1個 = 爆発リズムの1拍。")]
    public int BurstCirclesPerArm = 3;

    [Tooltip("着地点の中心から最初のサークルまでの距離（m）。衝撃波の穴(WaveInnerRadius)より外が目安。")]
    public float BurstStartDistance = 2.5f;

    [Tooltip("サークル同士の間隔（m）。")]
    public float BurstCircleSpacing = 2.2f;

    [Tooltip("最内周サークルの爆発半径（m）。")]
    public float BurstCircleRadius = 1.1f;

    [Min(0f)]
    [Tooltip("外へ1リングごとに足す半径（m）。外周ほど大きい円になり「外へ広がる魔力」を見せる。0 で全部同じ大きさ。")]
    public float BurstRadiusGrowth = 0.25f;

    [Min(2)]
    [Tooltip("[Spiral] 円の総数（=拍数）。多いほど細かい連打になる。1発あたりのダメージは軽めに。")]
    public int SpiralCircleCount = 12;

    [Min(0.25f)]
    [Tooltip("[Spiral] 渦の周回数。1周ごとに BurstCircleSpacing ぶん外へ広がる。")]
    public float SpiralTurns = 1.5f;

    [Tooltip("予告サークル表示から初弾爆発までのチャージ時間（秒）。この間、全サークルが同じ鼓動で明滅し徐々に濃くなる。")]
    public float BurstChargeDuration = 0.9f;

    [Tooltip("最内周リング→次のリングの爆発間隔（秒）。導火線のテンポの基準値。")]
    public float BurstInterval = 0.28f;

    [Range(0.2f, 2f)]
    [Tooltip("外のリングへ進むごとの間隔倍率。1未満で外周へ向かって加速する（ダッ、ダッ、ダダーン）。1 で等間隔。")]
    public float BurstIntervalDecay = 0.8f;

    [Tooltip("爆発1発のダメージ量。リズムの拍ごとに当たり得るので着地本体より軽めが目安。")]
    public int BurstDamage = 2;

    [Tooltip("ManaBurst の Player 被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int BurstDamageType = 0;

    [Tooltip("爆発ヒット時のヒットストップ・ノックバック等の設定。")]
    public DamageContext BurstEffects;

    [Tooltip("爆発予告サークルの外縁色。着地予告（赤）と区別できる紫系を推奨（チェシャ猫の魔力色）。")]
    public Color BurstTelegraphColor = new Color(0.6f, 0.35f, 1f, 1f);

    // 爆発の見た目はB1_EffectSettings.ManaBurst、SEはB1_SoundSettings.ManaBurstで設定する。

    [Tooltip("爆発1発ごとのカメラシェイク時間（秒）。0 で無効。")]
    public float BurstShakeDuration = 0.12f;

    [Tooltip("爆発1発ごとのカメラシェイク強さ。最終リングは下の倍率が乗る。")]
    public float BurstShakeIntensity = 0.08f;

    [Tooltip("爆発カメラシェイクの揺れ速度。")]
    public float BurstShakeFrequency = 26f;

    [Min(1f)]
    [Tooltip("最終リング（フィニッシュ）のシェイク倍率。揺れの強弱がリズムのアクセントになる。")]
    public float BurstFinisherShakeMultiplier = 2.2f;

    [Min(0f)]
    [Tooltip("爆発がプレイヤーに当たったときだけ入れる全体ヒットストップ（秒）。空振りの爆発は止めない。0 で無効。")]
    public float BurstHitStopDuration = 0.07f;

    [Tooltip("最終リングの爆発時だけ画面歪み（WavePassShockwave の設定を流用）を出す。全弾にかけると酔うのでフィニッシュ限定。")]
    public bool BurstFinisherDistort = true;

    [Header("マナバースト中の透明化（チェシャ猫の消失）")]
    [Tooltip("爆発チャージ開始と同時にボスを透明化し、爆発が走っている間は姿を消す。\nしっぽ立ちループの静止姿勢を隠しつつ「消えても魔力は襲ってくる」を見せる。Rush の透明化ブレンドを流用。")]
    public bool VanishDuringBurst = true;

    [Min(0.05f)]
    [Tooltip("透明化にかける時間（秒）。")]
    public float VanishFadeDuration = 0.35f;

    [Min(0.05f)]
    [Tooltip("再出現（TailAttackLand 開始時）にかける時間（秒）。")]
    public float ReappearFadeDuration = 0.3f;

    [Tooltip("着地後、次のアクションへ移行するまでの待機時間（秒）。着地モーションの見せ場として使う。0 で即移行。")]
    public float LandingDelay = 0.5f;

    [Tooltip("着地演出(LandingDelay)の後、さらに硬直（その場待機）する時間（秒）。「避けたら殴れる」反撃猶予を作る。0 で即次行動。")]
    public float RecoveryDuration = 0.4f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。0 = 制限なし。")]
    public float Cooldown = 8f;
}

[Serializable]
public class TailRainActionConfig
{
    [Header("出現条件")]
    [Tooltip("Phase2（激昂）でのみ発動する見せ場攻撃にする。\nON なら Phase1 では候補に入らない（フェーズ移行と演出が競合しない）。")]
    public bool Phase2Only = true;

    [Tooltip("テールレインが候補になる最低距離（m）。近すぎると候補から外れる。")]
    public float MinRange = 3f;

    [Tooltip("テールレインが候補になる最大距離（m）。遠すぎると候補から外れる。")]
    public float MaxRange = 22f;

    [Tooltip("このアクションを再び選択できるまでの最低待機時間（秒）。見せ場なので長め推奨。")]
    public float Cooldown = 18f;

    [Header("演出フロー")]
    [Tooltip("マップへ叩きつける回数。プレイヤーはこの回数ぶんの着弾を避け続ける。")]
    public int StrikeCount = 5;

    [Tooltip("見下ろしカメラへ切り替えてから、最初の叩きつけを始めるまでの溜め（秒）。")]
    public float IntroDelay = 0.8f;

    [Tooltip("着弾予告（地面リング）を表示してから跳び上がるまでの溜め（秒）。回避猶予として使う。")]
    public float TelegraphLead = 0.35f;

    [Tooltip("各叩きつけのジャンプにかかる時間（秒）。ジャンプ中も予告リングは表示され続ける。")]
    public float JumpDuration = 0.55f;

    [Tooltip("各ジャンプ弧の高さ（m）。大きいほど高く跳んで大きく叩きつける。")]
    public float JumpHeight = 6f;

    [Tooltip("着地（叩きつけ）後、次の予告を出すまでの溜め（秒）。")]
    public float RecoverDelay = 0.3f;

    [Tooltip("着弾間隔のランダム加算(秒)。RecoverDelay にこの範囲から抽選した値を足す。\n「読めそうで読めない」リズムを作る。(0,0) で固定間隔。")]
    public Vector2 RecoverDelayJitter = new Vector2(0f, 0.45f);

    [Tooltip("最後の叩きつけ後、通常カメラへ戻すまでの溜め（秒）。")]
    public float OutroDelay = 0.7f;

    [Header("着弾地点の狙い")]
    [Tooltip("初弾をプレイヤー直撃で狙う。ON なら1発目はプレイヤーの足元、以降は周辺を狙う。")]
    public bool FirstStrikeOnPlayer = true;

    [Tooltip("プレイヤー周辺のどの範囲を狙うか（半径 m）。大きいほど散らばり、小さいほど詰めてくる。")]
    public float TargetSpread = 4f;

    [Header("ダメージ（TailAttack とは独立に調整可能。ドーナッツ波を流用）")]
    [Tooltip("各叩きつけの範囲攻撃半径（m）。予告サークルの見た目サイズは TelegraphRadius で別途調整する。")]
    public float AoeRadius = 3.5f;

    [Tooltip("各叩きつけのダメージ量。衝撃波の波面が通過した瞬間に1回入る。")]
    public int Damage = 1;

    [Tooltip("Player の被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int DamageType = 1;

    [Tooltip("この高さ(m)以上ジャンプしていれば衝撃波が足元を通り抜けて回避できる。\n0 で高さ無視（必ず当たる）。プレイヤーのジャンプ到達高より少し低めに設定するとギリギリ回避になる。")]
    public float JumpDodgeHeight = 1.2f;

    [Tooltip("叩きつけダメージ時のヒットストップ・カメラシェイク設定。")]
    public DamageContext Effects;

    [Tooltip("波面が穴(WaveInnerRadius)から外周(AoeRadius)まで広がりきる時間（秒）。")]
    public float WaveDuration = 0.35f;

    [Tooltip("ドーナッツの穴の半径（m）。ここより内側にいる対象は波に当たらない安全地帯。")]
    public float WaveInnerRadius = 0.8f;

    [Tooltip("波の見た目リング(帯)の幅（m）。")]
    public float WaveThickness = 0.6f;

    [Tooltip("波リングの発光色。")]
    public Color WaveColor = new Color(1f, 0.3f, 0.45f, 1f);

    [Tooltip("円周上のトゲの深さ（帯幅に対する倍率）。0 で完全な真円の波面。")]
    public float WaveSpikeDepth = 0f;

    [Tooltip("波の大きさのランダム倍率範囲（min〜max から着弾ごとに抽選）。(1,1) で固定。\n半径・帯幅に加え、広がり速度が変わらないよう時間も同倍率で伸縮する。\n※リズムランダム (RecoverDelayJitter) を主に使う場合は (1,1) 推奨。")]
    public Vector2 WaveScaleRange = new Vector2(1f, 1f);

    [Tooltip("波リングに貼るマテリアル。未設定なら URP のパーティクル系シェーダで簡易表示。")]
    public Material WaveMaterial;

    [Header("着弾予告サークル")]
    [Tooltip("予告サークルの外縁色。中心は透明で、外周へ向かってこの色が濃くなる。")]
    public Color TelegraphColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("予告サークルの半径（m）。AoeRadius とは独立に、エフェクトの見た目サイズだけを調整する。")]
    public float TelegraphRadius = 3.5f;

    [Tooltip("何個先の着弾地点まで予告サークルを同時に事前表示するか。3 なら実行中ぶんを含め3地点。1 で直前のみ（従来どおり）。\n先の予告は薄く表示され、着弾が近づくにつれ赤が濃く・明滅が速くなる。")]
    public int TelegraphAheadCount = 3;

    [Range(0f, 1f)]
    [Tooltip("事前表示（着弾がまだ先）のときの濃さ。着弾直前は常に 1（最大の濃さ）なので、\nこの値が小さいほど事前表示と最終表示の差が大きくなる。0 でほぼ透明から始まり、1 で差なし。")]
    public float TelegraphFarAlpha = 0.25f;

    [Range(1f, 4f)]
    [Tooltip("着弾直前（最終表示）で予告サークルの明滅を何倍速にするか。1 で明滅速度は変化しない。")]
    public float TelegraphPulseBoost = 2f;

    [Tooltip("予告サークルのマテリアル。未設定なら PettanMaker/Boss_01/TelegraphCircle シェーダで自動生成。\n（Shader.Find だけの参照はビルドで剥がされることがあるため、ビルドで確実に出すなら割り当てる）")]
    public Material TelegraphMaterial;
}

[Serializable]
public class RushActionConfig
{
    [Tooltip("突進速度（m/s）。Walk.Speed・Run.Speed より十分大きい値を推奨（目安 15〜25）。\nAnimator の BlendTree で Rush アニメが切り替わる閾値（10）より大きくすること。")]
    public float Speed = 20f;

    [Tooltip("突進がプレイヤーを通り過ぎる距離（m）。0 だとプレイヤー位置でぴったり止まる。\n大きくするほど突き抜けるような演出になる。")]
    public float OvershootDistance = 5f;

    [Tooltip("突進ヒット時のダメージ量。")]
    public int Damage = 2;

    [Tooltip("Player の被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int DamageType = 1;

    [Tooltip("突進終了後に硬直（その場待機）する時間（秒）。空振り時の大きな反撃チャンスを作る。0 で即次行動。")]
    public float RecoveryDuration = 0.8f;

    [Tooltip("突進ヒット時のヒットストップ・カメラシェイク設定。")]
    public DamageContext Effects;

    [Header("周辺オブジェクトの跳ね上げ")]
    [Tooltip("Rush中にB1_AttackBounceObjectを反応させる、ボス中心からの半径（m）。0で無効。")]
    [Min(0f)] public float BounceRadius = 2.5f;

    [Header("突進予告ライン")]
    [Tooltip("タメ（RushCharge）中、突進経路に予告ライン（チェック柄・中心透明）を表示する。\nプレイヤーを狙い続けて向き・長さが追従する。")]
    public bool UseTelegraph = true;

    [Tooltip("予告ラインの横幅（m）。突進の当たり幅とは独立に、エフェクトの見た目サイズだけを調整する。")]
    public float TelegraphWidth = 2.5f;

    [Tooltip("予告ラインの基準色。横幅の中心は透明で、左右の縁へ向かってこの色が濃くなる。")]
    public Color TelegraphColor = new Color(1f, 0.12f, 0.08f, 1f);

    [Tooltip("予告ラインのマテリアル。未設定なら PettanMaker/Boss_01/TelegraphLine シェーダで自動生成。\n（Shader.Find だけの参照はビルドで剥がされることがあるため、ビルドで確実に出すなら割り当てる）")]
    public Material TelegraphMaterial;
}

[Serializable]
public class DownStateConfig
{
    [Min(0f)]
    [Tooltip("Down 静止モーション + 浮遊を寄りカメラ演出で見せる時間（秒）。")]
    public float ShowcaseDuration = 2f;

    [Min(0f)]
    [Tooltip("ダウン時に浮き上がる高さ（m）。")]
    public float RiseHeight = 1.2f;

    [Min(0.05f)]
    [Tooltip("浮き上がりにかける時間（秒）。X軸の傾きも同じ時間でイージング適用される。")]
    public float RiseDuration = 0.6f;

    [Range(-180f, 180f)]
    [Tooltip("Down になった瞬間に最初から付いている X軸回転（度）。突入フラッシュで隠れるため、傾いた状態からいきなり始まってよい。\nここから RiseDuration かけて TiltAngleX まで傾く。")]
    public float TiltStartAngleX = -60f;

    [Range(-180f, 180f)]
    [Tooltip("Down 中にボス本体へ加える X軸回転の最終角（度）。-90 で上（空）を向く。\nDown → Down_Idle のブレンド中に 0 へ戻る。")]
    public float TiltAngleX = -90f;

    [Min(0f)]
    [Tooltip("Down 中にボスのモデルだけを小刻みに震わせる揺れ幅（ローカル単位）。0 で無効。カメラや当たり判定は揺らさない。")]
    public float ShakeAmplitude = 0.06f;

    [Min(1f)]
    [Tooltip("Down 中のモデルシェイクの揺れ速度（Hz）。")]
    public float ShakeFrequency = 35f;

    [Min(0f)]
    [Tooltip("Down モーション中、白または赤を1色表示し続ける時間（秒）。この間隔ごとに白↔赤を切り替える。0 で点滅無効。")]
    [FormerlySerializedAs("MaterialFlashDuration")]
    public float MaterialFlashInterval = 0.125f;

    [Min(0f)]
    [Tooltip("白・赤点滅を再生し続ける合計時間（秒）。経過するとDownモーション中でも元のモデル色へ戻る。0 で点滅無効。")]
    public float MaterialFlashTotalDuration = 2f;

    [Range(0f, 1f)]
    [Tooltip("白・赤点滅を元のモデル色へ重ねる強さ。1 で完全に点滅色になる。")]
    public float MaterialFlashAmount = 0.85f;

    [ColorUsage(true, true)]
    [Tooltip("Down モーション中の白側の点滅色。")]
    public Color MaterialFlashWhite = Color.white;

    [ColorUsage(true, true)]
    [Tooltip("Down モーション中の赤側の点滅色。")]
    public Color MaterialFlashRed = Color.red;

    [Min(0.05f)]
    [Tooltip("Down → Down_Idle へのアニメーションブレンド時間（秒）。\nこの間に浮遊（Y上昇）をイージングで戻して着地する。")]
    public float IdleBlendDuration = 0.5f;

    [Min(0f)]
    [Tooltip("Down_Idle で無防備になる時間（秒）。この間プレイヤーは自由に攻撃でき、経過すると Phase2 移行演出へ進む。")]
    public float Duration = 6f;

    [Min(0f)]
    [Tooltip("Down VFX が Phase2 カメラ開始直前までに自然消滅するための余裕（秒）。パーティクル寿命とリセット演出尺から停止タイミングを自動計算し、この分だけ早めに放出を止める。")]
    public float VFXCleanupMargin = 0.15f;
}

[Serializable]
public class CriticalHitConfig
{
    [Tooltip("クリティカルヒットを有効にする。OFF で従来どおり通常ダメージのみ。")]
    public bool Enabled = true;

    [Range(0f, 1f)]
    [Tooltip("ボスへの1ヒットごとにクリティカルが発生する確率。0.1 = 10%。")]
    public float Chance = 0.1f;

    [Min(1f)]
    [Tooltip("クリティカル時のダメージ倍率。2 なら通常の2倍（端数は切り上げ）。")]
    public float DamageMultiplier = 2f;

    [Header("スナップズーム（命中瞬間に画角を絞る）")]
    [Min(0f)]
    [Tooltip("クリティカル時に狭めるカメラFOV（度）。0 で無効。3〜5程度で強いパンチ感が出る。")]
    public float SnapZoomFov = 4f;

    [Min(0f)]
    [Tooltip("画角を狭め切るまでの時間（秒・実時間）。")]
    public float SnapZoomInDuration = 0.035f;

    [Min(0f)]
    [Tooltip("狭めた画角を保持する時間（秒・実時間）。")]
    public float SnapZoomHoldDuration = 0.07f;

    [Min(0f)]
    [Tooltip("通常の画角へ戻る時間（秒・実時間）。寄りより長くすると余韻が出る。")]
    public float SnapZoomOutDuration = 0.3f;

    [Header("ヒットストップ（通常被弾より強く止める）")]
    [Min(0f)]
    [Tooltip("クリティカル時にボスの Animator を止める時間（秒・実時間）。通常被弾（HitStopDuration）より長くする。0 で無効。")]
    public float BossHitStopDuration = 0.3f;

    [Min(0f)]
    [Tooltip("クリティカル時のグローバルヒットストップ時間（秒）。timeScale を落として全画面を止める。0 で無効。")]
    public float GlobalHitStopDuration = 0.3f;

    [Range(0.01f, 0.5f)]
    [Tooltip("グローバルヒットストップ中の timeScale。小さいほど強く止まる。")]
    public float GlobalHitStopScale = 0.02f;

    [Header("カメラシェイク")]
    [Min(0f)]
    [Tooltip("クリティカル時のカメラシェイク時間（秒）。0 で無効。")]
    public float ShakeDuration = 0.3f;

    [Min(0f)]
    [Tooltip("クリティカル時のカメラシェイク強さ。通常ヒット（0.30）より強めを推奨。")]
    public float ShakeIntensity = 0.5f;

    [Min(1f)]
    [Tooltip("クリティカル時のカメラシェイク周波数（Hz）。")]
    public float ShakeFrequency = 28f;

    [Header("ボスマテリアル点滅")]
    [Min(0f)]
    [Tooltip("点滅1回あたりの点灯/消灯時間（秒・実時間）。0 で点滅無効。")]
    public float BlinkInterval = 0.06f;

    [Min(1)]
    [Tooltip("点滅回数。点灯→消灯で1回と数える。")]
    public int BlinkCount = 3;

    [Range(0f, 1f)]
    [Tooltip("点滅色を元のモデル色へ重ねる強さ。1 で完全に点滅色になる。")]
    public float BlinkAmount = 0.9f;

    [ColorUsage(true, true)]
    [Tooltip("クリティカル点滅の色。HDR で 1 以上にすると発光して見える。")]
    public Color BlinkColor = new Color(4f, 3.2f, 1.2f, 1f);
}
