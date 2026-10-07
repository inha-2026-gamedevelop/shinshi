// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 서버에 접속해 있는 동안 공개 서비스 상태를 주기적으로 읽어 게임 대상 공지를 채팅 시스템 줄로 넣는다, 씬에 두지 않고 스스로 만든다 </summary>
    public sealed class ServiceNoticeFeed : MonoBehaviour
    {
        // 서버가 보내는 System 채널 방송은 ChatClient가 받지 않고, 이 공지는 API에서 클라이언트가 직접 읽어 ChatLog에 로컬로 넣는다.
        // 접속하지 않은 동안과 서버 빌드에서는 읽지 않는다. 마을과 던전 사이를 옮길 때는 연결이 잠깐 끊겨도 로그가 유지되므로
        // 이미 보인 공지를 다시 넣지 않고, 로그가 비워졌을 때(ChatLog.ClearGeneration 변화)만 기록을 지워 다음 접속에서 다시 보인다.
        // 실패는 다음 주기에 같은 방식으로 다시 읽는다. 응답 본문과 토큰은 남기지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly LobbyApiClient        _api     = new LobbyApiClient();
        private readonly ServiceNoticeTracker  _tracker = new ServiceNoticeTracker();
        private readonly List<string>          _lines   = new List<string>();

        private float _nextPollTime        = 0f;
        private bool  _isPolling           = false;
        private bool  _wasConnected        = false;
        private int   _seenClearGeneration = 0;

        // 접속을 맺을 때마다 올린다. 요청을 보낸 접속과 응답을 받는 접속이 같은지 가린다
        private int   _connectionGeneration = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStart()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode)
            {
                return;
            }

            var root = new GameObject(nameof(ServiceNoticeFeed));
            DontDestroyOnLoad(root);
            root.AddComponent<ServiceNoticeFeed>();
#endif
        }

        private void Start()
        {
            _seenClearGeneration = ChatLog.ClearGeneration;
        }

        private void Update()
        {
            if (!NetworkClient.isConnected)
            {
                _wasConnected = false;
                return;
            }

            // 접속하면 바로 한 번 읽는다
            if (!_wasConnected)
            {
                _wasConnected = true;
                _nextPollTime = 0f;
                ++_connectionGeneration;
            }

            if ((_isPolling) || (Time.unscaledTime < _nextPollTime))
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + Constants.Chat.NOTICE_POLL_INTERVAL_SECONDS;
            string baseUrl = LauncherSession.ApiBaseUrl;
            if (string.IsNullOrEmpty(baseUrl))
            {
                return;
            }

            _isPolling = true;
            StartCoroutine(CoPoll(baseUrl));
        }

        private void OnDestroy()
        {
            _api.Abort();
        }

        /****************************************
        *                Methods
        ****************************************/

        private IEnumerator CoPoll(string baseUrl)
        {
            int connection = _connectionGeneration;
            int clear      = ChatLog.ClearGeneration;
            LobbyApiReply<ServiceStateResponse> reply = null;
            yield return _api.Send<ServiceStateResponse>(baseUrl, null, ApiRoutes.SERVICE_STATE, null, value => reply = value);
            _isPolling = false;
            if (ServiceNoticeRules.IsStaleResponse(connection, _connectionGeneration, NetworkClient.isConnected, clear,
                ChatLog.ClearGeneration, baseUrl, LauncherSession.ApiBaseUrl))
            {
                yield break;
            }

            if ((reply == null) || (!reply.IsSuccess))
            {
                if (reply != null)
                {
                    Debug.LogWarning($"[Notice] 서비스 상태를 읽지 못했다, status={reply.StatusCode}, reason={reply.ReasonCode}");
                }

                yield break;
            }

            Announce(reply.Value);
        }

        private void Announce(ServiceStateResponse state)
        {
            // 로그가 비워졌으면 이미 보인 공지도 다시 넣는다
            if (_seenClearGeneration != ChatLog.ClearGeneration)
            {
                _seenClearGeneration = ChatLog.ClearGeneration;
                _tracker.Reset();
            }

            // 구버전 API가 서버 시각을 비우면 PC 시계로 대신한다
            long nowMs = (state.ServerTimeUnixMs > 0) ? state.ServerTimeUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double now = Time.realtimeSinceStartupAsDouble;
            _lines.Clear();
            _tracker.Collect(state.Notices, nowMs, now, _lines);
            for (int i = 0; i < _lines.Count; ++i)
            {
                ChatLog.AddSystem(_lines[i], now);
            }
        }
    }
}
