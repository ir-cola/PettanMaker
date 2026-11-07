using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// テールアタック着地後に発生する魔力地面爆発「マナバーストクロス」。
///
/// チェシャ猫の魔力(TailChargeと同じ紫)が地面を伝い、B1_TelegraphCircle を
/// パターンに並べて全円が同じ鼓動で明滅しながらチャージ
/// → 中心に近いグループから外へ向かって導火線のように順番に地面が爆ぜる。
///   ・Cross    : 前後左右の十字。1リング(腕4個ぶん)=1拍。斜め45°が安全地帯
///   ・Spiral   : 渦巻き尻尾の意匠。1円=1拍で「グルグルグルッ」と回りながら外へ走る。
///                プレイヤーは渦と逆回りに歩くと安全
///   ・Ring     : 同心円の波紋。1リング全周=1拍。リング間の隙間(距離)で避ける
///   ・Pinwheel : 風車。十字の腕が外へ進むごとに曲がり、安全地帯が回って見える
///   ・Scatter  : ランダム散布。方角は毎回変わるが、拍は内の帯→外の帯へ進む
/// 外周ほど間隔が詰まり(加速)、最後のグループだけ強シェイク+画面歪みのフィニッシュ。
/// 本体はこの間、透明化(チェシャ猫の消失)と組み合わせるのを想定している
/// (透明化自体は B1_TailAttackAction 側が既存の Rush 用ブレンドで行う)。
///
/// 爆発の見た目は BurstVFXPrefab(Kenney素材のParticleSystem等)を最優先、
/// 未設定なら fallbackBurst デリゲート(通常 boss.PlayExplosionVFX)、
/// それも無ければフラッシュ光のみで動く。着弾地点には傷跡(ScarTexture)が残る。
///
/// B1_DamageWave と同じ Spawn() 一発の自己完結・自己破棄パターン。
/// 途中で行動が割り込まれた場合、まだ爆発が始まっていなければ CancelIfCharging()
/// で予告ごと消せる(爆発開始後は最後まで自走する)。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_ManaBurstCross : MonoBehaviour
{
    /// <summary>爆発の並べ方。</summary>
    public enum Pattern
    {
        Cross,    // 十字。1リング(全腕ぶん)を同時に起爆
        Spiral,   // 渦巻き。1円ずつ回りながら外へ起爆
        Ring,     // 同心円。全周リングが内から外へ波紋のように起爆
        Pinwheel, // 風車。十字の腕が外へ進むごとに曲がり、安全地帯が回って見える
        Scatter,  // 散布。ランダムな方角へばら撒くが、拍は内の帯から外の帯へ進む
    }

    /// <summary>爆発1セットぶんのパラメータ。</summary>
    public struct Params
    {
        // --- 配置 ---
        public Pattern pattern;       // 並べ方(十字/螺旋)
        public int   armCount;        // [Cross] 腕の本数(4で十字)
        public float armAngleOffset;  // 全体の回転オフセット(度)。Crossなら45で×字、Spiralなら開始角
        public int   circlesPerArm;   // [Cross] 腕1本あたりの円の数
        public float startDistance;   // 中心から最初の円までの距離(m)
        public float circleSpacing;   // 円と円の間隔(m)。Spiralでは1周あたりの外への広がり幅
        public float circleRadius;    // 最内周の円の半径(m)
        public float radiusGrowth;    // 外へ進むごとに足す半径(m)。外周ほど大きい円に
        public int   spiralCircleCount; // [Spiral] 円の総数(=拍数)
        public float spiralTurns;       // [Spiral] 渦の周回数

        // --- タイミング ---
        public float chargeDuration;  // 予告表示から初弾までのチャージ時間(秒)
        public float burstInterval;   // リング0→1の間隔(秒)
        public float intervalDecay;   // 次リング間隔への倍率(1未満で外周へ加速)

        // --- ダメージ ---
        public int           damage;
        public DamageContext effects;

        // --- 予告サークル ---
        public Color    telegraphColor;
        public Material telegraphMaterial;
        public float    telegraphFarAlpha;
        public float    telegraphPulseBoost;

        // --- 爆発の見た目 ---
        public GameObject burstVFXPrefab;  // 爆発エフェクト。未設定なら fallbackBurst に委ねる
        public System.Action<Vector3> fallbackBurst; // プレハブ未設定時の爆発(通常 boss.PlayExplosionVFX)
        public Color      burstColor;      // プレハブ経路でのフラッシュ光の色
        public Material   scarMaterial;    // 爆発跡マテリアル(GroundScarシェーダ推奨)。null で跡なし
        public float      scarDuration;    // 爆発跡が消えるまでの時間(秒)

        // --- 落雷＋空間の裂け目(既存演出に加算。着弾ごとに空から雷→雷の形の裂け目が残る) ---
        public bool     manaBolt;           // 落雷を出すか
        public Material boltMaterial;       // 雷本体(発光/Kenney)。null で簡易フォールバック
        public Material riftMaterial;       // 着弾後に残る裂け目(屈折/歪み)。null で簡易フォールバック
        public Color    boltColor;          // 雷・裂け目の色(紫の魔力推奨)
        public float    boltHeight;         // 上空からの落下高さ(m)
        public float    boltWidth;          // 雷の太さ(m)
        public float    boltStrikeDuration; // 閃光が残る時間(秒)
        public float    riftDuration;       // 裂け目が残る時間(秒)
        public float    finisherBoltScale;  // フィニッシュの雷を何倍太く・高くするか
        public float    boltImpactYOffset;  // 雷の着弾点の高さ補正(m)。負で下げる(爆発中心が地面より高いとき地面へ落とす)

        // --- 着弾スパーク球(半透明球＋球面法線方向へ飛ぶSpark。着弾点で弾ける) ---
        public bool                    impactSparks;  // 着弾スパーク球を出すか
        public B1_ManaBoltImpact.Params impactParams; // 見た目パラメータ。radius は「AoE半径に対する倍率」を入れておく

        // --- 手応え ---
        public float strikeShakeDuration;
        public float strikeShakeIntensity;
        public float strikeShakeFrequency;
        public float finisherShakeMultiplier; // 最終リングのシェイク倍率
        public float hitStopDuration;         // プレイヤーに当たったときだけ入れる全体ヒットストップ(秒)。0で無効
        public Transform finisherDistortTarget; // 最終リング爆発時に画面歪みを出す対象(通常プレイヤー)。null で無効
        public B1_ShockwaveDistortion.Params finisherDistortParams;
        public Vector3   finisherDistortOffset;

        // --- 音 ---
        // B1_SoundManagerへ再生を戻し、クリップ・音量・ピッチをB1_SoundSettingsで一元管理する。
        public System.Action<Vector3, int, bool> playBurstSound;
    }

    private Params    p;
    private Vector3   center;
    private float     yawDeg;    // 十字全体の向き(ボスの向きに合わせる)
    private IHealth   ignore;    // ボス自身(自傷防止)
    private Component source;    // ヒットストップ等の発生源

    private float elapsed;
    private int   nextGroup;     // 次に爆ぜるグループ番号
    private float nextBurstAt;   // 次の爆発時刻(elapsed基準)
    private bool  burstsBegun;

    // 起爆グループごとの円。Cross=1リング(全腕ぶん)が1グループ、Spiral=1円が1グループ。
    // 同じグループの円は同時に爆ぜる=1拍。
    private B1_TelegraphCircle[][] telegraphs;
    private Vector3[][]            positions;
    private float[][]              radii;

    private const float MinBurstInterval = 0.03f; // 加速しきったときの最短拍(秒)

    private static readonly Collider[] _hits = new Collider[64];

    private int GroupCount => telegraphs?.Length ?? 0;

    /// <summary>全グループが爆ぜ終わるまでの時間(秒)。透明化の再出現タイミング合わせに使える。</summary>
    public float TotalDuration
    {
        get
        {
            float t = p.chargeDuration;
            for (int i = 0; i < GroupCount - 1; i++)
                t += GroupInterval(i);
            return t;
        }
    }

    // グループ g → g+1 の間隔。外へ進むごとに詰まる(導火線の加速)。
    private float GroupInterval(int g)
        => Mathf.Max(MinBurstInterval, p.burstInterval * Mathf.Pow(p.intervalDecay, g));

    /// <summary>指定位置に十字の魔力地面爆発を1セット発生させる。yawDeg で十字の向きを指定。</summary>
    public static B1_ManaBurstCross Spawn(Vector3 center, float yawDeg, in Params p, IHealth ignore, Component source)
    {
        // 注意: HideFlags.DontSave を付けるとPlayモード終了時のシーン破棄から除外されて
        // Editモードに残骸が残るため、ランタイム生成の演出GOには付けない。
        var go = new GameObject("B1_ManaBurstCross");
        go.transform.position = center;
        var self = go.AddComponent<B1_ManaBurstCross>();
        self.Init(center, yawDeg, p, ignore, source);
        return self;
    }

    private void Init(Vector3 c, float yaw, in Params src, IHealth ig, Component so)
    {
        center = c;
        yawDeg = yaw;
        p      = src;
        ignore = ig;
        source = so;

        p.armCount       = Mathf.Max(1, p.armCount);
        p.circlesPerArm  = Mathf.Max(1, p.circlesPerArm);
        p.circleSpacing  = Mathf.Max(0.1f, p.circleSpacing);
        p.circleRadius   = Mathf.Max(0.1f, p.circleRadius);
        p.chargeDuration = Mathf.Max(0.05f, p.chargeDuration);
        p.burstInterval  = Mathf.Max(0.02f, p.burstInterval);
        p.intervalDecay  = Mathf.Clamp(p.intervalDecay, 0.2f, 2f);

        p.spiralCircleCount = Mathf.Max(2, p.spiralCircleCount);
        p.spiralTurns       = Mathf.Max(0.25f, p.spiralTurns);

        elapsed     = 0f;
        nextGroup   = 0;
        nextBurstAt = p.chargeDuration;
        burstsBegun = false;

        switch (p.pattern)
        {
            case Pattern.Spiral:   BuildSpiral();               break;
            case Pattern.Ring:     BuildRing();                 break;
            case Pattern.Pinwheel: BuildArms(PinwheelBendDeg);  break;
            case Pattern.Scatter:  BuildScatter();              break;
            default:               BuildArms(0f);               break;
        }
    }

    // 風車で1リング外へ進むごとに腕が曲がる角度(度)。腕の間の安全地帯が回って見える量。
    private const float PinwheelBendDeg = 30f;

    // 腕構築の共通処理：円をリング(中心からの距離段階)×腕で並べる。1リング=1グループ=1拍。
    // bendPerRingDeg=0 で十字(Cross)、正の値で外へ進むごとに同方向へ曲がる風車(Pinwheel)。
    private void BuildArms(float bendPerRingDeg)
    {
        telegraphs = new B1_TelegraphCircle[p.circlesPerArm][];
        positions  = new Vector3[p.circlesPerArm][];
        radii      = new float[p.circlesPerArm][];

        for (int ring = 0; ring < p.circlesPerArm; ring++)
        {
            telegraphs[ring] = new B1_TelegraphCircle[p.armCount];
            positions[ring]  = new Vector3[p.armCount];
            radii[ring]      = new float[p.armCount];

            float dist = p.startDistance + ring * p.circleSpacing;
            // 外周ほど大きい円 =「外へ広がる魔力」の視覚 + 遠い円ほど回避猶予が長い公平さ。
            float radius = p.circleRadius + ring * Mathf.Max(0f, p.radiusGrowth);

            for (int arm = 0; arm < p.armCount; arm++)
            {
                float angle = yawDeg + p.armAngleOffset + arm * (360f / p.armCount)
                            + ring * bendPerRingDeg;
                PlaceCircle(ring, arm, angle, dist, radius);
            }
        }
    }

    // 同心円：全周リングが内から外へ波紋のように爆ぜる。1リング=1グループ=1拍。
    // 方角の安全地帯が無いぶん円同士の隙間は保ち、リング間の隙間へ入れば全弾回避できる
    // (十字が「角度」を問うのに対し、こちらは「中心からの距離」の位置取りを問う)。
    private void BuildRing()
    {
        telegraphs = new B1_TelegraphCircle[p.circlesPerArm][];
        positions  = new Vector3[p.circlesPerArm][];
        radii      = new float[p.circlesPerArm][];

        for (int ring = 0; ring < p.circlesPerArm; ring++)
        {
            float dist   = p.startDistance + ring * p.circleSpacing;
            float radius = p.circleRadius + ring * Mathf.Max(0f, p.radiusGrowth);

            // 外周ほど円周が長いので個数を増やして密度を保つ。ただし直径3個ぶんの
            // 隙間を残して完全な壁にはしない(すり抜け不能は理不尽)。
            int count = Mathf.Max(p.armCount,
                Mathf.RoundToInt(2f * Mathf.PI * dist / (radius * 3f)));

            telegraphs[ring] = new B1_TelegraphCircle[count];
            positions[ring]  = new Vector3[count];
            radii[ring]      = new float[count];

            // リングごとに半歩ずらす千鳥配置。隙間が一直線に並んで棒立ち安置にならないように。
            float stagger = ring * (180f / count);
            for (int i = 0; i < count; i++)
            {
                float angle = yawDeg + p.armAngleOffset + stagger + i * (360f / count);
                PlaceCircle(ring, i, angle, dist, radius);
            }
        }
    }

    // 散布：チェシャ猫の気まぐれ。円をランダムな方角へばら撒くが、拍(グループ)は
    // 内の距離帯から外の帯へ進むので「中心から外へ走る導火線」の読みは保たれる。
    // 完全ランダムだと1箇所に固まって理不尽になるため、方角は円周を等分した
    // セクターへ1個ずつ+ジッターで散らす。
    private void BuildScatter()
    {
        telegraphs = new B1_TelegraphCircle[p.circlesPerArm][];
        positions  = new Vector3[p.circlesPerArm][];
        radii      = new float[p.circlesPerArm][];

        for (int g = 0; g < p.circlesPerArm; g++)
        {
            telegraphs[g] = new B1_TelegraphCircle[p.armCount];
            positions[g]  = new Vector3[p.armCount];
            radii[g]      = new float[p.armCount];

            float baseDist = p.startDistance + g * p.circleSpacing;
            float radius   = p.circleRadius + g * Mathf.Max(0f, p.radiusGrowth);

            float sector    = 360f / p.armCount;
            float baseAngle = Random.Range(0f, 360f);
            for (int i = 0; i < p.armCount; i++)
            {
                float angle = baseAngle + i * sector
                            + Random.Range(-sector * 0.35f, sector * 0.35f);
                float dist  = baseDist
                            + Random.Range(-p.circleSpacing * 0.3f, p.circleSpacing * 0.3f);
                PlaceCircle(g, i, angle, dist, radius);
            }
        }
    }

    // 螺旋：チェシャ猫の渦巻き尻尾。1円=1グループ=1拍で、回りながら外へ走る。
    // アルキメデス螺旋(1周ごとに circleSpacing ぶん外へ)に等間隔で円を置く。
    private void BuildSpiral()
    {
        int count = p.spiralCircleCount;
        telegraphs = new B1_TelegraphCircle[count][];
        positions  = new Vector3[count][];
        radii      = new float[count][];

        for (int i = 0; i < count; i++)
        {
            telegraphs[i] = new B1_TelegraphCircle[1];
            positions[i]  = new Vector3[1];
            radii[i]      = new float[1];

            float t     = i / (float)(count - 1); // 0=中心, 1=終端
            float angle = yawDeg + p.armAngleOffset + t * p.spiralTurns * 360f;
            float dist  = p.startDistance + t * p.spiralTurns * p.circleSpacing;
            // 螺旋は円が多いぶん1個ずつの成長を抑え、終端で十字の最外周と同程度の大きさにする。
            float radius = p.circleRadius + t * Mathf.Max(0f, p.radiusGrowth) * 2f;

            PlaceCircle(i, 0, angle, dist, radius);
        }
    }

    private void PlaceCircle(int group, int index, float angleDeg, float dist, float radius)
    {
        Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
        Vector3 pos = center + dir * dist;

        positions[group][index] = pos;
        radii[group][index]     = radius;
        var tele = B1_TelegraphCircle.Spawn(pos, radius, p.telegraphColor, p.telegraphMaterial);
        tele.SetUrgencyStyle(p.telegraphFarAlpha, p.telegraphPulseBoost);
        tele.SetUrgency(0f);
        telegraphs[group][index] = tele;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        // チャージ中は全円が同じ鼓動で濃くなっていく(十字全体がひとつの心臓)。
        if (!burstsBegun)
        {
            float urgency = Mathf.Clamp01(elapsed / p.chargeDuration);
            for (int ring = 0; ring < telegraphs.Length; ring++)
                for (int arm = 0; arm < telegraphs[ring].Length; arm++)
                    if (telegraphs[ring][arm] != null)
                        telegraphs[ring][arm].SetUrgency(urgency);
        }

        // 導火線起爆：時間が来たグループから外へ順に爆ぜる。
        // 低フレームレートでも取りこぼさないよう while で追いつく。
        while (nextGroup < GroupCount && elapsed >= nextBurstAt)
        {
            BurstGroup(nextGroup);
            nextBurstAt += GroupInterval(nextGroup);
            nextGroup++;
        }

        if (nextGroup >= GroupCount)
            Destroy(gameObject); // 爆発VFX・傷跡は各自が自己破棄するので待たなくてよい
    }

    /// <summary>まだ爆発が始まっていなければ予告ごと消して自壊する(行動割り込み用)。</summary>
    public void CancelIfCharging()
    {
        if (burstsBegun) return; // 爆発開始後は最後まで自走させる
        for (int ring = 0; ring < telegraphs.Length; ring++)
            for (int arm = 0; arm < telegraphs[ring].Length; arm++)
                telegraphs[ring][arm]?.Dismiss();
        Destroy(gameObject);
    }

    private void BurstGroup(int group)
    {
        burstsBegun = true;
        bool isFinisher = group == GroupCount - 1;

        // 同一グループ内(同じ拍)で同じ対象へ多重ヒットさせない。グループが違えば別の拍として当たる。
        var damaged = new HashSet<IHealth>();
        bool hitSomeone = false;

        for (int i = 0; i < telegraphs[group].Length; i++)
        {
            Vector3 pos    = positions[group][i];
            float   radius = radii[group][i];

            var tele = telegraphs[group][i];
            if (tele != null)
            {
                tele.Detonate();
                telegraphs[group][i] = null;
            }

            SpawnBurstVisual(pos, radius);
            if (p.scarMaterial != null)
                B1_GroundScarDecal.Spawn(pos, radius, p.scarMaterial, p.scarDuration);

            // 爆発範囲のバウンスオブジェクトも打ち上げる(TailAttack/TailRainの着地と同じ挙動)。
            // 環境が拍に合わせて跳ねることで、導火線のリズムが地形にも伝わる。
            B1_AttackBounceObject.LaunchInRadius(pos, radius);

            // 既存の地面爆発はそのままに、着弾点へ上空から雷を落とす(縦の一撃)。
            // 閃光のあと雷と同じ形の歪んだ裂け目が一定時間残る。フィニッシュだけ極太。
            // 太さ・球の大きさは AoE(この爆発の半径)に比例させ、大きい爆発ほど大きく見せる。
            SpawnManaBoltFX(pos, radius, isFinisher);

            hitSomeone |= ApplyBurstDamage(pos, radius, damaged);
        }

        // カメラの拍：各リングは小さく、フィニッシュだけ強く。揺れの強弱が曲のアクセント。
        float shakeMul = isFinisher ? Mathf.Max(1f, p.finisherShakeMultiplier) : 1f;
        if (p.strikeShakeDuration > 0f)
            CameraShake.PlayImpulse(center, p.strikeShakeDuration, p.strikeShakeIntensity * shakeMul, p.strikeShakeFrequency);

        // 当たった爆発だけ短く止める。空振りの爆発は止めない(テンポを殺さない)。
        if (hitSomeone && p.hitStopDuration > 0f)
            GlobalHitStop.Play(p.hitStopDuration);

        // フィニッシュのみ画面歪み(全弾にかけると酔うので1発限定)。
        if (isFinisher && p.finisherDistortTarget != null)
        {
            Vector3 spawnPos = p.finisherDistortTarget.position
                + p.finisherDistortTarget.TransformVector(p.finisherDistortOffset);
            B1_ShockwaveDistortion.Spawn(spawnPos, p.finisherDistortParams);
        }

        p.playBurstSound?.Invoke(center, group, isFinisher);
    }

    // 爆発1発ぶんの円範囲ダメージ。水平距離で判定(高さは無視して地面サークルとして扱う)。
    private bool ApplyBurstDamage(Vector3 pos, float radius, HashSet<IHealth> damaged)
    {
        bool hit = false;
        int n = Physics.OverlapSphereNonAlloc(
            pos, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);

        for (int i = 0; i < n; i++)
        {
            IHealth health = _hits[i].GetComponent<IHealth>() ?? _hits[i].GetComponentInParent<IHealth>();
            if (health == null || health.IsDead) continue;
            if (ReferenceEquals(health, ignore)) continue;
            if (!damaged.Add(health)) continue;

            Vector3 tp = (health as MonoBehaviour) != null
                ? ((MonoBehaviour)health).transform.position
                : _hits[i].transform.position;
            Vector3 flat = tp - pos;
            flat.y = 0f;
            if (flat.sqrMagnitude > radius * radius) continue;

            CombatSystem.Instance.Damage(health, p.damage, p.effects, source);
            hit = true;
        }
        return hit;
    }

    // 爆発の見た目。プレハブ > フォールバック(PlayExplosionVFX等) > フラッシュ光のみ、の順で解決。
    // フォールバック経路は各自がフラッシュ光を持つため、こちらでは追加しない。
    private void SpawnBurstVisual(Vector3 pos, float radius)
    {
        if (p.burstVFXPrefab != null)
        {
            // プレハブ側に自己破棄が無くても残骸にならないよう保険で消す。
            GameObject vfx = Instantiate(p.burstVFXPrefab, pos, Quaternion.identity);
            Destroy(vfx, 4f);
            B1_ImpactLight.Spawn(pos, p.burstColor, 7f, radius * 6f, 0.25f);
            return;
        }
        if (p.fallbackBurst != null)
        {
            p.fallbackBurst(pos);
            return;
        }
        B1_ImpactLight.Spawn(pos, p.burstColor, 7f, radius * 6f, 0.25f);
    }

    // 着弾点へ上空から雷を落とし、着弾点でスパーク球を弾けさせる。既存演出への純追加。
    // radius はこの爆発のAoE半径。太さ・球の大きさをこれに比例させ、大きい爆発ほど大きく見せる。
    private void SpawnManaBoltFX(Vector3 pos, float radius, bool isFinisher)
    {
        if (!p.manaBolt && !p.impactSparks) return;

        // 爆発中心が地面より高い(AoeOffset.y)ぶんを雷専用オフセットで補正して着弾点を下げる。
        pos.y += p.boltImpactYOffset;
        float scale = isFinisher ? Mathf.Max(1f, p.finisherBoltScale) : 1f;

        if (p.manaBolt)
        {
            var bp = new B1_ManaBolt.Params
            {
                boltMaterial   = p.boltMaterial,
                riftMaterial   = p.riftMaterial,
                color          = p.boltColor,
                // boltWidth は「AoE半径に対する太さ倍率」。半径に比例して太くする。
                width          = p.boltWidth * radius,
                // 落下高さも半径で軽く底上げして、太い雷が寸詰まりに見えないようにする。
                height         = p.boltHeight + radius,
                strikeDuration = p.boltStrikeDuration,
                riftDuration   = p.riftDuration,
            };
            B1_ManaBolt.Spawn(pos, bp, scale);
        }

        if (p.impactSparks)
        {
            var ip = p.impactParams;
            // impactParams.radius は「AoE半径に対する倍率」。半径に比例した球にする。
            ip.radius = Mathf.Max(0.01f, ip.radius) * radius;
            B1_ManaBoltImpact.Spawn(pos, ip, scale);
        }
    }

    private void OnDestroy()
    {
        // 中断や親シーン破棄で残った予告を掃除(通常は BurstRing / Cancel で消えている)。
        if (telegraphs == null) return;
        for (int ring = 0; ring < telegraphs.Length; ring++)
            for (int arm = 0; arm < telegraphs[ring].Length; arm++)
                telegraphs[ring][arm]?.Dismiss();
    }
}

/// <summary>
/// 爆発跡のデカール。地面に寝かせたクアッドへ専用マテリアル
/// (PettanMaker/Boss_01/GroundScar 推奨)を貼り、寿命に沿って
///   ・_Cool 0→1 : 残り火(魔力の紫)が冷えて消える
///   ・_Fade 1→0 : 寿命の後半で跡そのものが消える
/// を流して自壊する。専用プロパティを持たないマテリアルでも、色フェードに
/// フォールバックして動く(_BaseColor/_Color のアルファを落とす)。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_GroundScarDecal : MonoBehaviour
{
    private const float GroundOffsetY = 0.03f; // Zファイト防止の浮かせ量

    private static readonly int CoolId      = Shader.PropertyToID("_Cool");
    private static readonly int FadeId      = Shader.PropertyToID("_Fade");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    private Material ownedMaterial; // 元マテリアルを汚さないためのインスタンス
    private float    duration;
    private float    elapsed;
    private bool     hasCool;
    private bool     hasFade;
    private Color    fallbackBaseColor;
    private int      fallbackColorId;

    public static void Spawn(Vector3 pos, float radius, Material sourceMaterial, float duration)
    {
        if (sourceMaterial == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "B1_GroundScarDecal"; // DontSave は付けない(Playモード終了時に残骸が残る)
        Destroy(go.GetComponent<Collider>()); // 見た目だけ。判定は持たない
        go.transform.position = pos + Vector3.up * GroundOffsetY;
        go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f); // 地面に寝かせ、向きはランダムで跡らしく
        go.transform.localScale = Vector3.one * (radius * 2f);
        var self = go.AddComponent<B1_GroundScarDecal>();
        self.Init(sourceMaterial, duration);
    }

    private void Init(Material source, float dur)
    {
        duration = Mathf.Max(0.1f, dur);

        ownedMaterial = new Material(source); // 共有アセットを直接いじらない
        hasCool = ownedMaterial.HasProperty(CoolId);
        hasFade = ownedMaterial.HasProperty(FadeId);

        // 専用シェーダでないマテリアル用のフォールバック(色アルファで消す)。
        fallbackColorId = ownedMaterial.HasProperty(BaseColorId) ? BaseColorId
                        : ownedMaterial.HasProperty(ColorId)     ? ColorId
                        : 0;
        if (!hasFade && fallbackColorId != 0)
            fallbackBaseColor = ownedMaterial.GetColor(fallbackColorId);

        if (hasCool) ownedMaterial.SetFloat(CoolId, 0f); // 出現直後は灼熱
        if (hasFade) ownedMaterial.SetFloat(FadeId, 1f);

        var mr = GetComponent<MeshRenderer>();
        mr.material = ownedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        // 残り火は寿命の前半 60% で冷え切る(消える瞬間まで光っていると安っぽいので先に鎮火)。
        if (hasCool)
            ownedMaterial.SetFloat(CoolId, Mathf.Clamp01(t / 0.6f));

        // 後半だけフェード(前半はくっきり残して「跡」を見せる)。
        float fade = 1f - Mathf.Clamp01((t - 0.5f) * 2f);
        if (hasFade)
        {
            ownedMaterial.SetFloat(FadeId, fade);
        }
        else if (fallbackColorId != 0)
        {
            Color c = fallbackBaseColor;
            c.a = fallbackBaseColor.a * fade;
            ownedMaterial.SetColor(fallbackColorId, c);
        }

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }
}
