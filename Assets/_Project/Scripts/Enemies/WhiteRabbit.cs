/**===========================================================================

 File   : 白ウサギAIクラス [WhiteRabbitAI.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 仕様   : 白ウサギ【近】（特殊）
            ・発見処理
            ・プレイヤーを見つけたら逃げ回るだけ（攻撃・反撃なし）
            ・アニメーションは Unity（Animator）側で設定するため非干渉
 Note   : EnemyAI とは独立した単体スクリプト（EnemyAI は継承しない）。
          EnemyHealth.onDied を購読して死亡を検知する。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.AI;

/**===========================================================================
  白ウサギAIクラス
  プレイヤーを発見すると逃げ回る臆病な敵。
===========================================================================**/
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public class WhiteRabbitAI : MonoBehaviour
{

    [Header("探索設定")]
    [Tooltip("この距離内にプレイヤーが入ると逃げ出す")]
    [SerializeField] private float detectionRange = 10.0f;

    [Header("逃走設定")]
    [Tooltip("逃走中の移動速度倍率")]
    [SerializeField] private float fleeSpeedMultiplier = 1.6f;
    [Tooltip("1回の逃走で進む距離（逃げ先の目標までの距離）")]
    [SerializeField] private float fleeStep = 6.0f;
    [Tooltip("逃げ先を再選択する間隔（秒）")]
    [SerializeField] private float fleeRepathInterval = 0.4f;
    [Tooltip("逃走方向に加える左右の振れ角（度）。大きいほど“逃げ回る”")]
    [SerializeField] private float fleeWanderAngle = 55f;

    [Header("発見処理設定")]
    [Tooltip("発見時に固まる（ビクッとする）時間（秒）")]
    [SerializeField] private float alertDuration = 0.2f;

    // コンポーネント参照
    private NavMeshAgent navMeshAgent;
    private EnemyHealth enemyHealth;
    private Transform playerTransform;

    // ステート
    private enum State { Idle, Flee, Dead }
    private State currentState = State.Idle;

    // 内部変数
    private float fleeRepathTimer = 0f;
    private float alertTimer = 0f;
    private float baseSpeed;

    /**=======================================================================
      初期化
    =======================================================================**/
    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        enemyHealth = GetComponent<EnemyHealth>();
    }

    /**=======================================================================
      開始処理
    =======================================================================**/
    void Start()
    {
        baseSpeed = navMeshAgent.speed;
        navMeshAgent.stoppingDistance = 0f; // 逃げ先の手前で止まらない

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }

        enemyHealth.onDied.AddListener(OnDied);
    }

    /**=======================================================================
      更新処理
    =======================================================================**/
    void Update()
    {
        if (currentState == State.Dead) return;
        if (playerTransform == null) return;

        float distance = Vector3.Distance(transform.position, playerTransform.position);

        UpdateState(distance);
        ExecuteState(distance);
    }

    /**=======================================================================
      ステート更新
      Idle → Flee へ遷移した瞬間に発見処理を呼ぶ
    =======================================================================**/
    private void UpdateState(float distance)
    {
        State previous = currentState;

        currentState = (distance <= detectionRange) ? State.Flee : State.Idle;

        if (previous == State.Idle && currentState == State.Flee)
        {
            OnPlayerDetected();
        }
    }

    /**=======================================================================
      ステート実行
    =======================================================================**/
    private void ExecuteState(float distance)
    {
        switch (currentState)
        {
            case State.Idle: ExecuteIdle(); break;
            case State.Flee: ExecuteFlee(); break;
        }
    }

    /**=======================================================================
      Idleステート
    =======================================================================**/
    private void ExecuteIdle()
    {
        navMeshAgent.ResetPath();
    }

    /**=======================================================================
      Fleeステート（逃走）
    =======================================================================**/
    private void ExecuteFlee()
    {
        // 発見直後の硬直
        if (alertTimer > 0f)
        {
            alertTimer -= Time.deltaTime;
            navMeshAgent.ResetPath();
            FacePlayer();
            return;
        }

        Flee();
        FaceMoveDirection();
    }

    /**=======================================================================
      発見処理
      プレイヤーを見つけてビクッと固まってから逃げ出す
    =======================================================================**/
    private void OnPlayerDetected()
    {
        alertTimer = alertDuration;
        Debug.Log($"[{gameObject.name}] プレイヤーを発見！逃げる！");
    }

    /**=======================================================================
      逃走処理
    =======================================================================**/
    private void Flee()
    {
        navMeshAgent.speed = baseSpeed * fleeSpeedMultiplier;

        fleeRepathTimer -= Time.deltaTime;

        bool reached = !navMeshAgent.pathPending &&
                       navMeshAgent.remainingDistance <= 0.3f;

        if (fleeRepathTimer <= 0f || reached || !navMeshAgent.hasPath)
        {
            PickFleeDestination();
            fleeRepathTimer = fleeRepathInterval;
        }
    }

    /**=======================================================================
      逃げ先を決める
    =======================================================================**/
    private void PickFleeDestination()
    {
        Vector3 away = transform.position - playerTransform.position;
        away.y = 0f;

        if (away.sqrMagnitude < 0.01f)
        {
            away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        }
        away.Normalize();

        float angle = Random.Range(-fleeWanderAngle, fleeWanderAngle);
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
        Vector3 target = transform.position + dir * fleeStep;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, fleeStep, NavMesh.AllAreas))
        {
            navMeshAgent.SetDestination(hit.position);
            return;
        }

        // 行き止まりなら横方向へ逃げ直す
        Vector3 side = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * away;
        Vector3 sideTarget = transform.position + side * fleeStep;
        if (NavMesh.SamplePosition(sideTarget, out NavMeshHit hit2, fleeStep, NavMesh.AllAreas))
        {
            navMeshAgent.SetDestination(hit2.position);
        }
    }

    /**=======================================================================
      プレイヤーの方向を向く
    =======================================================================**/
    private void FacePlayer()
    {
        Vector3 lookDir = playerTransform.position - transform.position;
        lookDir.y = 0f;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    /**=======================================================================
      移動方向を向く（逃げている向きを向かせる）
    =======================================================================**/
    private void FaceMoveDirection()
    {
        Vector3 v = navMeshAgent.velocity;
        v.y = 0f;
        if (v.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(v.normalized);
        }
    }

    /**=======================================================================
      死亡時コールバック
    =======================================================================**/
    private void OnDied()
    {
        currentState = State.Dead;
        if (navMeshAgent.isOnNavMesh) navMeshAgent.ResetPath();
        navMeshAgent.enabled = false;
    }

    /**=======================================================================
      ギズモ描画（デバッグ用）
    =======================================================================**/
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, fleeStep);
    }
}