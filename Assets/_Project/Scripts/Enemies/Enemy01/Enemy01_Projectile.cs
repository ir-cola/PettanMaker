using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy01 の投擲物。生成後は「バッグ地点で保持 → 耳地点へ運搬 → その位置から放物線投擲」。
/// 運搬の進捗(0→1)と投擲タイミングは Attack アニメのフレーム進捗に合わせて
/// 外部（Enemy01_ThrowAction）から <see cref="UpdateCarry"/> / <see cref="Release"/> で駆動する。
/// 万一駆動されないまま放置された場合（投擲者の消滅など）は保険で自動投擲する。
/// 運搬中は物理・当たり判定・トレイルを止め、投擲の瞬間に武装する。
/// </summary>
public class Enemy01_Projectile : MonoBehaviour
{
    /// <summary>投擲の入力一式。Enemy01 から渡す。</summary>
    public struct ThrowConfig
    {
        public Transform bagPoint;       // 生成/保持地点（バッグ）
        public Transform earPoint;       // 運搬先＝投擲開始地点（耳）
        public Transform target;         // 狙う相手（プレイヤー）
        public float aimHeightOffset;    // 狙う高さ補正
        public float launchAngle;        // 放物線の打ち上げ角度(度)
        public float fallbackSpeed;      // 弾道計算できない場合の直線初速
        public float spinDegPerSec;      // 飛行中の角速度(度/秒)
        public int damage;
        public int damageType;
        public Collider[] ignoreColliders;  // 投擲者など無視するコライダー
        public float safetyReleaseTime;  // 駆動されない場合に自動投擲するまでの秒数(保険)
        public Transform ownerTransform; // 弧の外側方向を決める基準（投擲者本体）
        public float carryArcHeight;     // 運搬の弧の高さ（上方向）
        public float carryArcOutward;    // 運搬の弧の外側方向への膨らみ
        public AudioClip throwSE;        // 投擲時SE
        public float throwSEVolume;
        public AudioClip hitSE;          // Player命中時SE
        public float hitSEVolume;
    }

    [Header("着地バウンド")]
    [Tooltip("地面で跳ねる最大回数。これを超えたらディゾルブして消える。")]
    [SerializeField] private int maxGroundBounces = 3;
    [Tooltip("バウンドごとの上方向の速度減衰（0〜1）。小さいほどすぐ跳ねなくなる。")]
    [SerializeField, Range(0f, 1f)] private float bounceUpDamping = 0.55f;
    [Tooltip("バウンドごとの水平速度減衰（0〜1）。")]
    [SerializeField, Range(0f, 1f)] private float bounceHorizontalDamping = 0.7f;
    [Tooltip("この上向き速度を下回ったら跳ねずにディゾルブへ移行する。")]
    [SerializeField] private float minBounceUpSpeed = 1.5f;

    [Header("ディゾルブ消滅")]
    [Tooltip("消えるまでの時間（秒）。")]
    [SerializeField] private float dissolveDuration = 0.5f;
    [Tooltip("空なら Shader.Find(\"Enemy/ProjectileDissolve\") を使う。")]
    [SerializeField] private Shader dissolveShader;
    [SerializeField, ColorUsage(true, true)] private Color dissolveEdgeColor = new Color(1f, 0.55f, 0.15f, 1f);
    [SerializeField] private float dissolveNoiseScale = 9f;
    [SerializeField, Range(0.01f, 0.4f)] private float dissolveEdgeWidth = 0.14f;
    [Tooltip("投擲後この秒数で強制的にディゾルブ（迷子防止）。0で無効。")]
    [SerializeField] private float maxAirLifetime = 8f;

    [Header("VFX")]
    [Tooltip("VFX_Enemy01_ProjectileTrail をアタッチする。")]
    [SerializeField] private GameObject projectileTrailVFXPrefab;
    [Tooltip("軌跡VFXの追従位置。未設定なら投擲物本体。")]
    [SerializeField] private Transform projectileTrailVFXTargetPoint;
    [Tooltip("VFX_Enemy01_ProjectileHit をアタッチする。")]
    [SerializeField] private GameObject projectileHitVFXPrefab;
    [Tooltip("命中VFXの位置。未設定なら投擲物本体。")]
    [SerializeField] private Transform projectileHitVFXTargetPoint;
    [SerializeField, Min(0f)] private float projectileHitVFXLifetime = 3f;

    private ThrowConfig cfg;
    private int damage;
    private bool held;      // バッグ〜耳で保持中（未武装）
    private bool released;  // 投擲済み
    private bool hasHit;
    private bool armed;     // 当たり判定有効（投擲後）

    private Rigidbody rb;
    private int groundBounceCount;
    private bool dissolving;
    private GameObject projectileTrailVFXInstance;
    private readonly List<Material> dissolveMats = new List<Material>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>バッグ地点で保持を開始する（物理・当たり・トレイルを止める）。</summary>
    public void BeginHold(ThrowConfig config)
    {
        cfg = config;
        damage = config.damage;
        held = true;
        released = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        SetCollidersEnabled(false);
        SetTrailsEnabled(false);

        transform.position = ResolvePoint(cfg.bagPoint, transform.position);

        if (cfg.safetyReleaseTime > 0f)
            StartCoroutine(SafetyReleaseRoutine(cfg.safetyReleaseTime));
    }

    /// <summary>
    /// 運搬の進捗を更新する。k=0でバッグ、k=1で耳。Attack アニメのフレーム進捗を渡す。
    /// バッグ/耳は敵の子なので毎フレーム現在位置を追い、敵の移動・回転にも追従する。
    /// </summary>
    public void UpdateCarry(float k)
    {
        if (!held || released) return;
        Vector3 from = ResolvePoint(cfg.bagPoint, transform.position);
        Vector3 to   = ResolvePoint(cfg.earPoint, from);
        transform.position = SampleCarryArc(from, to, Mathf.Clamp01(k));
    }

    // バッグ→耳を、体の外側＋上へ膨らむ弧（二次ベジエ）で運ぶ。直線だとモデルを貫通するため。
    // k はスムーズ補間して加減速をつける。
    private Vector3 SampleCarryArc(Vector3 from, Vector3 to, float k)
    {
        float t = k * k * (3f - 2f * k);              // smoothstep（緩急）
        Vector3 mid = (from + to) * 0.5f;

        // 外側方向 = 投擲者中心から中点への水平ベクトル。潰れる場合は本体前方でフォールバック。
        Vector3 outward = mid - (cfg.ownerTransform != null ? cfg.ownerTransform.position : mid);
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-4f)
            outward = cfg.ownerTransform != null ? cfg.ownerTransform.forward : Vector3.forward;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;

        // 制御点を中点から「上 + 外側」へ押し出して弧を作る。
        Vector3 control = mid + Vector3.up * cfg.carryArcHeight + outward * cfg.carryArcOutward;

        // 二次ベジエ: (1-t)^2*from + 2(1-t)t*control + t^2*to
        float u = 1f - t;
        return (u * u) * from + (2f * u * t) * control + (t * t) * to;
    }

    /// <summary>耳の位置から放物線投擲する（1回だけ有効）。</summary>
    public void Release()
    {
        if (released) return;
        released = true;
        held = false;

        Vector3 launchPos = ResolvePoint(cfg.earPoint, transform.position);
        transform.position = launchPos;

        Vector3 targetPos = cfg.target != null
            ? cfg.target.position + Vector3.up * cfg.aimHeightOffset
            : launchPos + transform.forward;   // 相手消失時は前方へ逃がす
        Vector3 launchVel = BallisticVelocity(launchPos, targetPos, cfg.launchAngle, cfg.fallbackSpeed);

        if (launchVel.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(launchVel.normalized);

        // 武装（当たり有効化 → 投擲者を無視 → トレイル復帰 → ダメージ受付開始）
        SetCollidersEnabled(true);
        IgnoreColliders(cfg.ignoreColliders);
        SetTrailsEnabled(true);   // 運搬の軌跡は Clear 済みなので投擲位置から描き始める
        StartProjectileTrailVFX();
        armed = true;
        PlaySE(cfg.throwSE, cfg.throwSEVolume);

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;                 // 放物線を描くために重力を有効化
            // 高速移動で Player/Ground をすり抜けないよう連続衝突判定にする
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = launchVel;
            rb.angularVelocity = Random.onUnitSphere * (cfg.spinDegPerSec * Mathf.Deg2Rad);
        }

        // 何にも当たらず飛び続けた場合の迷子防止。
        if (maxAirLifetime > 0f) StartCoroutine(AirLifetimeRoutine());
    }

    // 駆動されないまま時間切れになったら自動投擲する（投擲者の消滅・中断への保険）。
    private IEnumerator SafetyReleaseRoutine(float seconds)
    {
        float e = 0f;
        while (e < seconds && !released)
        {
            e += Time.deltaTime;
            yield return null;
        }
        if (!released) Release();
    }

    private static Vector3 ResolvePoint(Transform t, Vector3 fallback) => t != null ? t.position : fallback;

    private void SetCollidersEnabled(bool value)
    {
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null) c.enabled = value;
    }

    private void SetTrailsEnabled(bool value)
    {
        foreach (TrailRenderer tr in GetComponentsInChildren<TrailRenderer>(true))
        {
            if (tr == null) continue;
            if (value)
            {
                tr.Clear();          // 運搬中の位置から尾を引かないよう履歴を消してから
                tr.emitting = true;
                tr.enabled = true;   // 投擲位置から新しく尾を引き始める
            }
            else
            {
                // コンポーネントごと無効化する。emitting だけ切ると autodestruct が働き、
                // トレイルが空になった時点で GameObject ごと破棄される（＝弾が途中で消える）。
                tr.enabled = false;
            }
        }
    }

    // 重力を考慮し、指定の打ち上げ角度で target に着弾する初速ベクトルを求める。
    private static Vector3 BallisticVelocity(Vector3 origin, Vector3 target, float launchAngleDeg, float fallbackSpeed)
    {
        float g = Mathf.Abs(Physics.gravity.y);

        Vector3 delta = target - origin;
        Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
        float d = horizontal.magnitude;   // 水平距離
        float h = delta.y;                 // 高低差

        float angle = launchAngleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);

        // 距離ゼロ/無重力/角度が低すぎて届かない場合は直線フォールバック
        float denom = 2f * cos * cos * (d * Mathf.Tan(angle) - h);
        if (d < 0.01f || g < 0.01f || denom <= 0.01f)
            return delta.sqrMagnitude > 1e-4f ? delta.normalized * fallbackSpeed : Vector3.forward * fallbackSpeed;

        float speed = Mathf.Sqrt(g * d * d / denom);
        Vector3 horizontalDir = horizontal / d;
        return horizontalDir * (speed * cos) + Vector3.up * (speed * Mathf.Sin(angle));
    }

    private void IgnoreColliders(Collider[] colliders)
    {
        if (colliders == null) return;
        Collider[] own = GetComponentsInChildren<Collider>();
        foreach (Collider self in own)
            foreach (Collider other in colliders)
                if (self != null && other != null)
                    Physics.IgnoreCollision(self, other);
    }

    // プレイヤーの当たり判定はトリガーコライダーなので OnTriggerEnter で受ける。
    private void OnTriggerEnter(Collider other)
    {
        HandleContact(other.gameObject, other);
    }

    // 地形など非トリガー相手はこちらで受ける。
    private void OnCollisionEnter(Collision collision)
    {
        HandleContact(collision.gameObject, collision.collider);
    }

    private void HandleContact(GameObject hitObject, Collider hitCollider)
    {
        if (dissolving || !released) return;   // ディゾルブ中／運搬中(未投擲)は無反応

        // 他エネミー・投擲者には反応しない（IgnoreColliders で除外済みだが二重防御）
        if (hitObject.CompareTag("Enemy")) return;

        if (hitObject.CompareTag("Player"))
        {
            // まだ武装中（＝一度も地面に触れていない）なら1回だけダメージ → ディゾルブして消える。
            if (armed && !hasHit)
            {
                IHealth ph = hitObject.GetComponentInParent<IHealth>()
                          ?? hitCollider.GetComponentInParent<IHealth>();
                if (ph != null)
                    CombatSystem.Instance.Damage(
                        ph,
                        damage,
                        default(DamageContext).WithPlayerDamageType(cfg.damageType),
                        this);
                PlayProjectileHitVFX();
                PlaySE(cfg.hitSE, cfg.hitSEVolume);
                hasHit = true;
                BeginDissolve(false);
            }
            // 地面接触後（未武装）はプレイヤーに触れても素通り（ダメージなし）。
            return;
        }

        // 地面・壁など：初接地でダメージ無効化し、数回バウンドしてから消える。
        BounceOnGround();
    }

    // 初接地でダメージ無効化。数回バウンドさせ、跳ねが弱まるか回数超過でディゾルブへ。
    private void BounceOnGround()
    {
        armed = false;               // 一度地面に当たったらもうダメージは入らない
        groundBounceCount++;

        float upSpeed = rb != null ? Mathf.Abs(rb.linearVelocity.y) * bounceUpDamping : 0f;

        if (rb == null || groundBounceCount > maxGroundBounces || upSpeed < minBounceUpSpeed)
        {
            BeginDissolve(true);     // 跳ね終わり → その場で静止してディゾルブ
            return;
        }

        // 上向きに跳ね返し、水平は減衰。転がり感のスピンも付与。
        Vector3 v = rb.linearVelocity;
        v.y = upSpeed;
        v.x *= bounceHorizontalDamping;
        v.z *= bounceHorizontalDamping;
        rb.linearVelocity = v;
        rb.angularVelocity = Random.onUnitSphere * (cfg.spinDegPerSec * Mathf.Deg2Rad * 0.5f);
    }

    // 当たり判定・物理・トレイルを止め、マテリアルをディゾルブ用へ差し替えて消滅させる。
    private void BeginDissolve(bool settle)
    {
        if (dissolving) return;
        dissolving = true;
        armed = false;
        hasHit = true;

        SetCollidersEnabled(false);   // これ以上ヒットしない
        SetTrailsEnabled(false);
        StopProjectileTrailVFX();

        if (rb != null)
        {
            if (settle) rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;    // その場でディゾルブ（落下しない）
        }

        BuildDissolveMaterials();
        StartCoroutine(DissolveRoutine());
    }

    private void StartProjectileTrailVFX()
    {
        if (projectileTrailVFXPrefab == null || projectileTrailVFXInstance != null) return;
        Transform point = projectileTrailVFXTargetPoint != null ? projectileTrailVFXTargetPoint : transform;
        projectileTrailVFXInstance = Instantiate(projectileTrailVFXPrefab, point);
        projectileTrailVFXInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void StopProjectileTrailVFX()
    {
        if (projectileTrailVFXInstance == null) return;
        Destroy(projectileTrailVFXInstance);
        projectileTrailVFXInstance = null;
    }

    private void PlayProjectileHitVFX()
    {
        if (projectileHitVFXPrefab == null) return;
        Transform point = projectileHitVFXTargetPoint != null ? projectileHitVFXTargetPoint : transform;
        GameObject instance = Instantiate(projectileHitVFXPrefab, point.position, point.rotation);
        if (projectileHitVFXLifetime > 0f) Destroy(instance, projectileHitVFXLifetime);
    }

    private void PlaySE(AudioClip clip, float volume)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource.PlayClipAtPoint(clip, transform.position, Mathf.Clamp01(volume));
    }

    // 各レンダラーのマテリアルを、元の色/テクスチャを引き継いだディゾルブ用インスタンスへ差し替える。
    private void BuildDissolveMaterials()
    {
        dissolveMats.Clear();
        Shader sh = dissolveShader != null ? dissolveShader : Shader.Find("Enemy/ProjectileDissolve");
        if (sh == null)
            return;   // シェーダーが見つからなければ差し替えなし（時間経過で消える）

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;

            Material[] originals = r.sharedMaterials;
            Material[] swapped = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material dm = new Material(sh);
                Material src = originals[i];
                if (src != null)
                {
                    Texture tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex")
                                : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap")
                                : null;
                    if (tex != null) dm.SetTexture("_MainTex", tex);

                    Color tint = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                               : src.HasProperty("_Color")     ? src.GetColor("_Color")
                               : Color.white;
                    dm.SetColor("_BaseColor", tint);
                }
                dm.SetColor("_EdgeColor", dissolveEdgeColor);
                dm.SetFloat("_NoiseScale", dissolveNoiseScale);
                dm.SetFloat("_EdgeWidth", dissolveEdgeWidth);
                dm.SetFloat("_Dissolve", 0f);
                swapped[i] = dm;
                dissolveMats.Add(dm);
            }
            r.materials = swapped;
        }
    }

    private IEnumerator DissolveRoutine()
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, dissolveDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);
            for (int i = 0; i < dissolveMats.Count; i++)
                if (dissolveMats[i] != null) dissolveMats[i].SetFloat("_Dissolve", a);
            yield return null;
        }
        Destroy(gameObject);
    }

    // 何にも当たらず飛び続けた場合の迷子防止。時間切れでディゾルブする。
    private IEnumerator AirLifetimeRoutine()
    {
        float e = 0f;
        while (e < maxAirLifetime && !dissolving)
        {
            e += Time.deltaTime;
            yield return null;
        }
        if (!dissolving) BeginDissolve(true);
    }
}
