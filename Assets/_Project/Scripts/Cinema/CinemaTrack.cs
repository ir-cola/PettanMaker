using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// 汎用シネマトラック(キャラ専用スクリプト不要)。
/// 「Target を Mover Root で歩かせ、Camera を Camera Root で追う」を汎用化し、
/// 接地(NavMesh/Raycast)・歩きアニメ(Animator パラメータ)・CC 無効化・TPS 退避を
/// 組み込み設定として持つ。挙動の差し込みは Inspector のイベント(Trigger API)で行う:
///   ・onBegin / onEnd … 開始/終了(入力凍結や次の演出への連結を UnityEvent で)
///   ・Triggers … 進捗 0..1 の任意地点で UnityEvent(煙 spawn / 出現 / SE など)
/// 公開 API: Play() / Play(onComplete) / Stop() / IsPlaying / Progress。
/// 高度な特化が要るときだけ継承して hook を override する。
/// </summary>
public class CinemaTrack : MonoBehaviour
{
    public enum GroundMode { None, NavMesh, Raycast }
    public enum PathInterpolation { Linear, Spline }
    public enum DurationMode { MoveSpeed, FixedDuration }
    public enum MoveDriver { Auto, Transform, NavMeshAgent, Rigidbody }
    public enum PathSpace { WorldSnapshot, LocalToTrack, LocalToTarget }

    [Header("Playback")]
    [SerializeField] private bool playOnStart = false;
    [SerializeField] private float startDelay = 0f;
    [SerializeField] private bool useUnscaledTime = false;
    [SerializeField] private DurationMode durationMode = DurationMode.MoveSpeed;
    [SerializeField] private float duration = 3f;
    [SerializeField] private AnimationCurve progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField] private bool restoreMoverOnStop = false;
    [SerializeField] private bool restoreCameraOnStop = false;

    [Header("Target (動かす対象。null なら移動なし)")]
    [SerializeField] protected Transform moverTarget;
    [Tooltip("対象の経路 Root(子=通過点。位置=道 / 回転=向き)。未設定なら自身の子")]
    [SerializeField] protected Transform moverRoot;
    [Tooltip("MoverRoot の解釈。WorldSnapshot=現在のワールド位置、LocalToTrack=Track基準、LocalToTarget=MoverTarget基準")]
    [SerializeField] protected PathSpace moverPathSpace = PathSpace.WorldSnapshot;
    [SerializeField] protected PathInterpolation moverInterpolation = PathInterpolation.Linear;
    [SerializeField] protected float moveSpeed = 2.0f;
    [SerializeField] protected float turnSpeed = 540f;
    [SerializeField] protected bool faceWaypointRotation = true;
    [SerializeField] protected float endHold = 0.6f;
    [Tooltip("Target の動かし方。Auto は NavMeshAgent / Rigidbody / Transform の順に選ぶ")]
    [SerializeField] protected MoveDriver moveDriver = MoveDriver.Auto;
    [Tooltip("移動中に NavMeshAgent の経路を止める。NavMeshAgent Driver では Warp で同期する")]
    [SerializeField] protected bool stopNavMeshAgentWhileMoving = true;
    [Tooltip("Rigidbody Driver で移動中だけ kinematic + gravity off にして物理に押し戻されないようにする")]
    [SerializeField] protected bool makeRigidbodyKinematicWhileMoving = true;
    [Tooltip("移動中に止めたい独自制御 Behaviour。AI/入力/移動制御などを必要に応じて明示登録する")]
    [SerializeField] protected Behaviour[] disableBehavioursWhileMoving;
    [Tooltip("移動中に CharacterController を無効化する。自走キャラ(Player等)では駆動側が Move を呼び続けて " +
             "『inactive controller』警告が出るので OFF のまま、代わりに onBegin で駆動を凍結すること " +
             "(例: onBegin→player.SetUIMode(true) / onEnd→SetUIMode(false))")]
    [SerializeField] protected bool disableCharacterControllerWhileMoving = false;

    [Header("Grounding")]
    [SerializeField] protected GroundMode groundMode = GroundMode.NavMesh;
    [SerializeField] protected float navSampleRadius = 3f;
    [Tooltip("Raycast モード時に地面とみなすレイヤー")]
    [SerializeField] protected LayerMask groundLayers = ~0;
    [SerializeField] protected float groundRayUp = 3f;

    [Header("Camera")]
    [Tooltip("カメラの経路 Root(子=カメラ点 pos+rot)。未設定ならカメラは操作しない")]
    [SerializeField] protected Transform cameraRoot;
    [Tooltip("CameraRoot の解釈。WorldSnapshot=現在のワールド位置、LocalToTrack=Track基準、LocalToTarget=MoverTarget基準")]
    [SerializeField] protected PathSpace cameraPathSpace = PathSpace.WorldSnapshot;
    [SerializeField] protected PathInterpolation cameraInterpolation = PathInterpolation.Linear;
    [Tooltip("設定するとカメラは常にこの Transform を注視(点の回転より優先)")]
    [SerializeField] protected Transform cameraLookAt;
    [Tooltip("ONなら再生開始時の LookAt 位置を固定する。Gizmo と実行時の見え方を一致させる")]
    [SerializeField] protected bool lockCameraLookAtOnBegin = true;
    [Tooltip("ONなら終端では LookAt より最後の CameraRoot 点の回転を優先する")]
    [SerializeField] protected bool useEndpointRotationOnEnd = true;
    [Tooltip("カメラをカット切替にする(補間せず各点にスナップ)")]
    [SerializeField] protected bool cameraCuts = false;
    [Tooltip("カメラ経路がある間、無効化する TPS カメラ。未設定なら Instance / Main")]
    [SerializeField] protected TPSCameraController suspendCameraController;

    [Header("Animator (任意。移動中パラメータを毎フレーム上書き)")]
    [Tooltip("未設定なら moverTarget 配下から取得")]
    [SerializeField] protected Animator animator;
    [Tooltip("ONなら moveFloatParam / moveBoolParam が空のとき、Animator 内の一般的な移動パラメータを自動使用する")]
    [SerializeField] protected bool autoDetectMoveAnimatorParams = true;
    [Tooltip("移動中に animator.speed が 0 なら 1 に戻す")]
    [SerializeField] protected bool resumeAnimatorWhileMoving = true;
    [Tooltip("移動ブレンド用 Float パラメータ名(例: MoveSpeed / Speed / Move)。空なら自動検出")]
    [SerializeField] protected string moveFloatParam = "";
    [SerializeField] protected float moveFloatValue = 1f;
    [Tooltip("移動フラグ Bool パラメータ名。空なら自動検出")]
    [SerializeField] protected string moveBoolParam = "";

    [Header("Events (Trigger API)")]
    public UnityEvent onBegin;
    public UnityEvent onEnd;
    public FloatEvent onProgress;
    [Tooltip("進捗 0..1 の地点で UnityEvent を発火(煙/出現/SE などを差し込む)")]
    [SerializeField] private List<Trigger> triggers = new();

    [Serializable]
    public class Trigger
    {
        public string name;
        [Range(0f, 1f)] public float atProgress = 0.5f;
        public UnityEvent onReach;
        [NonSerialized] public bool fired;
    }

    [Serializable]
    public class FloatEvent : UnityEvent<float> { }

    [Header("Debug")]
    [SerializeField] protected bool debugGizmos = true;
    [SerializeField] protected float debugArrowLength = 2f;

    protected Transform camTf;
    protected CinemaPath moverPath, camPath;
    protected bool playing;
    private bool moverPlaying;
    private bool cameraPlaying;
    private Vector3 lockedCameraLookAtPosition;
    private bool hasLockedCameraLookAt;
    private Vector3 savedMoverPosition;
    private Quaternion savedMoverRotation;
    private bool hasSavedMoverPose;
    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;
    private bool hasSavedCameraPose;

    public bool IsPlaying => playing;
    public bool IsMoverPlaying => moverPlaying;
    public bool IsCameraPlaying => cameraPlaying;
    public float Progress { get; private set; }
    public float MoverProgress { get; private set; }
    public float CameraProgress { get; private set; }
    public Transform CameraRoot => cameraRoot;
    public Transform MoverRoot => moverRoot;
    // 外部(B1_CameraDirector)がカメラを代理駆動する際に、同じ注視規則を再現するための読み取り口。
    public Transform CameraLookAt => cameraLookAt;
    public bool LockCameraLookAtOnBegin => lockCameraLookAtOnBegin;
    public bool UseEndpointRotationOnEnd => useEndpointRotationOnEnd;
    public bool CameraCuts => cameraCuts;

    // 接地/CC ランタイム
    private float footOffset;
    private int groundMask;
    private CharacterController moverCC;
    private bool ccWasEnabled;
    private NavMeshAgent moverAgent;
    private bool agentWasEnabled;
    private bool agentWasStopped;
    private bool agentHadPath;
    private Vector3 agentDestination;
    private Rigidbody moverRigidbody;
    private bool rbWasKinematic;
    private bool rbWasUseGravity;
    private bool rbStateSaved;
    private MoveDriver activeMoveDriver;
    private readonly List<BehaviourState> disabledBehaviourStates = new();
    private string resolvedMoveFloatParam;
    private string resolvedMoveBoolParam;

    private struct BehaviourState
    {
        public Behaviour Behaviour;
        public bool WasEnabled;

        public BehaviourState(Behaviour behaviour)
        {
            Behaviour = behaviour;
            WasEnabled = behaviour != null && behaviour.enabled;
        }
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        endHold = Mathf.Max(0f, endHold);
        startDelay = Mathf.Max(0f, startDelay);
        duration = Mathf.Max(0.01f, duration);
        navSampleRadius = Mathf.Max(0f, navSampleRadius);
        groundRayUp = Mathf.Max(0f, groundRayUp);
        if (progressCurve == null || progressCurve.length == 0)
            progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    // ======================================================================
    //  公開 API
    // ======================================================================
    public void Play() => Play(null);

    public void Play(Action onComplete)
    {
        Play(true, true, onComplete);
    }

    public void PlayMoverOnly() => PlayMover(null);
    public void PlayCameraOnly() => PlayCamera(null);
    public void PlayMover(Action onComplete) => Play(true, false, onComplete);
    public void PlayCamera(Action onComplete) => Play(false, true, onComplete);

    public void Play(bool includeMover, bool includeCamera, Action onComplete = null)
    {
        if (!includeMover && !includeCamera) return;
        if ((includeMover && moverPlaying) || (includeCamera && cameraPlaying)) return;

        bool wasPlaying = moverPlaying || cameraPlaying; // 既に別チャンネルが再生中か
        if (includeMover) moverPlaying = true;
        if (includeCamera) cameraPlaying = true;
        playing = moverPlaying || cameraPlaying;
        StartCoroutine(Run(includeMover, includeCamera, onComplete, isPrimary: !wasPlaying));
    }

    public void Stop()
    {
        StopAllCoroutines();
        DriveAnimator(false);
        if (disableCharacterControllerWhileMoving && moverCC != null) moverCC.enabled = ccWasEnabled;
        RestoreMoverControl();
        SuspendCamera(false);
        RestoreSavedPoses();
        moverPlaying = false;
        cameraPlaying = false;
        playing = false;
    }

    public void Restart()
    {
        Stop();
        Play();
    }

    public void SetMoverTarget(Transform target) => moverTarget = target;
    public void SetMoverRoot(Transform root) => moverRoot = root;
    public void SetCameraRoot(Transform root) => cameraRoot = root;
    public void SetCameraLookAt(Transform target) => cameraLookAt = target;

    [ContextMenu("Snap Target To First Mover Point")]
    private void ContextSnapTargetToFirstMoverPoint() => SnapTargetToMoverPoint(false);

    [ContextMenu("Snap Target To Last Mover Point")]
    private void ContextSnapTargetToLastMoverPoint() => SnapTargetToMoverPoint(true);

    [ContextMenu("Snap Camera To First Camera Point")]
    private void ContextSnapCameraToFirstCameraPoint() => SnapCameraToCameraPoint(false);

    [ContextMenu("Snap Camera To Last Camera Point")]
    private void ContextSnapCameraToLastCameraPoint() => SnapCameraToCameraPoint(true);

    private IEnumerator Run(bool includeMover, bool includeCamera, Action onComplete, bool isPrimary = true)
    {
        Setup();

        if (startDelay > 0f)
        {
            float wait = 0f;
            while (wait < startDelay)
            {
                wait += DeltaTime();
                yield return null;
            }
        }

        var dense = BuildMoverPoints();
        bool driveCamera = includeCamera && camPath.IsValid && camTf != null;
        bool driveMover  = includeMover && moverTarget != null && dense.Count > 0;
        bool fixedDuration = durationMode == DurationMode.FixedDuration || !driveMover;
        SaveRuntimePoses(driveMover, driveCamera);

        if (driveMover)
        {
            SuspendMoverControl();
            GroundNow(); // 初回接地(CC は内部で一時退避して安全にスナップ)
            if (disableCharacterControllerWhileMoving && moverCC != null)
            {
                ccWasEnabled = moverCC.enabled;
                moverCC.enabled = false;
            }
        }

        // onBegin/OnEnd は再生セッション単位で 1 回だけ。isPrimary = このRunが idle→再生 の起点。
        // (PlayCameraOnly の後に PlayMoverOnly を重ねても onBegin は先頭で 1 回、onEnd は末尾で 1 回)
        if (isPrimary)
        {
            OnBegin();
            onBegin?.Invoke();
        }
        if (driveCamera)
        {
            LockCameraLookAt();
            SuspendCamera(true);
        }

        // トリガー/onProgress は 1 本のタイムラインが所有する。Mover を持つトラックでは
        // Mover 側が所有し、カメラ単独再生(Mover は別再生 or 無し)ではカメラ側が所有する。
        // これで PlayCameraOnly→PlayMoverOnly を同時並行しても、トリガー二重発火や
        // Progress の奪い合い(片方が終端に達して全トリガーを撃つ等)が起きない。
        bool trackHasMover = moverTarget != null && dense.Count > 0;
        bool ownsTimeline = driveMover || (driveCamera && !trackHasMover);
        if (ownsTimeline) ResetTriggers();
        float total = driveMover ? CinemaSpline.HorizontalLength(moverTarget.position, dense) : 0.001f;
        float covered = 0f; int idx = 0;
        float elapsed = 0f;
        float playDuration = Mathf.Max(0.01f, duration);
        List<Vector3> fixedMoverPoints = driveMover && fixedDuration ? BuildMoverTimelinePoints(dense) : null;
        if (ownsTimeline) Progress = 0f;
        if (driveMover) MoverProgress = 0f;
        if (driveCamera) CameraProgress = 0f;

        while ((driveMover && !fixedDuration && idx < dense.Count) ||
               (fixedDuration && (driveMover || driveCamera) && elapsed < playDuration))
        {
            float dt = DeltaTime();
            float p;
            if (fixedDuration)
            {
                elapsed += dt;
                p = Mathf.Clamp01(elapsed / playDuration);
                float eased = EvaluateProgress(p);
                if (driveMover)
                {
                    Vector3 next = SamplePoints(fixedMoverPoints, eased);
                    next.y = GroundY(next, moverTarget.position.y); // FixedDuration でも接地させる(MoveSpeed と同じ)
                    MoveTargetTo(next);
                    ApplyMoverRotation(eased, dt);
                    DriveAnimator(true);
                    OnMoverTick(eased);
                }
                if (driveCamera) UpdateCamera(eased);
            }
            else
            {
                covered += StepMover(dense, ref idx, moveSpeed * dt);
                p = Mathf.Clamp01(covered / total);
                float eased = EvaluateProgress(p);
                ApplyMoverRotation(eased, dt);
                DriveAnimator(true);
                OnMoverTick(eased);
                if (driveCamera) UpdateCamera(eased);
            }
            if (driveMover) MoverProgress = p;
            if (driveCamera) CameraProgress = p;
            if (ownsTimeline)
            {
                Progress = p;
                onProgress?.Invoke(p);
                FireTriggers(p);
            }
            yield return null;
        }
        if (driveMover) MoverProgress = 1f;
        if (driveCamera) CameraProgress = 1f;
        if (ownsTimeline)
        {
            Progress = 1f;
            onProgress?.Invoke(1f);
            FireTriggers(1f);
        }

        float t = 0f;
        while (t < endHold)
        {
            DriveAnimator(false);
            OnMoverHold();
            if (driveCamera) UpdateCamera(1f);
            t += DeltaTime();
            yield return null;
        }

        DriveAnimator(false);
        if (driveMover && disableCharacterControllerWhileMoving && moverCC != null)
            moverCC.enabled = ccWasEnabled;
        if (driveMover) RestoreMoverControl();
        if (driveCamera) SuspendCamera(false);
        RestoreSavedPoses(driveMover, driveCamera);

        if (includeMover) moverPlaying = false;
        if (includeCamera) cameraPlaying = false;
        playing = moverPlaying || cameraPlaying;
        if (!playing) // 全チャンネル終了時に 1 回だけ onEnd
        {
            OnEnd();
            onEnd?.Invoke();
        }
        onComplete?.Invoke();
    }

    private void Setup()
    {
        ResolveRefs();
        moverPath = PathFromRootOrSelf(moverRoot != null ? moverRoot : transform, moverPathSpace, moverTarget);
        camPath   = PathFromRootOrSelf(cameraRoot, cameraPathSpace, moverTarget);
        moverCC   = moverTarget != null ? moverTarget.GetComponent<CharacterController>() : null;
        moverAgent = moverTarget != null ? moverTarget.GetComponent<NavMeshAgent>() : null;
        moverRigidbody = moverTarget != null ? moverTarget.GetComponent<Rigidbody>() : null;
        activeMoveDriver = ResolveMoveDriver();
        ResolveAnimatorParams();
        SetupGrounding();
    }

    // カメラ注視点のロックは「カメラを駆動する再生」だけが行う。Setup で無条件に行うと、
    // PlayCameraOnly と PlayMoverOnly を同時に走らせたとき、後発の Mover 側 Setup が
    // 実行中カメラのロック済み注視点を上書きしてしまうため、ここに分離する。
    private void LockCameraLookAt()
    {
        hasLockedCameraLookAt = cameraLookAt != null && lockCameraLookAtOnBegin;
        if (hasLockedCameraLookAt) lockedCameraLookAtPosition = cameraLookAt.position;
    }

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private float EvaluateProgress(float progress)
    {
        return progressCurve != null ? Mathf.Clamp01(progressCurve.Evaluate(progress)) : Mathf.Clamp01(progress);
    }

    private void SaveRuntimePoses(bool saveMover, bool saveCamera)
    {
        if (saveMover)
        {
            hasSavedMoverPose = restoreMoverOnStop && moverTarget != null;
            if (hasSavedMoverPose)
            {
                savedMoverPosition = moverTarget.position;
                savedMoverRotation = moverTarget.rotation;
            }
        }

        if (saveCamera)
        {
            hasSavedCameraPose = restoreCameraOnStop && camTf != null;
            if (hasSavedCameraPose)
            {
                savedCameraPosition = camTf.position;
                savedCameraRotation = camTf.rotation;
            }
        }
    }

    private void RestoreSavedPoses() => RestoreSavedPoses(true, true);

    private void RestoreSavedPoses(bool restoreMover, bool restoreCamera)
    {
        if (restoreMover && hasSavedMoverPose && moverTarget != null)
        {
            moverTarget.position = savedMoverPosition;
            moverTarget.rotation = savedMoverRotation;
            hasSavedMoverPose = false;
        }

        if (restoreCamera && hasSavedCameraPose && camTf != null)
        {
            camTf.position = savedCameraPosition;
            camTf.rotation = savedCameraRotation;
            hasSavedCameraPose = false;
        }
    }

    // ======================================================================
    //  移動 / カメラ
    // ======================================================================
    protected float StepMover(List<Vector3> pts, ref int idx, float budget)
    {
        float moved = 0f;
        while (idx < pts.Count && budget > 1e-5f)
        {
            Vector3 cur = moverTarget.position;
            Vector3 to = pts[idx] - cur;
            to.y = 0f;                          // 水平のみ。ウェイポイントの y は無視
            float dist = to.magnitude;

            if (dist <= budget || dist < 1e-4f)
            {
                Vector3 np = new Vector3(pts[idx].x, cur.y, pts[idx].z);
                np.y = GroundY(np, cur.y);
                MoveTargetTo(np);
                moved += dist; budget -= dist; idx++;
            }
            else
            {
                Vector3 np = cur + (to / dist) * budget;
                np.y = GroundY(np, cur.y);
                MoveTargetTo(np);
                moved += budget; budget = 0f;
            }
        }
        return moved;
    }

    private void MoveTargetTo(Vector3 position)
    {
        switch (activeMoveDriver)
        {
            case MoveDriver.NavMeshAgent:
                if (moverAgent != null && moverAgent.enabled && moverAgent.isOnNavMesh)
                {
                    moverAgent.Warp(position);
                    moverAgent.nextPosition = position;
                }
                else
                {
                    moverTarget.position = position;
                }
                break;
            case MoveDriver.Rigidbody:
                if (moverRigidbody != null)
                {
                    moverRigidbody.MovePosition(position);
                    moverTarget.position = position;
                }
                else
                {
                    moverTarget.position = position;
                }
                break;
            default:
                moverTarget.position = position;
                break;
        }
    }

    private void SuspendMoverControl()
    {
        if (!Application.isPlaying) return;
        SuspendExplicitBehaviours();
        SuspendRigidbody();

        if (moverAgent == null || !moverAgent.enabled)
        {
            agentWasEnabled = false;
            return;
        }

        agentWasEnabled = moverAgent.enabled;
        agentWasStopped = moverAgent.isStopped;
        agentHadPath = moverAgent.isOnNavMesh && moverAgent.hasPath;
        agentDestination = agentHadPath ? moverAgent.destination : moverTarget.position;

        if (stopNavMeshAgentWhileMoving && moverAgent.isOnNavMesh)
        {
            moverAgent.ResetPath();
            moverAgent.isStopped = true;
            moverAgent.nextPosition = moverTarget.position;
        }

        if (activeMoveDriver != MoveDriver.NavMeshAgent)
            moverAgent.enabled = false;
    }

    private void RestoreMoverControl()
    {
        if (!Application.isPlaying) return;
        if (moverAgent != null && agentWasEnabled)
        {
            if (!moverAgent.enabled) moverAgent.enabled = true;
            if (moverAgent.isOnNavMesh)
            {
                moverAgent.Warp(moverTarget.position);
                moverAgent.isStopped = agentWasStopped;
                if (agentHadPath) moverAgent.SetDestination(agentDestination);
            }

            agentWasEnabled = false;
        }

        RestoreRigidbody();
        RestoreExplicitBehaviours();
    }

    private MoveDriver ResolveMoveDriver()
    {
        if (moveDriver != MoveDriver.Auto) return moveDriver;
        if (moverAgent != null && moverAgent.enabled && moverAgent.isOnNavMesh) return MoveDriver.NavMeshAgent;
        if (moverRigidbody != null) return MoveDriver.Rigidbody;
        return MoveDriver.Transform;
    }

    private void SuspendRigidbody()
    {
        rbStateSaved = false;
        if (activeMoveDriver != MoveDriver.Rigidbody || moverRigidbody == null || !makeRigidbodyKinematicWhileMoving) return;

        rbWasKinematic = moverRigidbody.isKinematic;
        rbWasUseGravity = moverRigidbody.useGravity;
        rbStateSaved = true;
        moverRigidbody.isKinematic = true;
        moverRigidbody.useGravity = false;
    }

    private void RestoreRigidbody()
    {
        if (!rbStateSaved || moverRigidbody == null) return;

        moverRigidbody.isKinematic = rbWasKinematic;
        moverRigidbody.useGravity = rbWasUseGravity;
        rbStateSaved = false;
    }

    private void SuspendExplicitBehaviours()
    {
        disabledBehaviourStates.Clear();
        if (disableBehavioursWhileMoving == null) return;

        foreach (Behaviour behaviour in disableBehavioursWhileMoving)
        {
            if (behaviour == null || behaviour == this || !behaviour.enabled) continue;
            if (ContainsDisabledBehaviour(behaviour)) continue;

            disabledBehaviourStates.Add(new BehaviourState(behaviour));
            behaviour.enabled = false;
        }
    }

    private void RestoreExplicitBehaviours()
    {
        for (int i = 0; i < disabledBehaviourStates.Count; i++)
        {
            BehaviourState state = disabledBehaviourStates[i];
            if (state.Behaviour != null) state.Behaviour.enabled = state.WasEnabled;
        }
        disabledBehaviourStates.Clear();
    }

    private bool ContainsDisabledBehaviour(Behaviour behaviour)
    {
        for (int i = 0; i < disabledBehaviourStates.Count; i++)
            if (disabledBehaviourStates[i].Behaviour == behaviour)
                return true;
        return false;
    }

    private CinemaPath PathFromRootOrSelf(Transform root, PathSpace space, Transform target)
    {
        var path = new CinemaPath();
        if (root == null) return path;

        Transform anchor = ResolvePathAnchor(space, target);
        bool added = false;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform point = root.GetChild(i);
            if (point == null || !point.gameObject.activeInHierarchy) continue;
            AddPathPoint(path, root, point, space, anchor);
            added = true;
        }

        if (!added) AddPathPoint(path, root, root, space, anchor);
        return path;
    }

    private Transform ResolvePathAnchor(PathSpace space, Transform target)
    {
        switch (space)
        {
            case PathSpace.LocalToTrack:
                return transform;
            case PathSpace.LocalToTarget:
                return target != null ? target : transform;
            default:
                return null;
        }
    }

    private static void AddPathPoint(CinemaPath path, Transform root, Transform point, PathSpace space, Transform anchor)
    {
        if (space == PathSpace.WorldSnapshot || anchor == null)
        {
            path.Points.Add(point.position);
            path.Rotations.Add(point.rotation);
            return;
        }

        Vector3 localPosition = root.InverseTransformPoint(point.position);
        Quaternion localRotation = Quaternion.Inverse(root.rotation) * point.rotation;
        path.Points.Add(anchor.TransformPoint(localPosition));
        path.Rotations.Add(anchor.rotation * localRotation);
    }

    private void SnapTargetToMoverPoint(bool last)
    {
        if (moverTarget == null) return;
        var path = PathFromRootOrSelf(moverRoot != null ? moverRoot : transform, moverPathSpace, moverTarget);
        if (!path.IsValid) return;

        int index = last ? path.Count - 1 : 0;
        moverTarget.position = path.Points[index];
        moverTarget.rotation = path.Rotations[index];
    }

    private void SnapCameraToCameraPoint(bool last)
    {
        ResolveRefs();
        if (camTf == null) return;
        var path = PathFromRootOrSelf(cameraRoot, cameraPathSpace, moverTarget);
        if (!path.IsValid) return;

        int index = last ? path.Count - 1 : 0;
        camTf.position = path.Points[index];
        camTf.rotation = path.Rotations[index];
    }

    protected void ApplyMoverRotation(float progress, float dt)
    {
        if (!faceWaypointRotation || moverTarget == null || moverPath == null || moverPath.Count == 0) return;
        Quaternion target = moverPath.SampleRotation(progress);
        RotateTargetTo(Quaternion.RotateTowards(moverTarget.rotation, target, turnSpeed * dt));
    }

    private void RotateTargetTo(Quaternion rotation)
    {
        if (activeMoveDriver == MoveDriver.Rigidbody && moverRigidbody != null)
        {
            moverRigidbody.MoveRotation(rotation);
            moverTarget.rotation = rotation;
            return;
        }

        moverTarget.rotation = rotation;
    }

    protected void UpdateCamera(float progress)
    {
        if (camTf == null || camPath == null || !camPath.IsValid) return;
        camTf.position = SampleCameraPosition(camPath, progress);
        bool useEndpointRotation = useEndpointRotationOnEnd && progress >= 0.999f;
        if (!useEndpointRotation && TryGetCameraLookAt(out Vector3 lookAt))
        {
            Vector3 to = lookAt - camTf.position;
            if (to.sqrMagnitude > 1e-6f) camTf.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }
        else
        {
            camTf.rotation = camPath.SampleRotation(progress, cameraCuts || useEndpointRotation);
        }
    }

    private Vector3 SampleCameraPosition(CinemaPath path, float progress)
    {
        if (path == null || path.Count == 0) return Vector3.zero;
        if (cameraCuts) return path.SamplePosition(progress, true);
        return SamplePathPosition(path, progress, cameraInterpolation == PathInterpolation.Linear);
    }

    private bool TryGetCameraLookAt(out Vector3 lookAt)
    {
        if (hasLockedCameraLookAt)
        {
            lookAt = lockedCameraLookAtPosition;
            return true;
        }

        if (cameraLookAt != null)
        {
            lookAt = cameraLookAt.position;
            return true;
        }

        lookAt = default;
        return false;
    }

    private List<Vector3> BuildMoverPoints()
    {
        return moverInterpolation == PathInterpolation.Spline
            ? moverPath.SmoothedPoints()
            : new List<Vector3>(moverPath.Points);
    }

    private List<Vector3> BuildMoverTimelinePoints(List<Vector3> pathPoints)
    {
        var points = new List<Vector3>();
        if (moverTarget != null) points.Add(moverTarget.position);
        if (pathPoints != null) points.AddRange(pathPoints);
        return points;
    }

    private static Vector3 SamplePoints(IReadOnlyList<Vector3> points, float progress)
    {
        if (points == null || points.Count == 0) return Vector3.zero;
        if (points.Count == 1) return points[0];

        float u = Mathf.Clamp01(progress) * (points.Count - 1);
        int i = Mathf.Min(Mathf.FloorToInt(u), points.Count - 2);
        return Vector3.Lerp(points[i], points[i + 1], u - i);
    }

    private static Vector3 SamplePathPosition(CinemaPath path, float progress, bool linear)
    {
        if (path == null || path.Count == 0) return Vector3.zero;
        if (!linear) return path.SamplePosition(progress);
        if (path.Count == 1) return path.Points[0];

        float u = Mathf.Clamp01(progress) * (path.Count - 1);
        int i = Mathf.Min(Mathf.FloorToInt(u), path.Count - 2);
        return Vector3.Lerp(path.Points[i], path.Points[i + 1], u - i);
    }

    // ======================================================================
    //  接地 / アニメ / トリガー(組み込み)
    // ======================================================================
    protected virtual float GroundY(Vector3 pos, float fallbackY)
    {
        switch (groundMode)
        {
            case GroundMode.NavMesh:
                if (NavMesh.SamplePosition(pos, out NavMeshHit nav, navSampleRadius, NavMesh.AllAreas))
                    return nav.position.y - footOffset;
                break;
            case GroundMode.Raycast:
                if (Physics.Raycast(pos + Vector3.up * groundRayUp, Vector3.down,
                        out RaycastHit hit, groundRayUp + 200f, groundMask, QueryTriggerInteraction.Ignore))
                    return hit.point.y - footOffset;
                break;
        }
        return fallbackY;
    }

    private void SetupGrounding()
    {
        footOffset = moverCC != null ? moverCC.center.y - moverCC.height * 0.5f - moverCC.skinWidth : 0f;
        // 何を地面とみなすかは Inspector の Ground Layers で指定する(ハードコードしない)。
        groundMask = groundLayers.value;
    }

    private void GroundNow()
    {
        bool was = moverCC != null && moverCC.enabled;
        if (was) moverCC.enabled = false;
        Vector3 p = moverTarget.position;
        p.y = GroundY(p, p.y);
        moverTarget.position = p;
        if (was) moverCC.enabled = true;
    }

    private void DriveAnimator(bool moving)
    {
        if (animator == null) return;
        if (moving && resumeAnimatorWhileMoving && Mathf.Approximately(animator.speed, 0f))
            animator.speed = 1f;
        if (!string.IsNullOrEmpty(resolvedMoveFloatParam))
            animator.SetFloat(resolvedMoveFloatParam, moving ? moveFloatValue : 0f);
        if (!string.IsNullOrEmpty(resolvedMoveBoolParam))
            animator.SetBool(resolvedMoveBoolParam, moving);
    }

    private void ResolveAnimatorParams()
    {
        resolvedMoveFloatParam = ResolveAnimatorParam(moveFloatParam, AnimatorControllerParameterType.Float,
            "MoveSpeed", "Speed", "Move", "Velocity", "Blend");
        resolvedMoveBoolParam = ResolveAnimatorParam(moveBoolParam, AnimatorControllerParameterType.Bool,
            "IsMoving", "Moving", "Move", "Walk", "Run");
    }

    private string ResolveAnimatorParam(string configuredName, AnimatorControllerParameterType type, params string[] candidates)
    {
        if (animator == null) return null;
        if (!string.IsNullOrEmpty(configuredName))
            return HasParam(configuredName, type) ? configuredName : null;
        if (!autoDetectMoveAnimatorParams) return null;

        for (int i = 0; i < candidates.Length; i++)
            if (HasParam(candidates[i], type))
                return candidates[i];
        return null;
    }

    private bool HasParam(string name, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(name)) return false;
        foreach (var p in animator.parameters)
            if (p.type == type && p.name == name) return true;
        return false;
    }

    private void ResetTriggers()
    {
        if (triggers == null) return;
        foreach (var t in triggers) if (t != null) t.fired = false;
    }

    private void FireTriggers(float progress)
    {
        if (triggers == null) return;
        foreach (var t in triggers)
            if (t != null && !t.fired && progress >= t.atProgress) { t.fired = true; t.onReach?.Invoke(); }
    }

    // ======================================================================
    //  hook(必要なときだけ継承で override)
    // ======================================================================
    protected virtual void ResolveRefs()
    {
        if (suspendCameraController == null) suspendCameraController = TPSCameraController.Instance;
        if (suspendCameraController == null) suspendCameraController = FindObjectOfType<TPSCameraController>();
        camTf = suspendCameraController != null ? suspendCameraController.transform
              : (Camera.main != null ? Camera.main.transform : null);
        if (animator == null && moverTarget != null) animator = moverTarget.GetComponentInChildren<Animator>();
    }

    protected virtual void SuspendCamera(bool suspend)
    {
        if (suspendCameraController != null) suspendCameraController.enabled = !suspend;
    }

    protected virtual void OnBegin() { }
    protected virtual void OnEnd() { }
    protected virtual void OnMoverTick(float progress) { }
    protected virtual void OnMoverHold() { }

    // ======================================================================
    //  ギズモ
    // ======================================================================
    protected virtual void OnDrawGizmos()
    {
        var mp = PathFromRootOrSelf(moverRoot != null ? moverRoot : transform, moverPathSpace, moverTarget);
        var cp = PathFromRootOrSelf(cameraRoot, cameraPathSpace, moverTarget);

        if (debugGizmos)
        {
            mp.DrawDebug("T ", new Color(0.3f, 1f, 0.5f), new Color(0.95f, 1f, 0.3f), false,
                         debugArrowLength, connected: moverInterpolation == PathInterpolation.Spline);
            Vector3? lookAt = cameraLookAt != null ? cameraLookAt.position : (Vector3?)null;
            cp.DrawDebug("Cam ", new Color(0.3f, 0.8f, 1f), new Color(1f, 0.4f, 0.5f), true,
                         debugArrowLength, connected: !cameraCuts && cameraInterpolation == PathInterpolation.Spline,
                         lookAt: lookAt);

            if (cameraLookAt != null)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f);
                Gizmos.DrawWireSphere(cameraLookAt.position, 0.2f);
#if UNITY_EDITOR
                UnityEditor.Handles.color = new Color(1f, 0.9f, 0.2f);
                UnityEditor.Handles.Label(cameraLookAt.position + Vector3.up * 0.25f, "LookAt");
#endif
            }
            DrawLiveDirections();
        }
        else
        {
            DrawPathLine(mp, new Color(0.3f, 1f, 0.5f), moverInterpolation);
            DrawPathLine(cp, new Color(0.3f, 0.8f, 1f), cameraInterpolation);
        }
    }

    private static void DrawPathLine(CinemaPath path, Color color, PathInterpolation interpolation)
    {
        if (path == null || path.Count < 2) return;
        if (interpolation == PathInterpolation.Spline)
        {
            path.DrawSpline(color);
            return;
        }

        Gizmos.color = color;
        for (int i = 0; i < path.Count - 1; i++)
            Gizmos.DrawLine(path.Points[i], path.Points[i + 1]);
    }

    private void DrawLiveDirections()
    {
        if (moverTarget != null)
            CinemaPath.DrawArrow(moverTarget.position + Vector3.up * 0.1f,
                moverTarget.position + Vector3.up * 0.1f + moverTarget.forward * debugArrowLength, Color.green);
        if (camTf != null)
            CinemaPath.DrawArrow(camTf.position, camTf.position + camTf.forward * (debugArrowLength * 1.5f), Color.cyan);
    }

#if UNITY_EDITOR
    // ======================================================================
    //  Edit プレビュー(Target と Camera を経路で動かす → 復元)。イベント/アニメ/CC は触らない。
    // ======================================================================
    private bool editPreviewing;
    private int editPhase;
    private double editLastTime;
    private List<Vector3> editDense;
    private float editTotal, editCovered;
    private int editIdx;

    private Vector3 sMoverPos; private Quaternion sMoverRot; private bool sHasMover;
    private Vector3 sCamPos;   private Quaternion sCamRot;   private bool sHasCam;

    [ContextMenu("Preview Track (Edit)")]
    protected void PreviewTrackEdit()
    {
        if (Application.isPlaying) { Play(); return; }
        StartEditPreview();
    }

    [ContextMenu("Stop Track Preview")]
    protected void StopTrackPreviewMenu()
    {
        if (!Application.isPlaying) StopEditPreview();
    }

    protected void StartEditPreview()
    {
        StopEditPreview();
        Setup();
        editDense = moverPath.SmoothedPoints();

        sHasMover = moverTarget != null;
        if (sHasMover) { sMoverPos = moverTarget.position; sMoverRot = moverTarget.rotation; }
        sHasCam = camTf != null;
        if (sHasCam) { sCamPos = camTf.position; sCamRot = camTf.rotation; }

        if (camPath.IsValid && camTf != null)
        {
            LockCameraLookAt();
            SuspendCamera(true);
        }

        editTotal = sHasMover && editDense.Count > 0
            ? CinemaSpline.HorizontalLength(moverTarget.position, editDense) : 0.001f;
        editCovered = 0f; editIdx = 0; editPhase = 0;
        editLastTime = UnityEditor.EditorApplication.timeSinceStartup;
        editPreviewing = true;
        UnityEditor.EditorApplication.update += EditTick;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopEditPreview;
    }

    private void EditTick()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= EditTick; return; }
        if (Application.isPlaying) { StopEditPreview(); return; }

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = Mathf.Clamp((float)(now - editLastTime), 0f, 0.05f);
        editLastTime = now;

        if (editPhase == 0)
        {
            bool moving = moverTarget != null && editIdx < editDense.Count;
            if (moving)
            {
                editCovered += StepMover(editDense, ref editIdx, moveSpeed * dt);
                float prog = editCovered / editTotal;
                ApplyMoverRotation(prog, dt);
                if (camPath != null && camPath.IsValid) UpdateCamera(prog);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            else
            {
                editPhase = 1;
                OnEditWalkComplete();
            }
        }
        else
        {
            if (!EditBusy()) StopEditPreview();
        }
    }

    protected void StopEditPreview()
    {
        UnityEditor.EditorApplication.update -= EditTick;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopEditPreview;
        if (!editPreviewing) return;
        editPreviewing = false;

        if (sHasMover && moverTarget != null) { moverTarget.position = sMoverPos; moverTarget.rotation = sMoverRot; }
        if (sHasCam && camTf != null) { camTf.position = sCamPos; camTf.rotation = sCamRot; }
        SuspendCamera(false);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    protected virtual void OnEditWalkComplete() { }
    protected virtual bool EditBusy() => false;
#endif
}
