/*=============================================================================

 File   : [B2_MoveHedgeHog.cs]
 Desc   : B2_SummonHedgehog�̍s���Ŏg�p����A�n���l�Y�~�̈ړ�������S������N���X
 ------------------------------------------------------------------------------

 Date   : 2026/06/10
 Author : Yokoyama Haruki
 
===============================================================================*/

using UnityEngine;

public class B2_MoveHedgeHog : MonoBehaviour
{
    enum State
    {
        Generate,
        Stay,
        Flying,
        Rolling
    }

    [Header("Generate")]
    [SerializeField] float generateTime = 1f;
    [SerializeField] AnimationCurve generateCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Flying")]
    [SerializeField] float gravity = 30f;

    [Header("Rolling")]
    [SerializeField] float rollingSpeed = 15f;
    [SerializeField] float collisionRadius = 0.5f;
    [SerializeField] float visualRadius = 0.5f;

    [SerializeField] LayerMask groundLayer;
    [SerializeField] LayerMask wallLayer;

    [Header("Damage")]
    [SerializeField] LayerMask playerLayer;
    [SerializeField] int damage = 10;
    [SerializeField] float hitInterval = 0.5f;

    [Header("VFX")]
    [Tooltip("砂ぼこりVFX(発生量・エフェクト等はこのコンポーネント側で設定)。未設定なら自動生成")]
    [SerializeField] B2_HedgehogDustVFX dustVFX;
    [Tooltip("ぶたれた瞬間のVFX(VFX_Hit_0003)。打ち出しの手応えを出す。未設定なら何も出ない")]
    [SerializeField] GameObject launchVfxPrefab;
    [Tooltip("プレイヤーに当たった瞬間のVFX(VFX_Hit_0002)。未設定なら何も出ない")]
    [SerializeField] GameObject hitVfxPrefab;
    [Tooltip("上2つのVFXを消すまでの時間(秒)。0以下なら自動で消さない。\n" +
             "ハリネズミ本体とは別に生成するので、これが無いとシーンに残り続ける")]
    [SerializeField] float vfxLifetime = 3f;

    [Header("Lifetime")]
    [Tooltip("転がり始めてから消えるまでの時間(秒)。0以下なら時間では消えない。\n" +
             "これが無いと召喚のたびにハリネズミが増え続ける")]
    [SerializeField] float rollingLifetime = 8f;
    [Tooltip("このY座標より下へ落ちたら消す(場外・床抜けの保険)")]
    [SerializeField] float killBelowY = -20f;

    [Header("SE")]
    [SerializeField] AudioSource seSource;
    [Tooltip("打ち出した瞬間のSE")]
    [SerializeField] AudioClip launchSe;
    [Tooltip("着地した瞬間のSE (Boss02_HedgehogBall など)")]
    [SerializeField] AudioClip landSe;
    [Tooltip("プレイヤーに当たった瞬間のSE")]
    [SerializeField] AudioClip hitSe;

    State state;

    Vector3 velocity;
    Vector3 moveDir;
    Vector3 originalScale;
    float generateTimer;
    float hitTimer;
    float rollingTimer;

    /// <summary>生成完了(Stay)で打ち出し可能なとき true。</summary>
    public bool IsLaunchable => state == State.Stay;

    void Start()
    {
        state = State.Generate;
        originalScale = transform.localScale;
        transform.localScale = Vector3.zero;

        // 未設定なら砂ぼこりVFXを自動生成(この場合エフェクト等は既定値。通常はプレハブで設定)
        if (dustVFX == null)
        {
            dustVFX = GetComponentInChildren<B2_HedgehogDustVFX>();
            if (dustVFX == null)
            {
                dustVFX = gameObject.AddComponent<B2_HedgehogDustVFX>();
            }
        }
    }

    public void Launch(Vector3 launchVelocity)
    {
        if (state != State.Stay) return; // 生成完了(Stay)前は打ち出さない

        velocity = launchVelocity * rollingSpeed;

        state = State.Flying;
        PlaySe(launchSe);
        SpawnVfx(launchVfxPrefab, transform.position);
    }

    void PlaySe(AudioClip clip)
    {
        if (seSource != null && clip != null) seSource.PlayOneShot(clip);
    }

    // 打撃/着弾のVFXを生成する。ハリネズミは即座に飛んでいく(着弾時は消える)ので
    // 子にはせず、その場に置いて寿命で消す
    void SpawnVfx(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;

        GameObject vfx = Instantiate(prefab, position, Quaternion.identity);
        if (vfxLifetime > 0f) Destroy(vfx, vfxLifetime);
    }

    void Update()
    {
        if (hitTimer > 0f) hitTimer -= Time.deltaTime;

        // 場外・床抜けの保険。転がり寿命だけだと、飛んでいる間に落ちた個体が残る
        if (transform.position.y < killBelowY)
        {
            Destroy(gameObject);
            return;
        }

        switch (state)
        {
            case State.Generate:
                UpdateGenerate();
                break;
            case State.Stay:
                break;
            case State.Flying:
                UpdateFlying();
                break;

            case State.Rolling:
                UpdateRolling();
                break;
        }
    }

    void UpdateGenerate()
    {
        generateTimer += Time.deltaTime;

        float t = Mathf.Clamp01(generateTimer / generateTime);
        transform.localScale = originalScale * generateCurve.Evaluate(t);

        if (t >= 1f)
        {
            state = State.Stay;
        }
    }
    void UpdateFlying()
    {
        velocity += Vector3.down * gravity * Time.deltaTime;

        Vector3 nextPos =
            transform.position +
            velocity * Time.deltaTime;

        float distance =
            Vector3.Distance(
                transform.position,
                nextPos);

        Vector3 dir =
            (nextPos - transform.position).normalized;

        if (Physics.SphereCast(
            transform.position,
            collisionRadius,
            dir,
            out RaycastHit hit,
            distance,
            groundLayer))
        {
            StartRolling(hit);
            return;
        }

        transform.position = nextPos;
    }

    void StartRolling(RaycastHit hit)
    {
        transform.position =
            hit.point +
            hit.normal * collisionRadius;

        // ���������������o��
        moveDir =
            Vector3.ProjectOnPlane(
                velocity,
                Vector3.up).normalized;

        if (moveDir.sqrMagnitude < 0.01f)
        {
            moveDir = transform.forward;
        }
        state = State.Rolling;
        rollingTimer = 0f;
        PlaySe(landSe);

        // 着地の砂ぼこりバースト＋転がりトレイル開始
        if (dustVFX != null)
        {
            dustVFX.PlayImpact(hit.point);
            dustVFX.SetTrail(true);
        }
    }

    void UpdateRolling()
    {
        // 転がり寿命。これを過ぎたら消す(放置すると永久に転がり続けて蓄積する)
        rollingTimer += Time.deltaTime;
        if (rollingLifetime > 0f && rollingTimer >= rollingLifetime)
        {
            Destroy(gameObject);
            return;
        }

        float moveDistance =
            rollingSpeed * Time.deltaTime;

        // 壁での反射
        if (Physics.SphereCast(
            transform.position,
            collisionRadius,
            moveDir,
            out RaycastHit hit,
            moveDistance,
            wallLayer))
        {
            Vector3 normal = hit.normal;
            normal.y = 0f;

            moveDir =
                Vector3.Reflect(
                    moveDir,
                    normal.normalized).normalized;
        }

        // プレイヤーへのヒット判定(壁とは別レイヤーで検出)
        if (Physics.SphereCast(
            transform.position,
            collisionRadius,
            moveDir,
            out RaycastHit playerHit,
            moveDistance,
            playerLayer))
        {
            HitPlayer(playerHit);
        }

        transform.position +=
            moveDir * moveDistance;

        RotateVisual(moveDistance);
    }

    void RotateVisual(float moveDistance)
    {
        float angle =
            moveDistance /
            (2f * Mathf.PI * visualRadius)
            * 360f;

        Vector3 axis =
            Vector3.Cross(
                Vector3.up,
                moveDir);

        transform.Rotate(
            axis,
            angle,
            Space.World);
    }

    void HitPlayer(RaycastHit hit)
    {
        if (hitTimer > 0f) return; // 連続ヒット防止
        hitTimer = hitInterval;

        IHealth health = hit.collider.GetComponentInParent<IHealth>();
        if (health != null)
        {
            CombatSystem.Instance.Damage(health, damage);
            PlaySe(hitSe);
            SpawnVfx(hitVfxPrefab, hit.point);
        }
    }
}
