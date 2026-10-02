// System
using System;
using System.Collections;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

// Third-party
using Mirror;
using Newtonsoft.Json;
using TMPro;

// Project
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 런처 세션으로 캐릭터를 선택하고 API가 배정한 마을에 접속한다 </summary>
    public sealed class LauncherTownConnector : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int REQUEST_TIMEOUT_SECONDS = 15;
        private const float CONNECT_TIMEOUT_SECONDS = 20f;
        private const int TEST_NAME_SUFFIX_LENGTH = 8;
        private const float PANEL_WIDTH = 620f;
        private const float PANEL_HEIGHT = 700f;
        private const float ROW_HEIGHT = 44f;
        private const float ROW_SPACING = 12f;
        private const float FONT_SIZE = 22f;
        private const string JSON_TYPE = "application/json";

        private ShinshiNetworkManager _manager;
        private GameObject _canvas;
        private TMP_Text _status;
        private RectTransform _rows;
        private UnityWebRequest _request;
        private Coroutine _operation;
        private bool _busy;
        private string _error;
        private CharacterCreateRequest _pendingCreation;
        private readonly JsonSerializerSettings _json = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> NetworkManager의 초기화가 끝난 뒤 한 번 연결한다 </summary>
        public void Initialize(ShinshiNetworkManager manager)
        {
            if ((_manager != null) || (manager == null))
            {
                return;
            }

            _manager = manager;
            CreatePanel();
            _operation = StartCoroutine(CoInitialize());
        }

        private IEnumerator CoInitialize()
        {
            while (LauncherSession.State == ELauncherSessionState.Receiving)
            {
                yield return null;
            }

            if (!LauncherSession.HasSession)
            {
                _status.text = "Launcher session failed. Restart from the launcher.";
                yield break;
            }

            yield return CoList();
        }

        private IEnumerator CoList()
        {
            _busy = true;
            CharacterListResponse result = null;
            yield return CoRequest<CharacterListResponse>(ApiRoutes.CHARACTERS, null, value => result = value);
            _busy = false;
            foreach (Transform row in _rows)
            {
                Destroy(row.gameObject);
            }

            if (result?.Characters == null)
            {
                _status.text = _error;
                AddButton("Retry", () => Run(CoList()));
                yield break;
            }

            _status.text = "Select a character to connect to the town test server.";
            foreach (CharacterSummary character in result.Characters)
            {
                CharacterSummary selected = character;
                AddButton(character.Name + " - Enter town", () => Run(CoJoin(selected.CharacterId)));
            }

            if (result.Characters.Count == 0)
            {
                _status.text = "No characters. Create a test warrior to begin.";
                AddButton("Create test warrior", () => Run(CoCreate()));
            }
        }

        private IEnumerator CoCreate()
        {
            _busy = true;
            if (_pendingCreation == null)
            {
                int[] face = new int[CharacterAppearance.FACE_SLIDER_COUNT];
                int[] body = new int[CharacterAppearance.BODY_SLIDER_COUNT];
                Array.Fill(face, CharacterAppearance.SLIDER_NEUTRAL);
                Array.Fill(body, CharacterAppearance.SLIDER_NEUTRAL);
                var appearance = new CharacterAppearance(CharacterAppearance.CURRENT_VERSION, CharacterAppearance.BODY_TYPE_FEMALE,
                    face, body, CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                    CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN);
                _pendingCreation = new CharacterCreateRequest(Guid.NewGuid().ToString("D"),
                    "LAN" + Guid.NewGuid().ToString("N").Substring(0, TEST_NAME_SUFFIX_LENGTH), ContractCodes.PlayerClass.WARRIOR, appearance);
            }

            // 응답을 잃어도 같은 작업 ID와 본문을 다시 보내 중복 생성을 막는다.
            CharacterCreateResponse result = null;
            yield return CoRequest<CharacterCreateResponse>(ApiRoutes.CHARACTERS, _pendingCreation, value => result = value);
            _busy = false;
            if (result?.Character == null)
            {
                _status.text = _error;
                yield break;
            }

            _pendingCreation = null;
            yield return CoList();
        }

        private IEnumerator CoJoin(string characterId)
        {
            if ((NetworkClient.active) || !(Transport.active is PortTransport portTransport)
                || !(_manager.authenticator is ShinshiClientAuthenticator authenticator))
            {
                yield break;
            }

            _busy = true;
            _status.text = "Requesting town entry...";
            JoinTicket ticket = null;
            yield return CoRequest<JoinTicket>(ApiRoutes.TOWN_JOIN_TICKET, new TownJoinTicketRequest(characterId), value => ticket = value);
            if ((ticket == null) || (ticket.ServerRole != ContractCodes.ServerRole.TOWN)
                || (ticket.CharacterId != characterId) || string.IsNullOrEmpty(ticket.JoinToken)
                || (Uri.CheckHostName(ticket.Address ?? string.Empty) == UriHostNameType.Unknown)
                || (ticket.Port <= 0) || (ticket.Port > ushort.MaxValue))
            {
                _status.text = ticket == null ? _error : "Invalid town ticket.";
                _busy = false;
                yield break;
            }

            _manager.ConfigureServerRole(EServerRole.Town);
            _manager.networkAddress = ticket.Address;
            portTransport.Port = (ushort)ticket.Port;
            authenticator.SetJoinToken(ticket.JoinToken);
            _manager.StartClient();
            float deadline = Time.realtimeSinceStartup + CONNECT_TIMEOUT_SECONDS;
            while ((Time.realtimeSinceStartup < deadline) && NetworkClient.active
                && ((NetworkClient.connection == null) || !NetworkClient.connection.isAuthenticated))
            {
                yield return null;
            }

            bool connected = NetworkClient.isConnected && (NetworkClient.connection != null)
                && NetworkClient.connection.isAuthenticated;
            _status.text = connected ? "Connected to town. Bootstrap test scene - gameplay is not available yet."
                : "Connection failed: " + authenticator.LastRejectReason + ". Retry after closing any other game window.";
            Debug.Log(connected ? "[LauncherTown] Town authentication succeeded." : "[LauncherTown] Town authentication failed.");
            if (!connected)
            {
                authenticator.CancelJoinRetry();
                _manager.StopClient();
            }
            _busy = connected;
        }

        private IEnumerator CoRequest<T>(string path, object body, Action<T> receive) where T : class
        {
            string token = LauncherSession.GetGameSessionToken();
            if (string.IsNullOrEmpty(token))
            {
                _error = "Session expired. Close the game and log in through the launcher again.";
                yield break;
            }

            string address = LauncherSession.ApiBaseUrl.TrimEnd('/') + path;
            using (var request = new UnityWebRequest(address, body == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST))
            {
                _request = request;
                request.timeout = REQUEST_TIMEOUT_SECONDS;
                request.redirectLimit = 0;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, _json)));
                    request.SetRequestHeader("Content-Type", JSON_TYPE);
                }

                yield return request.SendWebRequest();
                _request = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _error = "API request failed (HTTP " + request.responseCode + "). Check the server or restart the launcher.";
                    yield break;
                }

                T result;
                try
                {
                    result = JsonConvert.DeserializeObject<T>(request.downloadHandler.text, _json);
                }
                catch (JsonException)
                {
                    _error = "Invalid API response.";
                    yield break;
                }
                receive(result);
            }
        }

        private void Run(IEnumerator operation)
        {
            if (!_busy)
            {
                _operation = StartCoroutine(operation);
            }
        }

        private void CreatePanel()
        {
            _canvas = new GameObject("LauncherTown", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.transform.SetParent(transform, false);
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(PANEL_WIDTH * 2f, PANEL_HEIGHT);
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(_canvas.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            panel.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f, 0.96f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)ROW_SPACING, (int)ROW_SPACING, (int)ROW_SPACING, (int)ROW_SPACING);
            layout.spacing = ROW_SPACING;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _status = AddText(panel.transform, "Receiving launcher session...", ROW_HEIGHT * 2f);
            var rows = new GameObject("Characters", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rows.transform.SetParent(panel.transform, false);
            _rows = rows.GetComponent<RectTransform>();
            rows.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
            rows.GetComponent<VerticalLayoutGroup>().spacing = ROW_SPACING;
            if (EventSystem.current == null)
            {
                var events = new GameObject("LauncherInput", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(_canvas.transform, false);
            }
        }

        private void AddButton(string label, UnityEngine.Events.UnityAction action)
        {
            var row = new GameObject("Character", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(_rows, false);
            row.GetComponent<LayoutElement>().preferredHeight = ROW_HEIGHT;
            row.GetComponent<Image>().color = new Color(0.12f, 0.23f, 0.32f);
            row.GetComponent<Button>().onClick.AddListener(action);
            TMP_Text text = AddText(row.transform, label, ROW_HEIGHT);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
        }

        private static TMP_Text AddText(Transform parent, string value, float height)
        {
            var element = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            element.transform.SetParent(parent, false);
            element.GetComponent<LayoutElement>().preferredHeight = height;
            TMP_Text text = element.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.richText = false;
            text.fontSize = FONT_SIZE;
            text.color = Color.white;
            return text;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable()
        {
            if (_request != null)
            {
                _request.Abort();
            }
            if (_operation != null)
            {
                StopCoroutine(_operation);
                _operation = null;
            }
            _busy = false;
        }
    }
}
