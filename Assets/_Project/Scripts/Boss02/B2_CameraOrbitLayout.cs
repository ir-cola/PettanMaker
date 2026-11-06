/*=============================================================================

 File   : [B2_CameraOrbitLayout.cs]
 Desc   : 登場カットの旋回カメラ点(Boss2Camera_1..N)を、円周上に自動配置する
          エディタ用ツール。

          水平半径と高さの2つを Inspector で変えるだけで、7点が常に正確な
          円周上へ並び直る。手で1点ずつ動かすと真円が崩れるため、この
          コンポーネントが唯一の調節口になる。

          ■ 使い方
            1. Horizontal Radius / Height を変える
            2. 自動で並び直る(エディタでの変更時)。
               反映されない時は右クリック →「カメラ点を配置」

          ■ 点の作られ方
            namePrefix + 連番 の名前でシーンのルートに作られる。
            B2_QueenIntroCamera 側の「番号付きカメラ点の自動収集」が
            同じ名前規則で拾うので、配置するだけで経路になる。

          ■ 見下ろし角について
            見下ろし角は「高さ」と「水平半径」から自動的に決まる(従属値)。
              角度 = atan((height - center.y) / horizontalRadius)
            角度を直接指定することはできない。角度から逆算したい場合は
            Inspector に表示される現在の角度を見ながら高さ/半径を詰める。
            (実行時は B2_QueenIntroCamera の Camera Look At が毎フレーム
             狙いを強制するため、点の回転そのものは使われない)

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    /// <summary>旋回カメラ点を円周上に並べるエディタツール。</summary>
    public class B2_CameraOrbitLayout : MonoBehaviour
    {
        [Header("注視点(円の中心)")]
        [Tooltip("円の中心。B2_QueenIntroCamera の Camera Look At と揃えること。\n" +
                 "既定 (0, 4.02, 0) は HeartQueen_Model のワールド位置")]
        [SerializeField] private Vector3 centerPosition = new Vector3(0f, 4.02f, 0f);
        [Tooltip("設定するとこの Transform の位置を中心に使う(Center Position より優先)")]
        [SerializeField] private Transform centerOverride;

        [Header("ここを調節する")]
        [Tooltip("中心からの水平距離")]
        [SerializeField] private float horizontalRadius = 15f;
        [Tooltip("カメラの高さ(ワールド Y)")]
        [SerializeField] private float height = 25f;

        [Header("旋回 (回る量 = 速度 × 時間 で自動決定)")]
        [Tooltip("1秒あたり何度回るか。大きいほど速い")]
        [SerializeField, Min(0.1f)] private float degreesPerSecond = 17.5f;
        [Tooltip("旋回にかける時間(秒)。トラックの Duration へも自動で書き込まれる")]
        [SerializeField, Min(0.05f)] private float orbitDuration = 2f;
        [Tooltip("開始角(度)。0 = 中心から見て +Z 方向(プレイヤー側)")]
        [SerializeField] private float startAngle = 0f;
        [Tooltip("点の数。多いほど弧が滑らかになる(スプライン補間と併用するので7もあれば十分)。\n" +
                 "減らすと余った点は自動削除される(名前で自動収集されるため、残すと経路が巻き戻って壊れる)")]
        [SerializeField, Min(2)] private int pointCount = 7;

        [Header("トラックへの反映")]
        [Tooltip("ONなら上の時間を Target Track の Duration へ自動で書き込む。\n" +
                 "OFF にすると Duration を直接いじれる")]
        [SerializeField] private bool driveTrackDuration = true;
        [Tooltip("Duration の書き込み先(旋回トラック = Shot1_Orbit)。未設定なら何も書かない。\n" +
                 "※ 以前は B2_QueenIntroCamera を自動検索していたが、旋回が独立トラックに\n" +
                 "   なった今、それだと名前ショットの尺を壊すため明示参照にした")]
        [SerializeField] private CinemaTrack targetTrack;

        /// <summary>回り込む量(度)。速度×時間から自動で決まる従属値</summary>
        public float SweepAngle => degreesPerSecond * Mathf.Max(0.05f, orbitDuration);

        [Header("点の名前")]
        [Tooltip("B2_QueenIntroCamera の Waypoint Prefix と一致させること")]
        [SerializeField] private string namePrefix = "Boss2Camera_";

        /// <summary>現在の設定での見下ろし角(度)。高さと半径から決まる従属値</summary>
        public float PitchDegrees
        {
            get
            {
                float dy = height - Center.y;
                return horizontalRadius <= 0.001f ? 90f : Mathf.Atan2(dy, horizontalRadius) * Mathf.Rad2Deg;
            }
        }

        private Vector3 Center => centerOverride != null ? centerOverride.position : centerPosition;

        /// <summary>index 番目(0基点)の点のワールド位置。</summary>
        public Vector3 PointAt(int index)
        {
            float step = pointCount > 1 ? SweepAngle / (pointCount - 1) : 0f;
            float a = (startAngle + step * index) * Mathf.Deg2Rad;
            Vector3 c = Center;
            // 上から見て +Z → -X が反時計回り
            return new Vector3(c.x - horizontalRadius * Mathf.Sin(a),
                               height,
                               c.z + horizontalRadius * Mathf.Cos(a));
        }

#if UNITY_EDITOR
        // 数値をいじったら並び直す。オブジェクトの生成/破棄は OnValidate 中に行えないため
        // delayCall へ逃がす(Unity の制約)。
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Layout();
            };
        }

        [ContextMenu("カメラ点を配置")]
        public void Layout()
        {
            if (string.IsNullOrEmpty(namePrefix)) return;

            List<Transform> points = CollectPoints();

            // 足りない分を作る
            for (int i = points.Count; i < pointCount; i++)
            {
                var go = new GameObject(namePrefix + (i + 1));
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create camera point");
                points.Add(go.transform);
            }

            // 余った点は必ず消す。名前で自動収集されるため、残すと古い位置のまま
            // 経路に混ざって「回り切った後に巻き戻る」壊れ方をする。
            for (int i = points.Count - 1; i >= pointCount; i--)
            {
                if (points[i] != null) UnityEditor.Undo.DestroyObjectImmediate(points[i].gameObject);
                points.RemoveAt(i);
            }

            for (int i = 0; i < pointCount && i < points.Count; i++)
            {
                Transform t = points[i];
                UnityEditor.Undo.RecordObject(t, "Layout camera points");
                t.position = PointAt(i);
                // 回転は実行時 Camera Look At に上書きされるが、Scene ビューで
                // 見え方を確認できるよう中心へ向けておく
                Vector3 dir = Center - t.position;
                if (dir.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                UnityEditor.EditorUtility.SetDirty(t);
            }

            if (driveTrackDuration) ApplyDuration();

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        /// <summary>旋回にかかる秒数(=Orbit Duration そのもの)。</summary>
        public float TrackDuration => Mathf.Max(0.05f, orbitDuration);

        // 速度から尺を計算して B2_QueenIntroCamera の Duration へ書く。
        // duration は CinemaTrack の private フィールドなので SerializedObject 経由で触る。
        // (共有クラスである CinemaTrack 側に setter を足さずに済ませるため)
        private void ApplyDuration()
        {
            if (targetTrack == null) return;

            var so = new UnityEditor.SerializedObject(targetTrack);
            var prop = so.FindProperty("duration");
            if (prop == null)
            {
                Debug.LogWarning("[B2_CameraOrbitLayout] CinemaTrack に duration が見つかりません。" +
                                 "Drive Track Duration を OFF にして手動で設定してください。", this);
                return;
            }

            float dur = TrackDuration;
            if (!Mathf.Approximately(prop.floatValue, dur))
            {
                prop.floatValue = dur;
                so.ApplyModifiedProperties();
            }
        }

        // 名前+連番の点を番号順に集める(番号は数値比較なので _2 と _10 が正しく並ぶ)
        private List<Transform> CollectPoints()
        {
            var found = new List<KeyValuePair<int, Transform>>();
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                if (t == null || !t.name.StartsWith(namePrefix)) continue;
                if (int.TryParse(t.name.Substring(namePrefix.Length).Trim(), out int n))
                    found.Add(new KeyValuePair<int, Transform>(n, t));
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            var list = new List<Transform>();
            foreach (var kv in found) list.Add(kv.Value);
            return list;
        }

        // 円と点をシーンビューに描く
        private void OnDrawGizmos()
        {
            Vector3 c = Center;
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(c, 0.3f);

            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            const int steps = 64;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float a = (startAngle + SweepAngle * i / steps) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(c.x - horizontalRadius * Mathf.Sin(a), height, c.z + horizontalRadius * Mathf.Cos(a));
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
            for (int i = 0; i < pointCount; i++)
            {
                Vector3 p = PointAt(i);
                Gizmos.DrawWireSphere(p, 0.35f);
                Gizmos.DrawLine(p, c);
            }
        }
#endif
    }
}
