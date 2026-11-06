/*==================================================================================================

    File   : プレイヤー足IK [PlayerFootIK.cs]

    Author : Hasegawa Jin
    Date   : 2026/7/18
    接地時に足を地形へ吸着させる FootIK。リグが Generic (Humanoid ではない) ため
    Unity 標準の OnAnimatorIK は使えず、LateUpdate で解析的 2 ボーン IK を自前で解く。
    - 各足元へ Raycast し、「ルート平面との高低差」ぶんだけ足を上下させる (XZ は動かさない)。
      アニメが足を持ち上げている量 (歩行のスイング等) はそのまま保存されるので、
      平地では見た目がアニメと完全に一致し、足を床へ引きずり下ろさない
    - 低い側の足に届くようモデルルート (Player_Model) ごと沈める (ペルビスオフセット)。
      ※ Hips を沈めると、スケルトン外にある髪グループ (HairBoneFollower 参照) が
        付いてこず頭から浮くため、髪も含む共通親のモデルルートを下げる
    - 足裏を地面法線へ傾ける (角度クランプ付き)
    PlayerLifetimeScope が実行時に AddComponent + Initialize する (Mover と同パターン)。

==================================================================================================*/

using UnityEngine;

// 髪 (HairBoneFollower) やスカート (SkirtDynamicBone) の LateUpdate より先に IK 後の
// ポーズを確定させる (追従系がIK適用前の脚位置を参照して1フレームずれるのを防ぐ)。
[DefaultExecutionOrder(-10)]
public class PlayerFootIK : MonoBehaviour {

    [Header("有効化")]
    [Tooltip("FootIK 全体の ON/OFF (実行中の切り替え可)")]
    public bool ikEnabled = true;

    [Header("レイキャスト")]
    [Tooltip("足首から上方向へこの高さからレイを撃ち始める (m)")]
    public float rayOriginHeight = 0.5f;
    [Tooltip("ルート平面より下方向へ地面を探す最大距離 (m)。これより低い地面へは吸着しない")]
    public float maxStepDown = 0.15f;
    [Tooltip("地面と判定するレイヤー (Player / Weapon は Awake で自動除外)")]
    public LayerMask groundMask = ~0;

    [Header("チューニング")]
    [Tooltip("足首ボーンの足裏からの高さ (m)。モデルの Foot ボーン位置に合わせる")]
    public float footHeight = 0.1f;
    [Tooltip("モデルを沈める最大量 (m)")]
    public float maxPelvisDrop = 0.12f;
    [Tooltip("ペルビスオフセットの追従速度 (大きいほど機敏。大きすぎると腰が跳ねる)")]
    public float pelvisLerpSpeed = 6f;
    [Tooltip("IK ウェイトのフェード速度 (接地/離陸時のポップ防止)")]
    public float weightLerpSpeed = 6f;
    [Tooltip("足裏を地面法線へ傾ける最大角度 (度)")]
    public float maxFootRotation = 8f;
    [Tooltip("足裏の法線合わせを効かせる接地距離 (m)。足がこれ以上浮いていると傾けない")]
    public float footPlantRange = 0.08f;

    // ボーン参照 (Initialize で名前検索して取得)
    private Transform hips;
    private Transform legL, kneeL, footL;
    private Transform legR, kneeR, footR;
    // ペルビスオフセットの適用先 (Hips ではなくモデルルート)。髪グループなどスケルトン外の
    // 子も一緒に沈めるため、Hips から遡ったプレイヤールート直下の Transform を使う。
    private Transform modelRoot;
    private Vector3 modelRootBaseLocalPos;

    private IPlayerContext context;
    private PlayerMover mover;
    private PlayerHitReaction hitReaction;
    private Animator anim;
    private bool dead;

    private float weight;        // 全体ウェイト (接地で1へ、空中で0へフェード)
    private float pelvisOffset;  // 現在の Hips 沈み量 (負値)
    private bool valid;          // ボーンが揃っているか

    /// <summary>PlayerLifetimeScope の Awake から呼ぶ。ボーンを名前検索し、ステート/接地情報の参照を受け取る。</summary>
    public void Initialize(IPlayerContext context) {
        this.context = context;
        mover        = context.Mover;
        hitReaction  = GetComponent<PlayerHitReaction>();
        anim         = GetComponent<Animator>();
        if (context.Health != null) context.Health.onDied.AddListener(() => dead = true);

        // 地面判定の対象から Player と Weapon を除外 (PlayerMover と同じ方針)
        int playerLayer = LayerMask.NameToLayer("Player");
        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (playerLayer >= 0) groundMask &= ~(1 << playerLayer);
        if (weaponLayer >= 0) groundMask &= ~(1 << weaponLayer);

        hips  = FindDeep(transform, "Hips");
        legL  = FindDeep(transform, "Leg_L");
        kneeL = FindDeep(transform, "Knee_L");
        footL = FindDeep(transform, "Foot_L");
        legR  = FindDeep(transform, "Leg_R");
        kneeR = FindDeep(transform, "Knee_R");
        footR = FindDeep(transform, "Foot_R");

        valid = hips && legL && kneeL && footL && legR && kneeR && footR;
        if (!valid) {
            Debug.LogWarning("[PlayerFootIK] 脚ボーン (Hips/Leg/Knee/Foot _L/_R) が見つからないため FootIK を無効化します。", this);
            return;
        }

        // モデルルート = Hips から遡ったプレイヤールート直下の Transform (通常 Player_Model)。
        // Animator はこの Transform 自体を駆動しないので、毎フレーム絶対値で上書きしても安全。
        modelRoot = hips;
        while (modelRoot.parent != null && modelRoot.parent != transform)
            modelRoot = modelRoot.parent;
        modelRootBaseLocalPos = modelRoot.localPosition;
    }

    private void LateUpdate() {
        if (!valid) return;

        // 前フレームのペルビスオフセットを打ち消して純粋なアニメ姿勢に戻す。
        // モデルルートは Animator が書き戻さないため += だと累積する。毎フレーム
        // 「基準位置へ戻す → 計算 → 絶対値で適用」の順にして累積とフィードバックを防ぐ。
        modelRoot.localPosition = modelRootBaseLocalPos;

        // 適用条件の方針:
        //   残す  : 接地ロコモーション (Idle/歩き/走り) のみ
        //   切る  : 攻撃/チャージ (StateAttack)・コンボ (StateComboAttack)、空中 (StateAirborne)、
        //           回避ロール (体が回転し足が地面から離れる)、ノックバック (吹き飛び/壁激突)、
        //           死亡 (倒れポーズに吸着が干渉)
        bool active = ikEnabled && !dead
                   && mover != null && mover.IsGrounded
                   && context.StateMachine != null
                   && context.StateMachine.IsInLocomotionState<StateGrounded>()
                   && !context.StateMachine.IsInActionState<StateRoll>()
                   && !context.StateMachine.IsInActionState<StateAttack>()
                   && !context.StateMachine.IsInActionState<StateComboAttack>()
                   && !IsSuppressedByState();
        float targetWeight = active ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, targetWeight, weightLerpSpeed * Time.deltaTime);

        float scale = Mathf.Max(0.01f, transform.lossyScale.y); // アリス小/巨大のスケール対応

        // ウェイト 0 でもペルビスは元へ戻し続ける
        if (weight <= 0.001f) {
            pelvisOffset = Mathf.Lerp(pelvisOffset, 0f, pelvisLerpSpeed * Time.deltaTime);
            ApplyPelvisOffset(scale);
            return;
        }

        // 1) アニメ後の足位置から各足元の地面を探す (ペルビス適用前の純アニメ姿勢)
        // 基準はルート Transform の高さではなく「キャラ中心直下の地面」。transform.position は
        // CharacterController の接地押し付け等で微妙に上下し続けるため、それを基準にすると
        // 平地の Idle でも高低差が揺れて腰が周期的にバビングする。地面同士の比較なら平地で厳密に 0。
        float rootY = CenterGroundY();
        Vector3 animPosL = footL.position;
        Vector3 animPosR = footR.position;
        bool hitL = RaycastFoot(animPosL, rootY, scale, out RaycastHit groundL);
        bool hitR = RaycastFoot(animPosR, rootY, scale, out RaycastHit groundR);

        // 各足元の地形がルート平面よりどれだけ低いか (0 = 平地/登り。負のみ扱う)
        float deltaL = hitL ? Mathf.Max(Mathf.Min(0f, groundL.point.y - rootY), -maxStepDown * scale) : 0f;
        float deltaR = hitR ? Mathf.Max(Mathf.Min(0f, groundR.point.y - rootY), -maxStepDown * scale) : 0f;

        // 2) ペルビスオフセット: 低い側の地形へ届くようモデルルートごと沈める (上げはしない)。
        //    髪グループ等スケルトン外の子も一緒に動くので、頭と髪がずれない。
        float targetPelvis = Mathf.Max(Mathf.Min(deltaL, deltaR), -maxPelvisDrop * scale) * weight;
        pelvisOffset = Mathf.Lerp(pelvisOffset, targetPelvis, pelvisLerpSpeed * Time.deltaTime);
        ApplyPelvisOffset(scale);

        // 3) 各脚の 2 ボーン IK (目標はペルビス適用前のアニメ位置から作る = 二重に沈めない)
        if (hitL) SolveLeg(legL, kneeL, footL, animPosL, deltaL, groundL, scale);
        if (hitR) SolveLeg(legR, kneeR, footR, animPosR, deltaR, groundR, scale);
    }

    /// <summary>ペルビスオフセットをモデルルートのローカル位置へ絶対値で反映する (world→local 変換のみ)。</summary>
    private void ApplyPelvisOffset(float scale) {
        modelRoot.localPosition = modelRootBaseLocalPos + Vector3.up * (pelvisOffset / scale);
    }

    /// <summary>
    /// ステートマシンに乗らない演出中の IK 抑制。ノックバック (PlayerHitReaction のコルーチン駆動) と、
    /// Animator フラグ駆動のロール/ノックバック系モーションを見る。
    /// </summary>
    private bool IsSuppressedByState() {
        if (hitReaction != null && hitReaction.IsKnockingBack) return true;
        if (anim == null) return false;
        return anim.GetBool(PlayerAnimator.IsRollParam)
            || anim.GetBool(PlayerAnimator.IsKnockBackParam)
            || anim.GetBool(PlayerAnimator.IsKnockBackForWallParam);
    }

    /// <summary>
    /// キャラ中心直下の地面高さ (足元の高低差の基準面)。地面が拾えないときは
    /// ルート位置へフォールバックする (接地中なのでほぼ同じ高さ)。
    /// </summary>
    private float CenterGroundY() {
        float scale = Mathf.Max(0.01f, transform.lossyScale.y);
        Vector3 origin = transform.position + Vector3.up * (rayOriginHeight * scale);
        float dist = (rayOriginHeight + maxStepDown) * scale;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore)
            && hit.collider.CompareTag("Ground"))
            return hit.point.y;
        return transform.position.y;
    }

    /// <summary>
    /// 足首位置の真下の地面 (Ground タグ) を探す。スイングで持ち上がった足でも拾えるよう、
    /// レイの範囲は足の高さではなくルート平面基準 (ルート + rayOriginHeight 〜 ルート - maxStepDown)。
    /// </summary>
    private bool RaycastFoot(Vector3 footPos, float rootY, float scale, out RaycastHit hit) {
        float originY = Mathf.Max(footPos.y, rootY) + rayOriginHeight * scale;
        Vector3 origin = new Vector3(footPos.x, originY, footPos.z);
        float dist = originY - (rootY - maxStepDown * scale);
        if (Physics.Raycast(origin, Vector3.down, out hit, dist, groundMask, QueryTriggerInteraction.Ignore))
            return hit.collider.CompareTag("Ground");
        return false;
    }

    /// <summary>
    /// 片脚の IK。アニメの持ち上げ量を保ったまま地形の高低差 (terrainDelta) ぶんだけ上下させ、
    /// 地面より下にはめり込ませない。足裏の法線合わせは接地している足だけに効かせる。
    /// </summary>
    private void SolveLeg(Transform upper, Transform mid, Transform foot,
                          Vector3 animPos, float terrainDelta, RaycastHit ground, float scale) {
        float groundY = ground.point.y + footHeight * scale;              // めり込み下限
        float targetY = Mathf.Max(animPos.y + terrainDelta, groundY);     // 持ち上げ保存 + 下限クランプ

        Vector3 target = Vector3.Lerp(foot.position, new Vector3(animPos.x, targetY, animPos.z), weight);
        Quaternion footWorldRot = foot.rotation;

        SolveTwoBone(upper, mid, foot, target);

        // 足の向き: 元のワールド回転を維持しつつ、接地中の足だけ地面法線へ傾ける
        // (浮いている足まで傾けると空中で足首が曲がって見えるため、接地距離でフェード)
        float plant = 1f - Mathf.Clamp01((targetY - groundY) / Mathf.Max(0.01f, footPlantRange * scale));
        Quaternion slope = Quaternion.FromToRotation(Vector3.up, ground.normal);
        slope = Quaternion.RotateTowards(Quaternion.identity, slope, maxFootRotation);
        foot.rotation = Quaternion.Slerp(footWorldRot, slope * footWorldRot, weight * plant);
    }

    /*==============================================================================================
      解析的 2 ボーン IK (余弦定理)。現在の膝の曲げ平面を維持するためポールターゲット不要。
    ==============================================================================================*/
    private static void SolveTwoBone(Transform upper, Transform mid, Transform end, Vector3 target) {
        Vector3 a = upper.position;
        Vector3 b = mid.position;
        Vector3 c = end.position;

        float lab = Vector3.Distance(a, b);
        float lbc = Vector3.Distance(b, c);
        float lat = Mathf.Clamp(Vector3.Distance(a, target), 0.01f, lab + lbc - 0.001f);

        // 曲げ平面の軸 (脚が伸び切っている場合は上体基準の外向きへフォールバック)
        Vector3 axis = Vector3.Cross(c - a, b - a);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(c - a, upper.root.forward);
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();

        float lac = Vector3.Distance(a, c);

        // 現在の関節角と、目標距離 lat を満たす関節角 (余弦定理)
        float angUpperCur = CosAngle(lab, lac, lbc);
        float angMidCur   = CosAngle(lab, lbc, lac);
        float angUpperNew = CosAngle(lab, lat, lbc);
        float angMidNew   = CosAngle(lab, lbc, lat);

        upper.rotation = Quaternion.AngleAxis(angUpperNew - angUpperCur, axis) * upper.rotation;
        mid.rotation   = Quaternion.AngleAxis(angMidNew - angMidCur, axis) * mid.rotation;

        // 曲げ調整後のチェーンを目標方向へ向け直す
        Vector3 cNew = end.position;
        if ((cNew - a).sqrMagnitude > 1e-8f)
            upper.rotation = Quaternion.FromToRotation(cNew - a, target - a) * upper.rotation;
    }

    /// <summary>余弦定理: 辺 e1, e2 に挟まれた角 (対辺 opposite) を度で返す。</summary>
    private static float CosAngle(float e1, float e2, float opposite) {
        float cos = Mathf.Clamp((e1 * e1 + e2 * e2 - opposite * opposite) / (2f * e1 * e2), -1f, 1f);
        return Mathf.Acos(cos) * Mathf.Rad2Deg;
    }

    /// <summary>階層を再帰検索して名前一致の Transform を返す。</summary>
    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
