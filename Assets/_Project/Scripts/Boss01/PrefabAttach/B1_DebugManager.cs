using UnityEngine;

/// <summary>
/// ボスのデバッグ情報を OnGUI で表示する。
/// boss フィールドをセットするだけで動く。
/// </summary>
public class B1_DebugManager : MonoBehaviour
{
    [SerializeField] private B1_Boss boss;

    [Header("表示位置")]
    [SerializeField] private float marginLeft = 10f;
    [SerializeField] private float marginTop  = 10f;

    private const float PanelWidth = 260f;
    private const float RowHeight  = 20f;
    private const float BarWidth   = 120f;
    private const float BarHeight  = 14f;
    private const float Padding    = 8f;

    private GUIStyle  boxStyle;
    private GUIStyle  headerStyle;
    private GUIStyle  labelStyle;
    private Texture2D texBg;
    private Texture2D texBar;
    private Texture2D texBarBg;
    private bool      stylesInitialized;
    private B1_TailAttackDebugDrawer tailAttackDrawer;
    private B1_HitboxDebugDrawer     hitboxDrawer;
    private B1_AttackHitbox[]        hitboxes;
    private B1_DebugRangeRings       rangeRings;
    private B1_DebugHitboxWires      hitboxWires;

    private void Awake()
    {
        if (boss == null) boss = GetComponent<B1_Boss>();
        if (GetComponent<B1_RuntimeGizmos>() == null)
            gameObject.AddComponent<B1_RuntimeGizmos>();
        tailAttackDrawer = new B1_TailAttackDebugDrawer();
        hitboxDrawer     = new B1_HitboxDebugDrawer();

        // 子 GO に追加する。同 GO に追加すると SetActive でボスごと無効化されるため
        var rrGO = new GameObject("DebugRangeRings");
        rrGO.transform.SetParent(transform, false);
        rangeRings = rrGO.AddComponent<B1_DebugRangeRings>();

        var hwGO = new GameObject("DebugHitboxWires");
        hwGO.transform.SetParent(transform, false);
        hitboxWires = hwGO.AddComponent<B1_DebugHitboxWires>();
    }

    private void Start()
    {
        // B1_Boss.Awake で B1_AttackHitbox が動的追加されるため Start でキャッシュする
        hitboxes = GetComponentsInChildren<B1_AttackHitbox>(true);
        if (boss?.Settings != null) rangeRings.Setup(boss.Settings);
        hitboxWires.Setup(hitboxes);
    }

    private void Update()
    {
        bool on = boss != null && boss.Settings != null && boss.Settings.DebugEnabled;
        rangeRings.gameObject.SetActive(on && boss.Settings.ShowRangeRings);
        hitboxWires.gameObject.SetActive(on && boss.Settings.ShowHitboxes);
        if (!on) return;

        var s = boss.Settings;
        if (s.ShowRangeRings) rangeRings.Tick(boss.transform.position);
        if (s.ShowHitboxes)   hitboxWires.Tick();
        if (s.ShowTailAttack)
            tailAttackDrawer.Update(boss.ActionController?.CurrentAction as B1_TailAttackAction);
    }

    private void OnDrawGizmos()
    {
        if (boss == null) boss = GetComponent<B1_Boss>();
        if (boss == null || boss.Settings == null || !boss.Settings.DebugEnabled) return;

        hitboxDrawer ??= new B1_HitboxDebugDrawer();
        hitboxes ??= GetComponentsInChildren<B1_AttackHitbox>(true);
        hitboxDrawer.DrawGizmos(hitboxes);
    }

    private void OnGUI()
    {
        if (boss == null || boss.Settings == null || !boss.Settings.DebugEnabled || !boss.Settings.ShowHUD) return;
        EnsureStyles();

        var bb = boss.Blackboard;

        float x = marginLeft;
        float y = marginTop;
        GUI.Box(new Rect(x - Padding, y - Padding, PanelWidth + Padding * 2, EstimateHeight() + Padding * 2), GUIContent.none, boxStyle);

        y = DrawHeader(x, y, "Boss01 Debug");
        y = DrawSeparator(y);

        y = DrawHeader(x, y, "State");
        y = DrawRow(x, y, "StatusState", boss.StateMachine?.CurrentStateName ?? "-");
        y = DrawRow(x, y, "Action",      boss.ActionController?.CurrentActionName ?? "-");
        y = DrawRow(x, y, "Locomotion",  boss.Locomotion?.CurrentMode.ToString() ?? "-");
        y = DrawSeparator(y);

        y = DrawHeader(x, y, "Blackboard");
        y = DrawBar(x, y, "HP",       bb?.HpRatio ?? 0f, new Color(0.85f, 0.1f, 0.1f));
        y = DrawRow(x, y, "Phase",    $"{bb?.CurrentPhase ?? 0}   Enraged: {BoolStr(bb?.IsEnraged ?? false)}");
        y = DrawRow(x, y, "Dist",     $"{bb?.PlayerDistance ?? 0f:F1} m");
        y = DrawRow(x, y, "Angle",    $"{bb?.PlayerAngle ?? 0f:F1}°");
        y = DrawRow(x, y, "Airborne", BoolStr(bb?.IsPlayerAirborne ?? false));
        y = DrawSeparator(y);

        y = DrawHeader(x, y, "Action History");
        y = DrawRow(x, y, "LastGroup", bb?.LastActionGroup.ToString() ?? "-");
        y = DrawRow(x, y, "SameCount", bb?.SameGroupCount.ToString() ?? "-");
    }

    private float DrawHeader(float x, float y, string title)
    {
        GUI.Label(new Rect(x, y, PanelWidth, RowHeight), title, headerStyle);
        return y + RowHeight;
    }

    private float DrawSeparator(float y)
    {
        GUI.DrawTexture(new Rect(marginLeft - Padding, y + 3f, PanelWidth + Padding * 2, 1f), texBarBg);
        return y + 8f;
    }

    private float DrawRow(float x, float y, string label, string val)
    {
        GUI.Label(new Rect(x,         y, 90f,              RowHeight), label + ":", labelStyle);
        GUI.Label(new Rect(x + 90f,   y, PanelWidth - 90f, RowHeight), val,          labelStyle);
        return y + RowHeight;
    }

    private float DrawBar(float x, float y, string label, float ratio, Color barColor)
    {
        GUI.Label(new Rect(x, y, 90f, RowHeight), label + ":", labelStyle);
        float bx = x + 90f;
        float by = y + (RowHeight - BarHeight) * 0.5f;
        GUI.DrawTexture(new Rect(bx, by, BarWidth, BarHeight), texBarBg);
        if (ratio > 0f)
        {
            var prev = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(bx, by, BarWidth * Mathf.Clamp01(ratio), BarHeight), texBar);
            GUI.color = prev;
        }
        GUI.Label(new Rect(bx + BarWidth + 4f, y, 50f, RowHeight), $"{ratio:F2}", labelStyle);
        return y + RowHeight;
    }

    private float EstimateHeight() => RowHeight * 3 + 8f * 3 + RowHeight * 12;

    private static string BoolStr(bool v) => v ? "YES" : "NO";

    private void EnsureStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        texBg    = MakeTex(new Color(0f, 0f, 0f, 0.75f));
        texBar   = MakeTex(Color.white);
        texBarBg = MakeTex(new Color(0.25f, 0.25f, 0.25f, 1f));

        boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = texBg } };
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            normal    = { textColor = new Color(1f, 0.85f, 0.3f) },
            fontSize  = 12,
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal   = { textColor = Color.white },
            fontSize = 11,
        };
    }

    private void OnDestroy()
    {
        Destroy(texBg);
        Destroy(texBar);
        Destroy(texBarBg);
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
