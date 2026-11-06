using UnityEngine;

/// <summary>
/// ゲームプレイ中にボスの内部状態をオーバーレイ表示するデバッグUI。
/// B1_Boss を持つ GameObject にアタッチし、Inspector で boss をセットする。
/// </summary>
public class B1_DebugGUI : MonoBehaviour
{
    [SerializeField] private B1_Boss boss;

    [Header("表示位置")]
    [SerializeField] private float marginLeft = 10f;
    [SerializeField] private float marginTop  = 10f;

    private const float PanelWidth  = 260f;
    private const float RowHeight   = 20f;
    private const float BarWidth    = 120f;
    private const float BarHeight   = 14f;
    private const float Padding     = 8f;

    private GUIStyle boxStyle;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private Texture2D texBg;
    private Texture2D texBar;
    private Texture2D texBarBg;
    private bool stylesInitialized;

    private void OnGUI()
    {
        if (boss == null) return;
        EnsureStyles();

        float x = marginLeft;
        float y = marginTop;
        float totalHeight = EstimateHeight();

        GUI.Box(new Rect(x - Padding, y - Padding, PanelWidth + Padding * 2, totalHeight + Padding * 2), GUIContent.none, boxStyle);

        y = DrawHeader(x, y, "Boss01 Debug");
        y = DrawSeparator(y);

        // ---- StatusState ----
        y = DrawHeader(x, y, "State");
        y = DrawRow(x, y, "StatusState", boss.StateMachine?.CurrentStateName ?? "-");
        y = DrawRow(x, y, "Action",      boss.ActionController?.CurrentActionName ?? "-");
        y = DrawRow(x, y, "Locomotion",  boss.Locomotion?.CurrentMode.ToString() ?? "-");
        y = DrawSeparator(y);

        // ---- Blackboard ----
        y = DrawHeader(x, y, "Blackboard");

        float hpRatio  = GetFloat("HpRatio");
        int   phase    = GetInt("CurrentPhase");
        bool  enraged  = GetBool("IsEnraged");
        float dist     = GetFloat("PlayerDistance");
        float angle    = GetFloat("PlayerAngle");
        bool  airborne = GetBool("IsPlayerAirborne");

        y = DrawBar(x, y, "HP",    hpRatio, new Color(0.85f, 0.1f, 0.1f));
        y = DrawRow(x, y, "Phase", $"{phase}   Enraged: {BoolStr(enraged)}");
        y = DrawRow(x, y, "Dist",  $"{dist:F1} m");
        y = DrawRow(x, y, "Angle",   $"{angle:F1}°");
        y = DrawRow(x, y, "Airborne", BoolStr(airborne));
        y = DrawSeparator(y);

        // ---- 行動履歴 ----
        y = DrawHeader(x, y, "Action History");
        string lastGroup  = GetObj("LastActionGroup")?.ToString() ?? "-";
        int    sameCount  = GetInt("SameGroupCount");
        y = DrawRow(x, y, "LastGroup", lastGroup);
        y = DrawRow(x, y, "SameCount", sameCount.ToString());
    }

    // ---- 描画ヘルパー ----

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

    private float DrawRow(float x, float y, string label, string value)
    {
        GUI.Label(new Rect(x, y, 90f, RowHeight), label + ":", labelStyle);
        GUI.Label(new Rect(x + 90f, y, PanelWidth - 90f, RowHeight), value, labelStyle);
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

    private float EstimateHeight()
    {
        // Header×3 + Separator×3 + Row×12
        return RowHeight * 3 + 8f * 3 + RowHeight * 12;
    }

    private static string BoolStr(bool v) => v ? "YES" : "NO";

    // ---- B1_Blackboard へのリフレクションアクセス（デバッグ専用） ----

    private System.Reflection.PropertyInfo[] bbProps;
    private IBlackboard bbInstance;

    private IBlackboard GetBBViaReflection()
    {
        if (bbInstance != null) return bbInstance;
        // B1_Boss の private bb フィールドを取得
        var field = typeof(B1_Boss).GetField("bb",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        bbInstance = field?.GetValue(boss) as IBlackboard;
        return bbInstance;
    }

    private float GetFloat(string propName)
    {
        var bb = GetBBViaReflection();
        if (bb == null) return 0f;
        var prop = typeof(IBlackboard).GetProperty(propName);
        return prop != null ? (float)prop.GetValue(bb) : 0f;
    }

    private int GetInt(string propName)
    {
        var bb = GetBBViaReflection();
        if (bb == null) return 0;
        var prop = typeof(IBlackboard).GetProperty(propName);
        return prop != null ? (int)prop.GetValue(bb) : 0;
    }

    private bool GetBool(string propName)
    {
        var bb = GetBBViaReflection();
        if (bb == null) return false;
        var prop = typeof(IBlackboard).GetProperty(propName);
        return prop != null && (bool)prop.GetValue(bb);
    }

    private object GetObj(string propName)
    {
        var bb = GetBBViaReflection();
        if (bb == null) return null;
        var prop = typeof(IBlackboard).GetProperty(propName);
        return prop?.GetValue(bb);
    }

    // ---- スタイル初期化 ----

    private void EnsureStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        texBg    = MakeTex(new Color(0f, 0f, 0f, 0.75f));
        texBar   = MakeTex(Color.white);
        texBarBg = MakeTex(new Color(0.25f, 0.25f, 0.25f, 1f));

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = texBg }
        };

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
        if (texBg    != null) Destroy(texBg);
        if (texBar   != null) Destroy(texBar);
        if (texBarBg != null) Destroy(texBarBg);
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
