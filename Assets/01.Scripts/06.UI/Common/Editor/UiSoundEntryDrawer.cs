#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.UI.Editor
{
    /// <summary> 카탈로그 목록의 각 칸 제목을 "그룹/동작"으로 보여 준다 </summary>
    [CustomPropertyDrawer(typeof(UiSoundEntry))]
    public sealed class UiSoundEntryDrawer : PropertyDrawer
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string GROUP_FIELD  = "_group";
        private const string ACTION_FIELD = "_action";
        private const string CLIP_FIELD   = "_clip";
        private const string EMPTY_SUFFIX = " (클립 없음)";

        /****************************************
        *                Methods
        ****************************************/

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, new GUIContent(BuildTitle(property)), true);
        }

        private static string BuildTitle(SerializedProperty property)
        {
            SerializedProperty group  = property.FindPropertyRelative(GROUP_FIELD);
            SerializedProperty action = property.FindPropertyRelative(ACTION_FIELD);
            SerializedProperty clip   = property.FindPropertyRelative(CLIP_FIELD);
            if ((group == null) || (action == null) || (clip == null))
            {
                return property.displayName;
            }

            string title = $"{EnumLabel(group)}/{EnumLabel(action)}";
            return (clip.objectReferenceValue == null) ? title + EMPTY_SUFFIX : title;
        }

        private static string EnumLabel(SerializedProperty property)
        {
            int index = property.enumValueIndex;
            string[] names = property.enumDisplayNames;
            return ((index >= 0) && (index < names.Length)) ? names[index] : property.intValue.ToString();
        }
    }
}
#endif
