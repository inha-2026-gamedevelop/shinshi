using System;
using System.Collections.Generic;
using Shinshi.Systems;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> View 구현이 없는 문서 요구사항의 계약 전용 페이지 정의 모음. 서버 호출 없이 입력 검증과 모의 응답만 쓴다 </summary>
    public static class PresentationContractCatalog
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ID_SETTINGS    = "contract.settings";
        public const string ID_KEYBINDING  = "contract.keybinding";
        public const string ID_NPC         = "contract.npc";
        public const string ID_DIALOGUE    = "contract.dialogue";
        public const string ID_PARTY_ROOM  = "contract.partyroom";
        public const string ID_CHAR_CREATE = "contract.charcreate";
        public const string ID_LOGIN       = "contract.login";
        public const string ID_OVERLAY     = "contract.networkoverlay";
        public const string ID_INTERACTION = "contract.interaction";
        public const string ID_PARTY_LOOT  = "contract.partyloot";
        public const string ID_PROGRESSION = "contract.progression";
        public const string ID_NOTICE      = "contract.servicenotice";
        public const string ID_ABANDON     = "contract.abandon";

        public const string KEY_ABANDON_ACTIVE    = "abandon.sessionActive";
        public const string KEY_ABANDON_CONFIRMED = "abandon.confirmed";
        public const string KEY_ABANDON_SETTLED   = "abandon.alreadySettled";
        public const string KEY_ABANDON_MISMATCH  = "abandon.sessionMismatch";
        public const string KEY_ABANDON_EXTRA     = "abandon.extraEssence";
        public const string KEY_ABANDON_GRACE     = "abandon.graceRemainSeconds";
        public const string KEY_ABANDON_PENDING   = "abandon.settlementPending";

        public const int ABANDON_GRACE_SECONDS = 120;
        private const int MAX_ABANDON_ESSENCE  = 100000;

        public const int NAME_MIN_LENGTH = 2;
        public const int NAME_MAX_LENGTH = 12;

        public const int HUD_SCALE_MIN = 80;
        public const int HUD_SCALE_MAX = 130;

        public const int INVITE_EXPIRE_SECONDS = 30;

        public const string KEY_SETTINGS_TEXT  = "settings.textScale";
        public const string KEY_SETTINGS_HUD   = "settings.hudScale";
        public const string KEY_SETTINGS_SHAKE = "settings.screenShake";
        public const string KEY_SETTINGS_COLOR = "settings.colorblind";

        public const string KEY_KEYBIND_PREFIX = "keybinding.";

        public const string KEY_NPC_NAME     = "npc.name";
        public const string KEY_NPC_SERVICE  = "npc.service";
        public const string KEY_NPC_DISTANCE = "npc.distance";
        public const string KEY_NPC_RANGE    = "npc.range";

        public const string KEY_DIALOGUE_LINES   = "dialogue.lineCount";
        public const string KEY_DIALOGUE_INDEX   = "dialogue.lineIndex";
        public const string KEY_DIALOGUE_SPEAKER = "dialogue.speaker";
        public const string KEY_DIALOGUE_TEXT    = "dialogue.text";
        public const string KEY_DIALOGUE_CHOICES = "dialogue.choiceCount";
        public const string KEY_DIALOGUE_PICK    = "dialogue.pick";

        public const string KEY_ROOM_TITLE      = "partyroom.title";
        public const string KEY_ROOM_PASSWORD   = "partyroom.password";
        public const string KEY_ROOM_JOIN_PASS  = "partyroom.joinPassword";
        public const string KEY_ROOM_EXISTS     = "partyroom.roomExists";
        public const string KEY_ROOM_INVITE_AGE = "partyroom.inviteAgeSeconds";

        public const string KEY_CREATE_NAME  = "charcreate.name";
        public const string KEY_CREATE_CLASS = "charcreate.class";
        public const string KEY_CREATE_TAKEN = "charcreate.nameTaken";

        public const string KEY_LOGIN_ID       = "login.accountId";
        public const string KEY_LOGIN_PASSWORD = "login.password";
        public const string KEY_LOGIN_VALID    = "login.credentialsValid";
        public const string KEY_LOGIN_LOCKED   = "login.accountLocked";

        public const string KEY_OVERLAY_STATE   = "overlay.state";
        public const string KEY_OVERLAY_LATENCY = "overlay.latencyMs";
        public const string KEY_OVERLAY_MESSAGE = "overlay.message";

        public const string KEY_INTERACT_TARGET  = "interaction.target";
        public const string KEY_INTERACT_PROMPT  = "interaction.prompt";
        public const string KEY_INTERACT_ENABLED = "interaction.enabled";
        public const string KEY_INTERACT_HOLD    = "interaction.holdSeconds";

        public const string KEY_LOOT_PARTICIPANTS = "partyloot.eligibleCount";
        public const string KEY_LOOT_BALANCE      = "partyloot.balance";
        public const string KEY_LOOT_HIGHEST      = "partyloot.highestBid";
        public const string KEY_LOOT_BID          = "partyloot.myBid";
        public const string KEY_LOOT_REMAIN       = "partyloot.remainSeconds";
        public const string KEY_LOOT_BLOCKED      = "partyloot.auctionBlocked";
        public const string KEY_LOOT_LATE_JOIN    = "partyloot.joinedAfterStart";
        public const string KEY_LOOT_OTHERS       = "partyloot.othersOnlineAtEnd";

        public const string KEY_PROLOGUE_STEP   = "progression.prologueStep";
        public const string KEY_PROG_PROFILE    = "progression.profile";
        public const string KEY_PROG_INTRO_DONE = "progression.introDone";
        public const string KEY_PROG_FOREST     = "progression.forestCleared";
        public const string KEY_PROG_RUINS      = "progression.ruinsCleared";
        public const string KEY_PROG_CHEOLGAK_E = "progression.cheolgakEasyCleared";
        public const string KEY_PROG_CHEOLGAK_H = "progression.cheolgakHardCleared";
        public const string KEY_PROG_TRACES     = "progression.foxTraces";

        public const string KEY_NOTICE_COUNT    = "servicenotice.count";
        public const string KEY_NOTICE_CATEGORY = "servicenotice.category";
        public const string KEY_NOTICE_AGE      = "servicenotice.sinceFetchSeconds";
        public const string KEY_NOTICE_FAILS    = "servicenotice.fetchFails";

        // docs/design/experience-service.md 5장 파티 경매 기본안(위임 초기값 포함). 서버 규칙의 재구현이 아니라 입력 검증용 예시 값이다
        public const int LOOT_MIN_BID         = 100;
        public const int LOOT_MIN_STEP        = 10;
        public const int LOOT_DEFAULT_SECONDS = 30;
        public const int LOOT_MAX_SECONDS     = 120;
        public const int LOOT_EXTEND_BELOW    = 10;
        public const int LOOT_MAX_ELIGIBLE    = 4;

        // 같은 문서 7장: 프롤로그 약 90초(30+30+30), 여우의 흔적 3개로 월백 결계 해금
        public const int PROLOGUE_STEP_SECONDS = 30;
        public const int FOX_TRACES_REQUIRED   = 3;

        // 런처 문서의 소식 규칙: 홈 3개, 마지막 수신이 60초보다 오래되면 소식 탭에서 다시 받음
        public const int NOTICE_HOME_COUNT    = 3;
        public const int NOTICE_REFETCH_AFTER = 60;

        private const int DEFAULT_NPC_RANGE  = 3;
        private const int MAX_DIALOGUE_LINES = 20;
        private const int MAX_CHOICES        = 4;
        private const int MAX_LATENCY_MS     = 5000;
        private const int DEFAULT_LATENCY_MS = 40;
        private const int DEFAULT_HUD_SCALE  = 100;
        private const int MAX_HOLD_SECONDS   = 10;
        private const int MAX_INVITE_AGE     = 120;
        private const int MAX_NPC_RANGE      = 20;
        private const int MAX_NPC_DISTANCE   = 50;

        private static readonly string[] G_TextScales    = { "100%", "125%", "150%" };
        private static readonly string[] G_NpcServices   = { "상점", "우편", "경매장 안내", "대화" };
        private static readonly string[] G_ClassNames    = { "무사 (0, 구)", "도술사 (1, 구)", "신령사 (2, 구)", "검사 (3)", "마검사 (4)", "궁수 (5)" };
        private static readonly string[] G_OverlayStates = { "연결됨", "재연결 중", "연결 끊김" };

        private static readonly string[] G_PrologueSteps     = { "신시 토벌 준비 (30초)", "스승의 부적과 산귀 단서 (30초)", "월백 전투 시험 출정 안내 (30초)" };
        private static readonly string[] G_ProgressProfiles  = { "체험판 (Trial)", "정식 해금 기본안" };
        private static readonly string[] G_NoticeCategories  = { "Notice", "Update", "Event", "Maintenance" };

        private const int MAX_LOOT_BALANCE = 100000;
        private const int MAX_NOTICES      = 20;
        private const int MAX_NOTICE_AGE   = 600;
        private const int MAX_FOX_TRACES   = 3;

        /****************************************
        *                Methods
        ****************************************/

        public static ContractOnlyPage[] CreatePages()
        {
            ContractSpec[]     specs = CreateSpecs();
            ContractOnlyPage[] pages = new ContractOnlyPage[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                pages[i] = new ContractOnlyPage(specs[i]);
            }

            return pages;
        }

        public static ContractSpec[] CreateSpecs()
        {
            return new[]
            {
                CreateSettings(),
                CreateKeybinding(),
                CreateNpc(),
                CreateDialogue(),
                CreatePartyRoom(),
                CreateCharacterCreate(),
                CreateLogin(),
                CreateOverlay(),
                CreateInteraction(),
                CreatePartyLoot(),
                CreateProgression(),
                CreateServiceNotice(),
                CreateAbandon()
            };
        }

        public static string KeybindKey(EHotkeyAction action)
        {
            return KEY_KEYBIND_PREFIX + action;
        }

        public static string ValidateNameLength(string name)
        {
            int length = (name == null) ? 0 : name.Trim().Length;
            if ((length < NAME_MIN_LENGTH) || (length > NAME_MAX_LENGTH))
            {
                return "InvalidCharacterName (1002): 이름은 " + NAME_MIN_LENGTH + "~" + NAME_MAX_LENGTH + "자";
            }

            return null;
        }

        private static ContractSpec CreateSettings()
        {
            return new ContractSpec(
                ID_SETTINGS,
                "설정",
                "문서 요구사항: 글자 크기 100/125/150%, HUD 크기 80~130%, 화면 흔들림 끄기, 색약 보조. 설정 화면 View는 아직 없다.",
                new[]
                {
                    ContractField.Choice(KEY_SETTINGS_TEXT, "글자 크기", 0, G_TextScales),
                    ContractField.Int(KEY_SETTINGS_HUD, "HUD 크기 (%)", DEFAULT_HUD_SCALE, HUD_SCALE_MIN, HUD_SCALE_MAX),
                    ContractField.Bool(KEY_SETTINGS_SHAKE, "화면 흔들림 사용", true),
                    ContractField.Bool(KEY_SETTINGS_COLOR, "색약 보조", false)
                },
                new[]
                {
                    new ContractOperation("settings.save", "설정 저장 (모의 요청)", ValidateSettings,
                        scenario => "설정 저장됨: 글자 " + G_TextScales[Mathf.Clamp(scenario.GetInt(KEY_SETTINGS_TEXT), 0, G_TextScales.Length - 1)] +
                                    ", HUD " + scenario.GetInt(KEY_SETTINGS_HUD) + "%")
                });
        }

        public static string ValidateSettings(UiSandboxScenarioSO scenario)
        {
            int hud = scenario.GetInt(KEY_SETTINGS_HUD);
            if ((hud < HUD_SCALE_MIN) || (hud > HUD_SCALE_MAX))
            {
                return "HUD 크기는 " + HUD_SCALE_MIN + "~" + HUD_SCALE_MAX + "%";
            }

            int text = scenario.GetInt(KEY_SETTINGS_TEXT);
            if ((text < 0) || (text >= G_TextScales.Length))
            {
                return "글자 크기는 100/125/150% 중 하나";
            }

            return null;
        }

        private static ContractSpec CreateKeybinding()
        {
            List<ContractField> fields = new List<ContractField>();
            fields.Add(ContractField.Header("동작별 키 (KeyCode 이름)"));

            Array actions = Enum.GetValues(typeof(EHotkeyAction));
            for (int i = 0; i < actions.Length; ++i)
            {
                EHotkeyAction action = (EHotkeyAction)actions.GetValue(i);
                if (action == EHotkeyAction.None)
                {
                    continue;
                }

                fields.Add(ContractField.Text(KeybindKey(action), action.ToString(), DefaultKey(action)));
            }

            return new ContractSpec(
                ID_KEYBINDING,
                "키 설정",
                "문서 요구사항: EHotkeyAction별 키 재설정과 충돌 감지. 키 설정 화면 View는 아직 없고 HotkeyConfigSO의 기본 배치만 예시로 쓴다.",
                fields.ToArray(),
                new[]
                {
                    new ContractOperation("keybinding.save", "키 저장 (모의 요청)", ValidateKeybinding,
                        scenario => "키 설정 저장됨")
                });
        }

        private static string DefaultKey(EHotkeyAction action)
        {
            switch (action)
            {
                case EHotkeyAction.Inventory:
                    return KeyCode.I.ToString();
                case EHotkeyAction.AuctionHouse:
                    return KeyCode.Y.ToString();
                case EHotkeyAction.CloseWindow:
                    return KeyCode.Escape.ToString();
                case EHotkeyAction.SortiePrep:
                    return KeyCode.P.ToString();
                case EHotkeyAction.Chat:
                    return KeyCode.Return.ToString();
                default:
                    return KeyCode.None.ToString();
            }
        }

        public static string ValidateKeybinding(UiSandboxScenarioSO scenario)
        {
            Dictionary<KeyCode, EHotkeyAction> used = new Dictionary<KeyCode, EHotkeyAction>();
            Array actions = Enum.GetValues(typeof(EHotkeyAction));
            for (int i = 0; i < actions.Length; ++i)
            {
                EHotkeyAction action = (EHotkeyAction)actions.GetValue(i);
                if (action == EHotkeyAction.None)
                {
                    continue;
                }

                string  text = scenario.GetString(KeybindKey(action));
                KeyCode key;
                if ((string.IsNullOrEmpty(text)) || (!Enum.TryParse(text, true, out key)) || (!Enum.IsDefined(typeof(KeyCode), key)))
                {
                    return action + ": 알 수 없는 키 '" + text + "'";
                }

                if (key == KeyCode.None)
                {
                    continue;
                }

                EHotkeyAction other;
                if (used.TryGetValue(key, out other))
                {
                    return "키 충돌: " + key + " 가 " + other + " 와 " + action + " 에 중복 지정됨";
                }

                used.Add(key, action);
            }

            return null;
        }

        private static ContractSpec CreateNpc()
        {
            return new ContractSpec(
                ID_NPC,
                "NPC 서비스",
                "문서 요구사항: NPC가 상점 · 우편 · 경매장 안내 같은 서비스로 연결된다. 거리 제한 값은 예시이며 NPC 서비스 View는 아직 없다.",
                new[]
                {
                    ContractField.Text(KEY_NPC_NAME, "NPC 이름", "마을 상인"),
                    ContractField.Choice(KEY_NPC_SERVICE, "서비스", 0, G_NpcServices),
                    ContractField.Int(KEY_NPC_RANGE, "상호작용 거리 제한 (m, 예시)", DEFAULT_NPC_RANGE, 1, MAX_NPC_RANGE),
                    ContractField.Float(KEY_NPC_DISTANCE, "현재 거리 (m)", 1.5f, 0, MAX_NPC_DISTANCE)
                },
                new[]
                {
                    new ContractOperation("npc.interact", "서비스 열기 (모의 요청)",
                        scenario => (scenario.GetFloat(KEY_NPC_DISTANCE) > scenario.GetInt(KEY_NPC_RANGE)) ? "NPC와 너무 멀다" : null,
                        scenario => scenario.GetString(KEY_NPC_NAME) + ": " +
                                    G_NpcServices[Mathf.Clamp(scenario.GetInt(KEY_NPC_SERVICE), 0, G_NpcServices.Length - 1)] + " 열림")
                });
        }

        private static ContractSpec CreateDialogue()
        {
            return new ContractSpec(
                ID_DIALOGUE,
                "대화",
                "문서 요구사항: NPC 대화는 줄 단위로 진행되고 마지막 줄에서 선택지를 고른다. 대화 View는 아직 없다.",
                new[]
                {
                    ContractField.Text(KEY_DIALOGUE_SPEAKER, "화자", "마을 상인"),
                    ContractField.Text(KEY_DIALOGUE_TEXT, "대사", "어서 오게, 모험가"),
                    ContractField.Int(KEY_DIALOGUE_LINES, "전체 줄 수", 3, 1, MAX_DIALOGUE_LINES),
                    ContractField.Int(KEY_DIALOGUE_INDEX, "현재 줄 (0부터)", 0, 0, MAX_DIALOGUE_LINES - 1),
                    ContractField.Int(KEY_DIALOGUE_CHOICES, "선택지 수 (마지막 줄)", 2, 0, MAX_CHOICES),
                    ContractField.Int(KEY_DIALOGUE_PICK, "고를 선택지 (0부터)", 0, 0, MAX_CHOICES - 1)
                },
                new[]
                {
                    new ContractOperation("dialogue.advance", "다음 줄 (모의 요청)",
                        scenario => IsLastLine(scenario) ? "마지막 줄이다. 선택지를 고른다" : null,
                        scenario => "다음 줄로 이동"),
                    new ContractOperation("dialogue.choose", "선택지 고르기 (모의 요청)", ValidateDialogueChoice,
                        scenario => "선택지 " + scenario.GetInt(KEY_DIALOGUE_PICK) + " 선택됨")
                });
        }

        private static bool IsLastLine(UiSandboxScenarioSO scenario)
        {
            return scenario.GetInt(KEY_DIALOGUE_INDEX) >= (scenario.GetInt(KEY_DIALOGUE_LINES) - 1);
        }

        public static string ValidateDialogueChoice(UiSandboxScenarioSO scenario)
        {
            if (!IsLastLine(scenario))
            {
                return "마지막 줄에서만 선택지를 고를 수 있다";
            }

            int choices = scenario.GetInt(KEY_DIALOGUE_CHOICES);
            if ((choices <= 0) || (scenario.GetInt(KEY_DIALOGUE_PICK) >= choices))
            {
                return "없는 선택지";
            }

            return null;
        }

        private static ContractSpec CreatePartyRoom()
        {
            return new ContractSpec(
                ID_PARTY_ROOM,
                "파티 방",
                "문서 요구사항: 방 만들기 · 입장 · 비밀번호, 초대는 " + INVITE_EXPIRE_SECONDS + "초 뒤 만료. 오류 RoomNotFound / RoomPasswordMismatch / InviteExpired. 파티 방 View는 아직 없다.",
                new[]
                {
                    ContractField.Header("방 만들기"),
                    ContractField.Text(KEY_ROOM_TITLE, "방 제목", "숲 일반전 같이 가요"),
                    ContractField.Text(KEY_ROOM_PASSWORD, "방 비밀번호 (비면 공개)", string.Empty),
                    ContractField.Header("입장 / 초대"),
                    ContractField.Bool(KEY_ROOM_EXISTS, "방이 존재함", true),
                    ContractField.Text(KEY_ROOM_JOIN_PASS, "입력한 비밀번호", string.Empty),
                    ContractField.Int(KEY_ROOM_INVITE_AGE, "초대 후 경과 (초)", 0, 0, MAX_INVITE_AGE)
                },
                new[]
                {
                    new ContractOperation("partyroom.create", "방 만들기 (모의 요청)",
                        scenario => string.IsNullOrWhiteSpace(scenario.GetString(KEY_ROOM_TITLE)) ? "방 제목이 비어 있다" : null,
                        scenario => "방 생성됨: " + scenario.GetString(KEY_ROOM_TITLE)),
                    new ContractOperation("partyroom.join", "방 입장 (모의 요청)", ValidateRoomJoin,
                        scenario => "방에 입장했다"),
                    new ContractOperation("partyroom.acceptInvite", "초대 수락 (모의 요청)",
                        scenario => (scenario.GetInt(KEY_ROOM_INVITE_AGE) > INVITE_EXPIRE_SECONDS) ? "InviteExpired: 초대가 만료되었다" : null,
                        scenario => "초대를 수락했다")
                });
        }

        public static string ValidateRoomJoin(UiSandboxScenarioSO scenario)
        {
            if (!scenario.GetBool(KEY_ROOM_EXISTS))
            {
                return "RoomNotFound: 방을 찾을 수 없다";
            }

            string password = scenario.GetString(KEY_ROOM_PASSWORD);
            if ((!string.IsNullOrEmpty(password)) && (!string.Equals(password, scenario.GetString(KEY_ROOM_JOIN_PASS), StringComparison.Ordinal)))
            {
                return "RoomPasswordMismatch: 비밀번호가 다르다";
            }

            return null;
        }

        private static ContractSpec CreateCharacterCreate()
        {
            return new ContractSpec(
                ID_CHAR_CREATE,
                "캐릭터 생성",
                "문서 요구사항: 이름 " + NAME_MIN_LENGTH + "~" + NAME_MAX_LENGTH + "자, 중복 시 CharacterNameTaken(1001), 형식 오류 InvalidCharacterName(1002). " +
                "생성 화면 View는 없고 로비 네트워크 컴포넌트는 서버를 호출하므로 쓰지 않는다. 직업 목록은 ID를 함께 적었다: 구직업 0/1/2(무사 · 도술사 · 신령사)와 현재 직업 3/4/5(검사 · 마검사 · 궁수). " +
                "어느 ID를 생성에 허용할지는 서버 설정이 정하며 이 페이지는 허용 범위를 가정하지 않는다.",
                new[]
                {
                    ContractField.Text(KEY_CREATE_NAME, "캐릭터 이름", "새캐릭터"),
                    ContractField.Choice(KEY_CREATE_CLASS, "직업", 0, G_ClassNames),
                    ContractField.Bool(KEY_CREATE_TAKEN, "서버에서 이미 사용 중인 이름 (모의)", false)
                },
                new[]
                {
                    new ContractOperation("charcreate.submit", "생성 (모의 요청)", ValidateCharacterCreate,
                        scenario => "캐릭터 생성됨: " + scenario.GetString(KEY_CREATE_NAME))
                });
        }

        public static string ValidateCharacterCreate(UiSandboxScenarioSO scenario)
        {
            string invalid = ValidateNameLength(scenario.GetString(KEY_CREATE_NAME));
            if (invalid != null)
            {
                return invalid;
            }

            return scenario.GetBool(KEY_CREATE_TAKEN) ? "CharacterNameTaken (1001): 이미 사용 중인 이름" : null;
        }

        private static ContractSpec CreateLogin()
        {
            return new ContractSpec(
                ID_LOGIN,
                "로비 로그인",
                "문서 요구사항: 계정 로그인 실패는 InvalidCredentials(1205), 잠긴 계정은 AccountLocked(1206). 실제 로그인 컴포넌트는 서버를 호출하므로 쓰지 않으며 비밀번호는 시험용 문자열이다.",
                new[]
                {
                    ContractField.Text(KEY_LOGIN_ID, "계정", "tester"),
                    ContractField.Text(KEY_LOGIN_PASSWORD, "비밀번호 (시험용)", "sample"),
                    ContractField.Bool(KEY_LOGIN_VALID, "서버가 자격 증명을 인정함 (모의)", true),
                    ContractField.Bool(KEY_LOGIN_LOCKED, "계정 잠김 (모의)", false)
                },
                new[]
                {
                    new ContractOperation("login.submit", "로그인 (모의 요청)", ValidateLogin,
                        scenario => "로그인됨: " + scenario.GetString(KEY_LOGIN_ID))
                });
        }

        public static string ValidateLogin(UiSandboxScenarioSO scenario)
        {
            if ((string.IsNullOrEmpty(scenario.GetString(KEY_LOGIN_ID))) || (string.IsNullOrEmpty(scenario.GetString(KEY_LOGIN_PASSWORD))))
            {
                return "계정과 비밀번호를 입력한다";
            }

            if (scenario.GetBool(KEY_LOGIN_LOCKED))
            {
                return "AccountLocked (1206): 계정이 잠겼다";
            }

            return scenario.GetBool(KEY_LOGIN_VALID) ? null : "InvalidCredentials (1205): 계정 또는 비밀번호가 다르다";
        }

        private static ContractSpec CreateOverlay()
        {
            return new ContractSpec(
                ID_OVERLAY,
                "네트워크 상태 오버레이",
                "NetworkOverlay는 Mirror 연결 상태를 직접 읽으므로 쓰지 않는다. 연결 상태 · 지연 · 안내 문구와 재연결 요청의 계약만 시험한다.",
                new[]
                {
                    ContractField.Choice(KEY_OVERLAY_STATE, "연결 상태", 0, G_OverlayStates),
                    ContractField.Int(KEY_OVERLAY_LATENCY, "지연 (ms)", DEFAULT_LATENCY_MS, 0, MAX_LATENCY_MS),
                    ContractField.Text(KEY_OVERLAY_MESSAGE, "안내 문구", "서버와 연결이 끊겼습니다")
                },
                new[]
                {
                    new ContractOperation("network.reconnect", "재연결 (모의 요청)",
                        scenario => (scenario.GetInt(KEY_OVERLAY_STATE) == 0) ? "이미 연결되어 있다" : null,
                        scenario => "재연결됨")
                });
        }

        private static ContractSpec CreateInteraction()
        {
            return new ContractSpec(
                ID_INTERACTION,
                "상호작용 패널",
                "상호작용 패널 View는 직접 서버 명령을 보내므로 쓰지 않는다. 대상 · 안내 문구 · 길게 누르기 시간의 계약과 사용 요청만 시험한다.",
                new[]
                {
                    ContractField.Text(KEY_INTERACT_TARGET, "대상", "보물 상자"),
                    ContractField.Text(KEY_INTERACT_PROMPT, "안내 문구", "열기"),
                    ContractField.Bool(KEY_INTERACT_ENABLED, "상호작용 가능", true),
                    ContractField.Int(KEY_INTERACT_HOLD, "길게 누르기 (초, 0이면 즉시)", 0, 0, MAX_HOLD_SECONDS)
                },
                new[]
                {
                    new ContractOperation("interaction.use", "사용 (모의 요청)",
                        scenario => scenario.GetBool(KEY_INTERACT_ENABLED) ? null : "지금은 상호작용할 수 없다",
                        scenario => scenario.GetString(KEY_INTERACT_TARGET) + ": " + scenario.GetString(KEY_INTERACT_PROMPT) + " 완료")
                });
        }

        private static ContractSpec CreatePartyLoot()
        {
            return new ContractSpec(
                ID_PARTY_LOOT,
                "보스 파티 경매 · 단독 구매 · 분배 (U15)",
                "로드맵 U15의 경매 화면은 미착수다(API는 있고 경매 UI와 `auction` 기능 차단 때 개시 처리는 기획 미정). 유저 경매장(U28)과 별개이며 같은 거래 재화 잔액만 공유한다. " +
                "값은 experience-service 5장 기본안(최저가 " + LOOT_MIN_BID + ", 최소 증가 " + LOOT_MIN_STEP + ", 기본 " + LOOT_DEFAULT_SECONDS + "초, 10초 미만 입찰 시 10초로 연장, 상한 " + LOOT_MAX_SECONDS + "초)이고 " +
                "단독 모드 30초 유찰은 위임 초기안이다. 잔액 · 접속 인원은 모의 값이며 서버 정산을 재구현하지 않고 입력 검증만 한다. 상한 120초와 원자 낙찰은 서버 몫이라 여기서 시험하지 않는다.",
                new[]
                {
                    ContractField.Header("품목 상태 (모의)"),
                    ContractField.Int(KEY_LOOT_PARTICIPANTS, "품목을 열 때 접속 자격자 (명)", 3, 1, LOOT_MAX_ELIGIBLE),
                    ContractField.Int(KEY_LOOT_BALANCE, "내 잔액 (엽전)", 500, 0, MAX_LOOT_BALANCE),
                    ContractField.Int(KEY_LOOT_HIGHEST, "직전 최고가 (0이면 없음)", 0, 0, MAX_LOOT_BALANCE),
                    ContractField.Int(KEY_LOOT_BID, "내 입찰가", LOOT_MIN_BID, 0, MAX_LOOT_BALANCE),
                    ContractField.Int(KEY_LOOT_REMAIN, "남은 시간 (초)", LOOT_DEFAULT_SECONDS, 0, LOOT_MAX_SECONDS),
                    ContractField.Bool(KEY_LOOT_BLOCKED, "`auction` 기능 차단 중", false),
                    ContractField.Bool(KEY_LOOT_LATE_JOIN, "품목이 열린 뒤 복귀함", false),
                    ContractField.Header("분배 (정산)"),
                    ContractField.Int(KEY_LOOT_OTHERS, "종료 때 접속 중인 다른 참가자 (낙찰자 제외, 명)", 2, 0, LOOT_MAX_ELIGIBLE - 1)
                },
                new[]
                {
                    new ContractOperation("partyloot.bid", "입찰 (모의 요청)", ValidateLootBid, DescribeLootBid),
                    new ContractOperation("partyloot.buy", "단독 구매 (모의 요청)", ValidateLootBuy,
                        scenario => LOOT_MIN_BID + " 엽전 구매 완료. 대금은 소모하고 즉시 종료"),
                    new ContractOperation("partyloot.settle", "낙찰 · 분배 확인 (모의 요청)", ValidateLootSettle, DescribeLootSettle)
                });
        }

        public static string ValidateLootBid(UiSandboxScenarioSO scenario)
        {
            if (scenario.GetInt(KEY_LOOT_PARTICIPANTS) < 2)
            {
                return "접속 자격자가 한 명인 품목은 입찰이 아니라 단독 구매다";
            }

            if (scenario.GetBool(KEY_LOOT_BLOCKED))
            {
                return "auction 기능이 차단되어 입찰을 거절한다";
            }

            if (scenario.GetBool(KEY_LOOT_LATE_JOIN))
            {
                return "품목이 열린 뒤 복귀한 참가자는 다음 품목부터 입찰한다";
            }

            if (scenario.GetInt(KEY_LOOT_REMAIN) <= 0)
            {
                return "마감된 품목이다";
            }

            int highest = scenario.GetInt(KEY_LOOT_HIGHEST);
            int minimum = (highest > 0) ? (highest + LOOT_MIN_STEP) : LOOT_MIN_BID;
            int bid     = scenario.GetInt(KEY_LOOT_BID);
            if (bid < minimum)
            {
                return "입찰은 " + minimum + " 이상이어야 한다";
            }

            return (bid > scenario.GetInt(KEY_LOOT_BALANCE)) ? "잔액이 부족하다" : null;
        }

        public static string DescribeLootBid(UiSandboxScenarioSO scenario)
        {
            string extend = (scenario.GetInt(KEY_LOOT_REMAIN) < LOOT_EXTEND_BELOW)
                ? "남은 시간을 " + LOOT_EXTEND_BELOW + "초로 연장"
                : "시간 연장 없음";
            return "입찰 " + scenario.GetInt(KEY_LOOT_BID) + " 접수 (잔액에서 예약). " + extend;
        }

        public static string ValidateLootBuy(UiSandboxScenarioSO scenario)
        {
            if (scenario.GetInt(KEY_LOOT_PARTICIPANTS) != 1)
            {
                return "단독 구매는 품목을 열 때 접속 자격자가 한 명일 때만 쓴다";
            }

            if (scenario.GetInt(KEY_LOOT_REMAIN) <= 0)
            {
                return "제한 시간 안에 구매하지 않아 유찰되었다. 재료는 소멸한다";
            }

            return (scenario.GetInt(KEY_LOOT_BALANCE) < LOOT_MIN_BID) ? "잔액이 부족하다" : null;
        }

        public static string ValidateLootSettle(UiSandboxScenarioSO scenario)
        {
            if (scenario.GetInt(KEY_LOOT_PARTICIPANTS) < 2)
            {
                return "단독 품목은 분배가 없다";
            }

            return (scenario.GetInt(KEY_LOOT_HIGHEST) < LOOT_MIN_BID) ? "유효 입찰이 없어 유찰되었다. 재료는 소멸한다" : null;
        }

        public static string DescribeLootSettle(UiSandboxScenarioSO scenario)
        {
            int price  = scenario.GetInt(KEY_LOOT_HIGHEST);
            int others = scenario.GetInt(KEY_LOOT_OTHERS);
            if (others <= 0)
            {
                return "낙찰금 " + price + " 전부 소모 (분배 대상 없음)";
            }

            int share  = price / others;
            int burned = price - (share * others);
            return "낙찰금 " + price + " / 대상 " + others + "명 = 각 " + share + " 지급, 남는 " + burned + " 소모";
        }

        private static ContractSpec CreateProgression()
        {
            return new ContractSpec(
                ID_PROGRESSION,
                "프롤로그 · 튜토리얼 · 계정 진행 해금 (U18 / U21)",
                "U18(프롤로그 약 90초 · 튜토리얼)은 미착수이고 U21의 계정 진행 · 해금 기록과 신시 안내 완료 API는 있으나 안내 완료 송신 UI는 남았다. " +
                "프롤로그는 신시 토벌 준비 30초 → 스승의 부적과 단서 30초 → 출정 안내 30초(experience-service 7장). " +
                "해금 사슬은 숲 → 폐허 → 철각 이지 → 철각 하드 · 제단 → 여우의 흔적 " + FOX_TRACES_REQUIRED + "개 + 철각 하드 → 월백 이지의 기본안이며 값은 모의 입력이다. " +
                "체험판은 폐허 · 철각 · 제단 없이 월백 이지를 미리 해금한 전투 시험이고 정식 해금 완료로 기록하지 않는다.",
                new[]
                {
                    ContractField.Header("프롤로그"),
                    ContractField.Choice(KEY_PROLOGUE_STEP, "장면", 0, G_PrologueSteps),
                    ContractField.Header("계정 진행 (모의)"),
                    ContractField.Choice(KEY_PROG_PROFILE, "콘텐츠 프로필", 0, G_ProgressProfiles),
                    ContractField.Bool(KEY_PROG_INTRO_DONE, "신시 안내 완료 기록됨", false),
                    ContractField.Bool(KEY_PROG_FOREST, "첫 숲 6웨이브 성공", false),
                    ContractField.Bool(KEY_PROG_RUINS, "폐허 6웨이브 성공", false),
                    ContractField.Bool(KEY_PROG_CHEOLGAK_E, "철각 이지 첫 처치", false),
                    ContractField.Bool(KEY_PROG_CHEOLGAK_H, "철각 하드 첫 처치", false),
                    ContractField.Int(KEY_PROG_TRACES, "여우의 흔적 (개)", 0, 0, MAX_FOX_TRACES)
                },
                new[]
                {
                    new ContractOperation("prologue.play", "프롤로그 장면 재생 (모의 요청)", scenario => null,
                        scenario => "장면 " + (scenario.GetInt(KEY_PROLOGUE_STEP) + 1) + "/" + G_PrologueSteps.Length + " 재생: " +
                                    G_PrologueSteps[Mathf.Clamp(scenario.GetInt(KEY_PROLOGUE_STEP), 0, G_PrologueSteps.Length - 1)]),
                    new ContractOperation("progress.intro", "신시 안내 완료 송신 (모의 요청)",
                        scenario => scenario.GetBool(KEY_PROG_INTRO_DONE) ? "이미 기록된 안내다. 한 번만 기록한다" : null,
                        scenario => "신시 안내 완료 기록"),
                    new ContractOperation("progress.ruins", "폐허 입장 확인 (모의 요청)", ValidateRuinsEntry,
                        scenario => "폐허 입장 가능"),
                    new ContractOperation("progress.cheolgakEasy", "철각 이지 입장 확인 (모의 요청)", ValidateCheolgakEasyEntry,
                        scenario => "철각 이지 입장 가능 (보스 티켓 1장 필요)"),
                    new ContractOperation("progress.cheolgakHard", "철각 하드 · 제단 입장 확인 (모의 요청)", ValidateCheolgakHardEntry,
                        scenario => "철각 하드와 제단 입장 가능"),
                    new ContractOperation("progress.moonwhite", "월백 이지 입장 확인 (모의 요청)", ValidateMoonwhiteEntry,
                        scenario => IsTrialProfile(scenario)
                            ? "체험판: 월백 이지는 미리 해금된 전투 시험이다 (정식 해금 완료로 기록하지 않음)"
                            : "월백 이지 입장 가능")
                });
        }

        private static bool IsTrialProfile(UiSandboxScenarioSO scenario)
        {
            return scenario.GetInt(KEY_PROG_PROFILE) == 0;
        }

        private static string TrialExcludes(UiSandboxScenarioSO scenario)
        {
            return IsTrialProfile(scenario) ? "체험판에는 폐허 · 철각 · 제단이 없다" : null;
        }

        public static string ValidateRuinsEntry(UiSandboxScenarioSO scenario)
        {
            string trial = TrialExcludes(scenario);
            if (trial != null)
            {
                return trial;
            }

            if (!scenario.GetBool(KEY_PROG_INTRO_DONE))
            {
                return "신시 안내를 먼저 완료한다";
            }

            return scenario.GetBool(KEY_PROG_FOREST) ? null : "첫 숲 6웨이브를 먼저 성공한다";
        }

        public static string ValidateCheolgakEasyEntry(UiSandboxScenarioSO scenario)
        {
            string trial = TrialExcludes(scenario);
            if (trial != null)
            {
                return trial;
            }

            return scenario.GetBool(KEY_PROG_RUINS) ? null : "폐허 6웨이브를 먼저 성공한다";
        }

        public static string ValidateCheolgakHardEntry(UiSandboxScenarioSO scenario)
        {
            string trial = TrialExcludes(scenario);
            if (trial != null)
            {
                return trial;
            }

            return scenario.GetBool(KEY_PROG_CHEOLGAK_E) ? null : "철각 이지를 먼저 처치한다";
        }

        public static string ValidateMoonwhiteEntry(UiSandboxScenarioSO scenario)
        {
            if (IsTrialProfile(scenario))
            {
                return null;
            }

            if (scenario.GetInt(KEY_PROG_TRACES) < FOX_TRACES_REQUIRED)
            {
                return "여우의 흔적 " + FOX_TRACES_REQUIRED + "개가 필요하다";
            }

            return scenario.GetBool(KEY_PROG_CHEOLGAK_H) ? null : "철각 하드 첫 처치 기록이 필요하다";
        }

        private static ContractSpec CreateServiceNotice()
        {
            return new ContractSpec(
                ID_NOTICE,
                "서비스 공지 (런처 소식 계약)",
                "서비스 공지는 런처 문서(launcher-and-patch 9항)가 정의한다: GET /v1/service-state의 notices[], 분류 Notice/Update/Event/Maintenance, 홈 " + NOTICE_HOME_COUNT + "개, " +
                "비면 '새 소식이 없습니다', 마지막 수신이 " + NOTICE_REFETCH_AFTER + "초보다 오래되면 소식 탭에서 다시 받는다. 게임 안 공지 View는 문서 근거가 없고 런처 UI는 이 샌드박스의 View 대상이 아니라 입력 계약만 시험한다.",
                new[]
                {
                    ContractField.Int(KEY_NOTICE_COUNT, "수신한 공지 수", 5, 0, MAX_NOTICES),
                    ContractField.Choice(KEY_NOTICE_CATEGORY, "열 공지의 분류", 0, G_NoticeCategories),
                    ContractField.Int(KEY_NOTICE_AGE, "마지막 수신 후 경과 (초)", 0, 0, MAX_NOTICE_AGE),
                    ContractField.Bool(KEY_NOTICE_FAILS, "공지를 받지 못함 (모의)", false)
                },
                new[]
                {
                    new ContractOperation("notice.refresh", "소식 탭 열기 · 다시 받기 (모의 요청)", ValidateNoticeRefresh,
                        scenario => (scenario.GetInt(KEY_NOTICE_COUNT) == 0)
                            ? "새 소식이 없습니다"
                            : scenario.GetInt(KEY_NOTICE_COUNT) + "건 수신, 홈에는 " + Mathf.Min(scenario.GetInt(KEY_NOTICE_COUNT), NOTICE_HOME_COUNT) + "건"),
                    new ContractOperation("notice.open", "공지 상세 열기 (모의 요청)",
                        scenario => (scenario.GetInt(KEY_NOTICE_COUNT) == 0) ? "새 소식이 없습니다" : null,
                        scenario => G_NoticeCategories[Mathf.Clamp(scenario.GetInt(KEY_NOTICE_CATEGORY), 0, G_NoticeCategories.Length - 1)] + " 공지 상세 열림")
                });
        }

        public static string ValidateNoticeRefresh(UiSandboxScenarioSO scenario)
        {
            if (scenario.GetBool(KEY_NOTICE_FAILS))
            {
                return "공지를 받지 못했다. 다시 시도한다";
            }

            return (scenario.GetInt(KEY_NOTICE_AGE) <= NOTICE_REFETCH_AFTER) ? "마지막 수신이 " + NOTICE_REFETCH_AFTER + "초 이내라 다시 받지 않는다" : null;
        }

        private static ContractSpec CreateAbandon()
        {
            return new ContractSpec(
                ID_ABANDON,
                "중도 포기 · 귀환 (U17)",
                "로드맵 U17의 '포기·귀환 화면'은 아직 없다(SortieClient · SortieReturnFlow 흐름만 구현). 기술 설계의 RequestAbandon(sessionId, operationId) 실패 코드 AlreadySettled · SessionMismatch, " +
                "접속 끊김 " + ABANDON_GRACE_SECONDS + "초 복귀 유예, 포기 직후 새 출정은 원 출정 정산까지 막힘을 입력 계약으로만 시험한다. 정산 값은 전부 더미이며 추가 정기는 floor(추가 정기 ÷ 2)만 보존하는 문서 규칙의 예시다. 결과 화면 복귀와는 별개다.",
                new[]
                {
                    ContractField.Bool(KEY_ABANDON_ACTIVE, "출정 진행 중", true),
                    ContractField.Bool(KEY_ABANDON_CONFIRMED, "포기 확인 창에서 확인", true),
                    ContractField.Bool(KEY_ABANDON_SETTLED, "이미 정산됨 (AlreadySettled)", false),
                    ContractField.Bool(KEY_ABANDON_MISMATCH, "다른 출정 번호 (SessionMismatch)", false),
                    ContractField.Int(KEY_ABANDON_EXTRA, "확보한 추가 정기 (더미)", 1000, 0, MAX_ABANDON_ESSENCE),
                    ContractField.Int(KEY_ABANDON_GRACE, "재접속 유예 남은 시간 (초)", ABANDON_GRACE_SECONDS, 0, ABANDON_GRACE_SECONDS),
                    ContractField.Bool(KEY_ABANDON_PENDING, "원 출정 정산 대기 중", true)
                },
                new[]
                {
                    new ContractOperation("abandon.request", "중도 포기 (모의 요청)", ValidateAbandon,
                        scenario => "개인 실패 정산 후 마을 귀환 (추가 정기 " + scenario.GetInt(KEY_ABANDON_EXTRA) + " 중 " + AbandonPreserved(scenario) + " 보존, 더미)"),
                    new ContractOperation("abandon.reconnect", "재접속 복귀 (모의 요청)", ValidateReconnect,
                        scenario => "기존 체력 · 쿨다운 · 물약 상태로 복귀 (유예 " + scenario.GetInt(KEY_ABANDON_GRACE) + "초 남음)"),
                    new ContractOperation("abandon.newSortie", "새 출정 시작 (모의 요청)", ValidateNewSortieAfterAbandon,
                        scenario => "새 출정 시작 가능")
                });
        }

        public static int AbandonPreserved(UiSandboxScenarioSO scenario)
        {
            return scenario.GetInt(KEY_ABANDON_EXTRA) / 2;
        }

        public static string ValidateAbandon(UiSandboxScenarioSO scenario)
        {
            if (!scenario.GetBool(KEY_ABANDON_ACTIVE))
            {
                return "진행 중인 출정이 없다";
            }

            if (scenario.GetBool(KEY_ABANDON_MISMATCH))
            {
                return "SessionMismatch: 현재 출정 번호와 다르다";
            }

            if (scenario.GetBool(KEY_ABANDON_SETTLED))
            {
                return "AlreadySettled: 이미 정산된 출정이다";
            }

            return scenario.GetBool(KEY_ABANDON_CONFIRMED) ? null : "포기 확인이 필요하다";
        }

        public static string ValidateReconnect(UiSandboxScenarioSO scenario)
        {
            if (!scenario.GetBool(KEY_ABANDON_ACTIVE))
            {
                return "진행 중인 출정이 없다";
            }

            if (scenario.GetBool(KEY_ABANDON_SETTLED))
            {
                return "이미 정산돼 재합류할 수 없다";
            }

            return (scenario.GetInt(KEY_ABANDON_GRACE) <= 0) ? "유예 " + ABANDON_GRACE_SECONDS + "초가 지나 이탈로 정산됐다 (재합류 불가)" : null;
        }

        public static string ValidateNewSortieAfterAbandon(UiSandboxScenarioSO scenario)
        {
            return scenario.GetBool(KEY_ABANDON_PENDING) ? "원 출정 정산이 끝나기 전에는 새 출정을 시작할 수 없다" : null;
        }
    }
}
