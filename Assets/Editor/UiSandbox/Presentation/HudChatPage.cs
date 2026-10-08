using Shinshi.Network;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary>
    /// 채팅 HUD. 입력 칸 · 단축키 · 채널 드롭다운을 연결하지 않은 시험 전용 배치를 Play Mode에서만 만들고 전역 ChatLog에 줄을 넣는다.
    /// ChatLog는 전역 정적 상태라 시작 시 기존 줄이 하나라도 있으면 쓰기를 거절하고, 이 페이지가 넣은 줄만 있을 때에만 Detach에서 비운다
    /// </summary>
    public sealed class HudChatPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.chat";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_HUD.unity";

        public const string KEY_TAB      = "hud.chat.tab";
        public const string KEY_CHANNEL  = "hud.chat.channel";
        public const string KEY_SENDER   = "hud.chat.sender";
        public const string KEY_TEXT     = "hud.chat.text";
        public const string KEY_SYSTEM   = "hud.chat.systemText";
        public const string KEY_SAMPLES  = "hud.chat.seedSamples";

        public const string OP_SEND = "chat.send";

        public const string NOTE_NOT_PLAYING = "Play Mode가 아니라 채팅 배치와 로그 쓰기를 모두 끄고 있다 (Play에서만 만든다)";
        public const string NOTE_EXISTING    = "ChatLog에 이미 줄이 있어 전역 로그를 보호하려고 로컬 추가 · 지우기를 끈다";
        public const string NOTE_FOREIGN     = "다른 곳에서 ChatLog가 바뀌어 이 페이지가 넣은 줄과 구분할 수 없다. 로그를 지우지 않고 쓰기를 멈춘다";

        private const int    TAB_COUNT = 3;
        private const string LABEL_CHAT = "채팅";

        private HudChatView _chat;
        private bool        _attachedPlaying;
        private bool        _logWritable;
        private bool        _foreign;
        private int         _expectedCount;
        private string      _logNote = NOTE_NOT_PLAYING;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 채팅 (Play 전용 · 로컬 로그만)"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "HudChatView는 Dev_HUD 씬에 없고, 입력 칸이 연결된 View는 ChatClient로 서버에 송신하므로 쓰지 않는다. " +
                       "Play Mode에서만 입력 칸 · 단축키 · 채널 드롭다운이 없는 시험 배치를 만들어 탭 거르기와 색을 확인한다. " +
                       "전역 ChatLog는 시작 시 비어 있을 때만 쓰며, 원래 줄이 있으면 쓰기를 끈다. " +
                       "Play가 끝났거나 도메인 리로드가 꺼진 경우 정적 로그가 남을 수 있어 다음 Attach/Detach에서 이 페이지가 넣은 줄만 비운다. " +
                       PresentationNotes.NETWORK_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudChatView ChatView
        {
            get { return _chat; }
        }

        /// <summary> 이 페이지가 ChatLog에 쓸 수 있는 상태인지. Play이고 시작 시 로그가 비어 있었고 외부 변경이 없을 때만 참 </summary>
        public bool LogWritable
        {
            get { return (_logWritable) && (_chat != null) && (!_foreign); }
        }

        public bool AddedLines
        {
            get { return (_logWritable) && (_expectedCount > 0); }
        }

        public string LogNote
        {
            get { return _logNote; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override bool NeedsReattach(UiSandboxContext context)
        {
            return (!IsRefused) && (context.IsPlaying != _attachedPlaying);
        }

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_TAB, (int)EChatTab.All);
            scenario.SeedInt(KEY_CHANNEL, (int)EChatChannel.Party);
            scenario.SeedString(KEY_SENDER, "모의 동료");
            scenario.SeedString(KEY_TEXT, "안녕하세요");
            scenario.SeedString(KEY_SYSTEM, "던전에 입장했습니다");
            scenario.SeedBool(KEY_SAMPLES, true);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _attachedPlaying = context.IsPlaying;
            _logWritable     = false;
            _foreign         = false;
            _expectedCount   = 0;
            _logNote         = NOTE_NOT_PLAYING;

            if (!context.IsPlaying)
            {
                return;
            }

            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.BottomLeft)
                .WithOffset(40.0f, 40.0f)
                .WithLength("_tabs", TAB_COUNT)
                .WithLength("_tabLabels", TAB_COUNT)
                .WithLength("_underlines", TAB_COUNT)
                .Skip("_input")
                .Skip("_router")
                .Skip("_channel");
            _chat = CreateFixture<HudChatView>(LABEL_CHAT, options);
            if (_chat == null)
            {
                return;
            }

            if (ChatLog.Entries.Count > 0)
            {
                _logNote = NOTE_EXISTING;
                return;
            }

            _logWritable = true;
            _logNote     = string.Empty;

            if (context.Scenario.GetBool(KEY_SAMPLES))
            {
                Mutate(() =>
                {
                    ChatLog.AddMessage(EChatChannel.All, "모의 유저", "전체 채팅 예시");
                    ChatLog.AddMessage(EChatChannel.Party, "모의 동료", "파티 채팅 예시");
                    ChatLog.AddSystem("시스템 안내 예시");
                });
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_chat == null)
            {
                return;
            }

            _chat.SelectTab((EChatTab)context.Scenario.GetInt(KEY_TAB));
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (!string.IsNullOrEmpty(_logNote))
            {
                EditorGUILayout.HelpBox(_logNote, MessageType.Warning);
            }

            DrawHeader("탭");
            DrawEnum(scenario, KEY_TAB, "활성 탭", EChatTab.All);
            EditorGUILayout.LabelField("View 활성 탭", (_chat != null) ? _chat.ActiveTab.ToString() : "-");

            DrawHeader("로컬 로그 추가 (Play 전용)");
            DrawEnum(scenario, KEY_CHANNEL, "채널", EChatChannel.Party);
            DrawString(scenario, KEY_SENDER, "보낸 사람");
            DrawString(scenario, KEY_TEXT, "내용");
            DrawBool(scenario, KEY_SAMPLES, "처음 열 때 예시 줄 채우기");
            DrawString(scenario, KEY_SYSTEM, "시스템 문구");

            EditorGUI.BeginDisabledGroup(!LogWritable);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("줄 추가"))
            {
                TryAddLine();
            }

            if (GUILayout.Button("송신 (모의 요청)"))
            {
                TrySend();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("시스템 줄 추가"))
            {
                TryAddSystemLine();
            }

            if (GUILayout.Button("로그 지우기"))
            {
                TryClearLog();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
        }

        protected override void OnDetach()
        {
            // 이 페이지가 넣은 줄만 있다고 확인될 때만 비운다. 전역 로그에 남의 줄이 섞였으면 건드리지 않는다
            if ((_logWritable) && (!_foreign) && (_expectedCount > 0) && (ChatLog.Entries.Count == _expectedCount))
            {
                ChatLog.Clear();
            }

            _logWritable     = false;
            _foreign         = false;
            _expectedCount   = 0;
            _attachedPlaying = false;
            _logNote         = NOTE_NOT_PLAYING;
            _chat            = null;
        }

        /// <summary> 시나리오의 채널 · 보낸 사람 · 내용으로 줄을 추가한다. 쓸 수 없는 상태면 아무것도 바꾸지 않고 false </summary>
        public bool TryAddLine()
        {
            if ((!LogWritable) || (Context == null))
            {
                return false;
            }

            UiSandboxScenarioSO scenario = Context.Scenario;
            return AddLine((EChatChannel)scenario.GetInt(KEY_CHANNEL), scenario.GetString(KEY_SENDER), scenario.GetString(KEY_TEXT));
        }

        /// <summary> 모의 송신 요청을 시작한다. 요청 시점의 채널 · 보낸 사람 · 내용을 붙잡아 두므로 응답을 기다리는 동안 입력을 고쳐도 보낸 줄은 바뀌지 않는다 </summary>
        public bool TrySend()
        {
            if ((!LogWritable) || (Context == null))
            {
                return false;
            }

            UiSandboxContext context = Context;
            UiSandboxScenarioSO scenario = context.Scenario;
            EChatChannel channel = (EChatChannel)scenario.GetInt(KEY_CHANNEL);
            string sender = scenario.GetString(KEY_SENDER);
            string text = scenario.GetString(KEY_TEXT);

            return RunRequest(context, OP_SEND, () => AddLine(channel, sender, text));
        }

        private bool AddLine(EChatChannel channel, string sender, string text)
        {
            if (!LogWritable)
            {
                return false;
            }

            bool changed = Mutate(() => ChatLog.AddMessage(channel, sender, text));
            RefreshView();
            return changed;
        }

        public bool TryAddSystemLine()
        {
            if ((!LogWritable) || (Context == null))
            {
                return false;
            }

            string text = Context.Scenario.GetString(KEY_SYSTEM);
            bool changed = Mutate(() => ChatLog.AddSystem(text));
            RefreshView();
            return changed;
        }

        public bool TryClearLog()
        {
            if (!LogWritable)
            {
                return false;
            }

            bool changed = Mutate(ChatLog.Clear);
            RefreshView();
            return changed;
        }

        // 연산 직전 개수가 우리가 아는 값과 다르면 외부에서 바뀐 것이다. 그때부터 쓰기를 멈추고 로그를 지우지 않는다
        private bool Mutate(System.Action operation)
        {
            if (ChatLog.Entries.Count != _expectedCount)
            {
                _foreign = true;
                _logNote = NOTE_FOREIGN;
                return false;
            }

            int generation = ChatLog.ClearGeneration;
            operation();
            bool changed = (ChatLog.Entries.Count != _expectedCount) || (ChatLog.ClearGeneration != generation);
            _expectedCount = ChatLog.Entries.Count;
            return changed;
        }

        private void RefreshView()
        {
            if (_chat != null)
            {
                _chat.SelectTab(_chat.ActiveTab);
            }
        }
    }
}
