using UnityEditor;
using UnityEngine;

namespace ProjectU.Editor
{
    [CustomPropertyDrawer(typeof(RewardRarityChances))]
    public sealed class RewardRarityChancesDrawer : PropertyDrawer
    {
        private static readonly string[] Names = { "common", "rare", "epic", "unique" };
        private const float Gap = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            (EditorGUIUtility.singleLineHeight + Gap) * 6f + 42f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            var row = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(row, label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            int total = 0;
            foreach (string name in Names) total += property.FindPropertyRelative(name).intValue;
            foreach (string name in Names)
            {
                row.y += line + Gap;
                SerializedProperty chance = property.FindPropertyRelative(name);
                int others = total - chance.intValue;
                EditorGUI.BeginChangeCheck();
                int value = EditorGUI.IntSlider(row, new GUIContent(ObjectNames.NicifyVariableName(name) + " (%)"),
                    chance.intValue, 0, Mathf.Max(0, 100 - others));
                if (!EditorGUI.EndChangeCheck()) continue;
                chance.intValue = value;
                total = others + value;
            }
            row.y += line + Gap;
            EditorGUI.LabelField(row, "입력 합계", total + "% / 100%");
            row.y += line + Gap;
            row.height = 40f;
            EditorGUI.HelpBox(EditorGUI.IndentedRect(row),
                "합계가 100% 미만이면 입력 비율로 정규화합니다. 모두 0이면 기본값 50 / 30 / 15 / 5로 복구합니다.", MessageType.Info);
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
    }
}
