#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI.Editor
{
    /// <summary> 친구, 길드 창과 바인더, 단축키 토글, HUD 메뉴 버튼, 빨간 점을 마을 출시 씬에 놓고 두 출시 씬의 채팅에 길드, 귓속말 탭을 붙인다 </summary>
    public static class SocialWindowsWiring
    {
        // 이미 놓인 것은 다시 만들지 않는다. 던전 씬에는 창을 두지 않는다(던전은 채팅과 알림만, 사용자 결정)

        /****************************************
        *                Fields
        ****************************************/

        private const string TOWN_SCENE_PATH    = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string DUNGEON_SCENE_PATH = "Assets/00.Scenes/Release/DungeonForest.unity";
        private const string UI_LAYER           = "UI";
        private const string MENU_FRIENDS       = "친구";
        private const string MENU_GUILD         = "길드";
        private const string MENU_TEMPLATE      = "옵션 재설정";
        private const string BADGE_NAME         = "받은 요청 점";
        private const string GUILD_OPTION       = "[길드]";
        private const string GUILD_TAB          = "길드";
        private const string WHISPER_TAB        = "귓속말";
        private const string DOT_SPRITE         = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/T_UiSlot_Empty_Round.png";
        private const int    BASE_TAB_COUNT     = 3;
        private const int    FULL_TAB_COUNT     = 5;
        private const int    BASE_OPTION_COUNT  = 2;
        private const float  POSITION_EPSILON   = 1f;
        private const float  BADGE_SIZE         = 12f;
        private const float  BADGE_INSET        = 6f;

        private const string MENU_SECTION       = "제목 경제";
        private const float  MENU_ROW           = 52f;  // HUD 메뉴 버튼 한 줄 간격
        private const float  MENU_LEFT_X        = 24f;
        private const float  MENU_RIGHT_X       = 276f;

        private static readonly Color G_BadgeColor = new Color(0.93f, 0.26f, 0.26f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/친구, 길드창 만들고 출시 씬에 연결")]
        public static void BuildAndWire()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Play Mode에서는 출시 씬을 변경하지 않습니다.");
            }

            FriendsWindowBuilder.Build();
            GuildWindowBuilder.Build();
            WireTownScene();
            WireDungeonScene();
        }

        /// <summary> 마을 출시 씬에 두 창, 바인더, 단축키 토글, 메뉴 버튼, 빨간 점, 채팅 탭을 놓는다 </summary>
        public static void WireTownScene()
        {
            GameObject friendsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FriendsWindowBuilder.PREFAB_PATH);
            GameObject guildPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(GuildWindowBuilder.PREFAB_PATH);
            if ((friendsPrefab == null) || (guildPrefab == null))
            {
                throw new InvalidOperationException("친구, 길드창 프리팹이 없습니다. 먼저 창을 만드세요");
            }

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(TOWN_SCENE_PATH, OpenSceneMode.Single);
            HudChatView chat = Single<HudChatView>();
            AddChatTabs(chat);

            if (UnityEngine.Object.FindObjectsByType<FriendsBinder>(FindObjectsInactive.Include).Length == 0)
            {
                CharDetailBinder anchor = Single<CharDetailBinder>();
                HotkeyRouter     router = Single<HotkeyRouter>();
                HudMenuDropdown  menu   = Single<HudMenuDropdown>();
                Canvas           canvas = CreateCanvas();

                GameObject friendsWindow = (GameObject)PrefabUtility.InstantiatePrefab(friendsPrefab, canvas.transform);
                friendsWindow.name = "FriendWindow";
                GameObject guildWindow = (GameObject)PrefabUtility.InstantiatePrefab(guildPrefab, canvas.transform);
                guildWindow.name = "GuildWindow";

                GameObject bindingObject = anchor.gameObject;
                HotkeyWindowToggle friendsToggle = AddToggle(bindingObject, router, friendsWindow, EHotkeyAction.Friends);
                HotkeyWindowToggle guildToggle   = AddToggle(bindingObject, router, guildWindow, EHotkeyAction.Guild);

                FriendsBinder friends = bindingObject.AddComponent<FriendsBinder>();
                SocialWindowKit.SetReference(friends, "_view", friendsWindow.GetComponent<FriendsView>());
                SocialWindowKit.SetReference(friends, "_toggle", friendsToggle);
                SocialWindowKit.SetReference(friends, "_chat", chat);
                GuildBinder guild = bindingObject.AddComponent<GuildBinder>();
                SocialWindowKit.SetReference(guild, "_view", guildWindow.GetComponent<GuildView>());
                SocialWindowKit.SetReference(guild, "_toggle", guildToggle);

                var panel = (RectTransform)new SerializedObject(menu).FindProperty("_panel").objectReferenceValue;
                float row = OpenMenuRow(panel);
                Button friendsButton = AddMenuButton(panel, MENU_FRIENDS, new Vector2(MENU_LEFT_X, row), friendsToggle);
                Button guildButton   = AddMenuButton(panel, MENU_GUILD, new Vector2(MENU_RIGHT_X, row), guildToggle);
                SocialNoticeBinder notices = bindingObject.AddComponent<SocialNoticeBinder>();
                SocialWindowKit.SetReference(notices, "_friendBadge", AddBadge(friendsButton));
                SocialWindowKit.SetReference(notices, "_guildBadge", AddBadge(guildButton));
            }
            else
            {
                RelinkWindows();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 친구, 길드창과 채팅 탭을 마을 씬에 연결했다");
        }

        // 빌더가 프리팹을 통째로 다시 쓰면 씬 인스턴스 안 개체의 번호가 바뀌어 바인더와 토글의 참조가 끊길 수 있다. 다시 실행할 때 잇는다
        private static void RelinkWindows()
        {
            FriendsView   friendsView = Single<FriendsView>();
            GuildView     guildView   = Single<GuildView>();
            FriendsBinder friends     = Single<FriendsBinder>();
            GuildBinder   guild       = Single<GuildBinder>();
            SocialWindowKit.SetReference(friends, "_view", friendsView);
            SocialWindowKit.SetReference(guild, "_view", guildView);
            foreach (HotkeyWindowToggle toggle in friends.GetComponents<HotkeyWindowToggle>())
            {
                int action = new SerializedObject(toggle).FindProperty("_action").intValue;
                if (action == (int)EHotkeyAction.Friends)
                {
                    SocialWindowKit.SetReference(toggle, "_window", friendsView.gameObject);
                }
                else if (action == (int)EHotkeyAction.Guild)
                {
                    SocialWindowKit.SetReference(toggle, "_window", guildView.gameObject);
                }
            }
        }

        /// <summary> 던전 출시 씬에는 채팅 탭과 알림 바인더만 붙인다. 빨간 점은 메뉴 버튼이 없어 두지 않는다 </summary>
        public static void WireDungeonScene()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(DUNGEON_SCENE_PATH, OpenSceneMode.Single);
            HudChatView chat = Single<HudChatView>();
            AddChatTabs(chat);
            if (UnityEngine.Object.FindObjectsByType<SocialNoticeBinder>(FindObjectsInactive.Include).Length == 0)
            {
                chat.gameObject.AddComponent<SocialNoticeBinder>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 던전 씬 채팅에 길드, 귓속말 탭과 소셜 알림을 연결했다");
        }

        private static T Single<T>() where T : Component
        {
            T[] found = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            if (found.Length == 0)
            {
                throw new InvalidOperationException(typeof(T).Name + "이 씬에 없습니다.");
            }

            return found[0];
        }

        // 캐릭터 상세창 캔버스 설정을 따르고 정렬은 이미 놓인 창 캔버스보다 하나 위에 둔다
        private static Canvas CreateCanvas()
        {
            CharDetailView view = Single<CharDetailView>();
            Canvas reference = view.GetComponentInParent<Canvas>(true);
            CanvasScaler referenceScaler = (reference != null) ? reference.GetComponent<CanvasScaler>() : null;
            if ((reference == null) || (referenceScaler == null))
            {
                throw new InvalidOperationException("캐릭터 상세창의 캔버스 설정을 찾지 못했습니다");
            }

            int sortingOrder = reference.sortingOrder;
            foreach (SkillWindowView skill in UnityEngine.Object.FindObjectsByType<SkillWindowView>(FindObjectsInactive.Include))
            {
                Canvas skillCanvas = skill.GetComponentInParent<Canvas>(true);
                if (skillCanvas != null)
                {
                    sortingOrder = Mathf.Max(sortingOrder, skillCanvas.sortingOrder);
                }
            }

            GameObject canvasObject = new GameObject("SocialWindowCanvas", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode   = reference.renderMode;
            canvas.sortingOrder = sortingOrder + 1;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = referenceScaler.uiScaleMode;
            scaler.referenceResolution = referenceScaler.referenceResolution;
            scaler.screenMatchMode     = referenceScaler.screenMatchMode;
            scaler.matchWidthOrHeight  = referenceScaler.matchWidthOrHeight;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static HotkeyWindowToggle AddToggle(GameObject host, HotkeyRouter router, GameObject window, EHotkeyAction action)
        {
            HotkeyWindowToggle toggle = host.AddComponent<HotkeyWindowToggle>();
            SocialWindowKit.SetReference(toggle, "_router", router);
            SocialWindowKit.SetReference(toggle, "_window", window);
            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("_action").intValue = (int)action;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return toggle;
        }

        // 캐릭터 묶음 맨 아래 줄 밑에 새 줄을 열고, 그 아래 묶음(경제, 시스템)을 한 줄 내린다. 연 줄의 y를 돌려준다
        private static float OpenMenuRow(RectTransform panel)
        {
            Button template = FindButton(panel, MENU_TEMPLATE);
            if (template == null)
            {
                throw new InvalidOperationException("HUD 메뉴의 옵션 재설정 버튼을 찾지 못했습니다");
            }

            Transform     body    = template.transform.parent;
            RectTransform section = null;
            foreach (Transform child in body)
            {
                if (child.name == MENU_SECTION)
                {
                    section = (RectTransform)child;
                }
            }

            if (section == null)
            {
                throw new InvalidOperationException("HUD 메뉴의 경제 제목을 찾지 못했습니다");
            }

            float sectionTop = section.anchoredPosition.y;
            float lowest     = float.MaxValue;
            foreach (Transform child in body)
            {
                float y = ((RectTransform)child).anchoredPosition.y;
                if ((child.GetComponent<Button>() != null) && (y > sectionTop) && (y < lowest))
                {
                    lowest = y;
                }
            }

            if (lowest == float.MaxValue)
            {
                throw new InvalidOperationException("HUD 메뉴의 캐릭터 묶음 버튼을 찾지 못했습니다");
            }

            foreach (Transform child in body)
            {
                var rect = (RectTransform)child;
                if (rect.anchoredPosition.y <= sectionTop)
                {
                    rect.anchoredPosition += new Vector2(0f, -MENU_ROW);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                }
            }

            return lowest - MENU_ROW;
        }

        private static Button AddMenuButton(RectTransform panel, string name, Vector2 position, HotkeyWindowToggle toggle)
        {
            Button template = FindButton(panel, MENU_TEMPLATE);
            if (template == null)
            {
                throw new InvalidOperationException("HUD 메뉴의 옵션 재설정 버튼을 찾지 못했습니다");
            }

            Button existing = FindButton(panel, name);
            if (existing != null)
            {
                return existing;
            }

            Transform body = template.transform.parent;
            foreach (Transform child in body)
            {
                if (Vector2.Distance(((RectTransform)child).anchoredPosition, position) < POSITION_EPSILON)
                {
                    throw new InvalidOperationException("HUD 메뉴의 " + name + " 자리에 이미 " + child.name + "이 있습니다");
                }
            }

            Button button = UnityEngine.Object.Instantiate(template, body);
            button.name = name;
            ((RectTransform)button.transform).anchoredPosition = position;
            button.GetComponentInChildren<TMP_Text>(true).text = name;
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            UnityEventTools.AddPersistentListener(button.onClick, toggle.Toggle);
            button.gameObject.SetActive(true);
            button.interactable = true;
            EditorUtility.SetDirty(button);
            return button;
        }

        // 버튼 오른쪽 위 작은 빨간 점. 받은 요청이 있을 때만 SocialNoticeBinder가 켠다
        private static GameObject AddBadge(Button button)
        {
            Transform found = button.transform.Find(BADGE_NAME);
            if (found != null)
            {
                return found.gameObject;
            }

            var dot = new GameObject(BADGE_NAME, typeof(RectTransform));
            dot.layer = LayerMask.NameToLayer(UI_LAYER);
            var rect = (RectTransform)dot.transform;
            rect.SetParent(button.transform, false);
            rect.anchorMin        = Vector2.one;
            rect.anchorMax        = Vector2.one;
            rect.pivot            = Vector2.one;
            rect.anchoredPosition = new Vector2(-BADGE_INSET, -BADGE_INSET);
            rect.sizeDelta        = new Vector2(BADGE_SIZE, BADGE_SIZE);
            Image image = dot.AddComponent<Image>();
            image.sprite         = AssetDatabase.LoadAssetAtPath<Sprite>(DOT_SPRITE);
            image.color          = G_BadgeColor;
            image.raycastTarget  = false;
            image.preserveAspect = true;
            dot.SetActive(false);
            return dot;
        }

        // 시스템 탭을 복제해 길드, 귓속말 탭을 끝에 붙인다. 탭 사이 간격은 첫 두 탭의 간격을 따른다
        private static void AddChatTabs(HudChatView chat)
        {
            var data = new SerializedObject(chat);
            SerializedProperty tabs       = data.FindProperty("_tabs");
            SerializedProperty labels     = data.FindProperty("_tabLabels");
            SerializedProperty underlines = data.FindProperty("_underlines");
            if (tabs.arraySize >= FULL_TAB_COUNT)
            {
                AddGuildOption(data);
                return;
            }

            if (tabs.arraySize != BASE_TAB_COUNT)
            {
                throw new InvalidOperationException("채팅 탭이 전체, 파티, 시스템 3개가 아닙니다");
            }

            var first  = (Button)tabs.GetArrayElementAtIndex(0).objectReferenceValue;
            var second = (Button)tabs.GetArrayElementAtIndex(1).objectReferenceValue;
            var last   = (Button)tabs.GetArrayElementAtIndex(BASE_TAB_COUNT - 1).objectReferenceValue;
            var lastLabel     = (TMP_Text)labels.GetArrayElementAtIndex(BASE_TAB_COUNT - 1).objectReferenceValue;
            var lastUnderline = (GameObject)underlines.GetArrayElementAtIndex(BASE_TAB_COUNT - 1).objectReferenceValue;
            Vector2 step = ((RectTransform)second.transform).anchoredPosition - ((RectTransform)first.transform).anchoredPosition;

            string[] names = { GUILD_TAB, WHISPER_TAB };
            tabs.arraySize       = FULL_TAB_COUNT;
            labels.arraySize     = FULL_TAB_COUNT;
            underlines.arraySize = FULL_TAB_COUNT;
            for (int i = 0; i < names.Length; ++i)
            {
                Button clone = UnityEngine.Object.Instantiate(last, last.transform.parent);
                clone.name = "Tab_" + ((i == 0) ? "Guild" : "Whisper");
                ((RectTransform)clone.transform).anchoredPosition = ((RectTransform)last.transform).anchoredPosition + (step * (i + 1));
                while (clone.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(clone.onClick, 0);
                }

                TMP_Text label = Counterpart(last.transform, lastLabel, clone.transform);
                if (label == null)
                {
                    label = clone.GetComponentInChildren<TMP_Text>(true);
                }

                label.text = names[i];
                GameObject underline = Counterpart(last.transform, lastUnderline, clone.transform);
                if ((underline == null) && (lastUnderline != null))
                {
                    underline = UnityEngine.Object.Instantiate(lastUnderline, lastUnderline.transform.parent);
                    ((RectTransform)underline.transform).anchoredPosition =
                        ((RectTransform)lastUnderline.transform).anchoredPosition + (step * (i + 1));
                }

                tabs.GetArrayElementAtIndex(BASE_TAB_COUNT + i).objectReferenceValue       = clone;
                labels.GetArrayElementAtIndex(BASE_TAB_COUNT + i).objectReferenceValue     = label;
                underlines.GetArrayElementAtIndex(BASE_TAB_COUNT + i).objectReferenceValue = underline;
            }

            data.ApplyModifiedPropertiesWithoutUndo();
            AddGuildOption(data);
        }

        private static void AddGuildOption(SerializedObject chatData)
        {
            var dropdown = (TMP_Dropdown)chatData.FindProperty("_channel").objectReferenceValue;
            if ((dropdown == null) || (dropdown.options.Count != BASE_OPTION_COUNT))
            {
                return;
            }

            dropdown.options.Add(new TMP_Dropdown.OptionData(GUILD_OPTION));
            EditorUtility.SetDirty(dropdown);
        }

        // source 아래에 있는 target을 복제본에서 같은 경로로 찾는다. source 밖이면 null
        private static T Counterpart<T>(Transform source, T target, Transform clone) where T : UnityEngine.Object
        {
            Transform targetTransform = (target is Component component) ? component.transform
                : (target is GameObject gameObject) ? gameObject.transform : null;
            if (targetTransform == null)
            {
                return null;
            }

            var path = new List<string>();
            Transform cursor = targetTransform;
            while ((cursor != null) && (cursor != source))
            {
                path.Insert(0, cursor.name);
                cursor = cursor.parent;
            }

            if (cursor != source)
            {
                return null;
            }

            Transform found = (path.Count == 0) ? clone : clone.Find(string.Join("/", path));
            if (found == null)
            {
                return null;
            }

            if (typeof(T) == typeof(GameObject))
            {
                return found.gameObject as T;
            }

            return found.GetComponent(typeof(T)) as T;
        }

        private static Button FindButton(Transform parent, string name)
        {
            foreach (Button button in parent.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }

            return null;
        }
    }
}
#endif
