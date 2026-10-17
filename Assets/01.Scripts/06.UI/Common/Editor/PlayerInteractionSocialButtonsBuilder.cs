#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 상호작용 패널 프리팹의 다른 유저 패널에 친구 요청, 길드 초대 버튼을 붙이고 연결한다 </summary>
    public static class PlayerInteractionSocialButtonsBuilder
    {
        // 귓속말 버튼을 복제해 같은 모양을 쓴다. 다시 실행하면 이미 있는 버튼을 다시 놓고 연결만 고친다.
        // 버튼은 위 기준으로 32씩 쌓여 있어 맨 아래 버튼 밑에 이어 붙이고 패널 높이를 그만큼 늘린다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_PlayerInteractionPanel.prefab";

        private const string FRIEND_LABEL = "친구 요청";
        private const string GUILD_LABEL  = "길드 초대";

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/상호작용 패널 소셜 버튼 붙이기")]
        public static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                var opener = root.GetComponentInChildren<PlayerInteractionPanelOpener>(true);
                if (opener == null)
                {
                    Debug.LogError("[UI] 상호작용 패널 프리팹에 PlayerInteractionPanelOpener가 없다");
                    return;
                }

                var data    = new SerializedObject(opener);
                var other   = (RectTransform)data.FindProperty("_otherPanel").objectReferenceValue;
                var whisper = (Button)data.FindProperty("_whisperButton").objectReferenceValue;
                if ((other == null) || (whisper == null))
                {
                    Debug.LogError("[UI] 다른 유저 패널이나 귓속말 버튼 연결이 없다");
                    return;
                }

                Button friend = Ensure(other, whisper, FRIEND_LABEL);
                Button guild  = Ensure(other, whisper, GUILD_LABEL);
                float  bottom = LowestBottom(other, friend, guild);
                float  step   = ((RectTransform)whisper.transform).sizeDelta.y;
                Place(friend, bottom);
                Place(guild, bottom - step);
                other.sizeDelta = new Vector2(other.sizeDelta.x, -(bottom - (step * 2f)));

                data.FindProperty("_friendRequestButton").objectReferenceValue = friend;
                data.FindProperty("_guildInviteButton").objectReferenceValue   = guild;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                Debug.Log("[UI] 상호작용 패널에 친구 요청, 길드 초대 버튼을 붙였다: " + PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Button Ensure(RectTransform panel, Button template, string label)
        {
            Transform found = panel.Find(label);
            if ((found != null) && (found.TryGetComponent(out Button existing)))
            {
                return existing;
            }

            GameObject copy = Object.Instantiate(template.gameObject, panel, false);
            copy.name = label;
            Button button = copy.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = label;
            }

            return button;
        }

        // 새로 붙이는 두 버튼을 뺀 패널 자식 중 가장 아래 끝(위 기준 음수 y)
        private static float LowestBottom(RectTransform panel, Button skipA, Button skipB)
        {
            float bottom = 0f;
            for (int i = 0; i < panel.childCount; ++i)
            {
                var child = (RectTransform)panel.GetChild(i);
                if ((child == skipA.transform) || (child == skipB.transform))
                {
                    continue;
                }

                bottom = Mathf.Min(bottom, child.anchoredPosition.y - child.sizeDelta.y);
            }

            return bottom;
        }

        private static void Place(Button button, float top)
        {
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, top);
            rect.SetAsLastSibling();
        }
    }
}
#endif
