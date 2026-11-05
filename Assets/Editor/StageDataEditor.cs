using UnityEditor;
using UnityEngine;

namespace ProjectU.Editor
{
    [CustomEditor(typeof(StageData))]
    public sealed class StageDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!((StageData)target).TryValidate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
