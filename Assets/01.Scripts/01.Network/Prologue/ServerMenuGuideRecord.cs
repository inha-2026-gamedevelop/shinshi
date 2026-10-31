// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 성장 메뉴 안내 완료는 계정 단위 서버 기록이 정본이다. 진행기(MenuGuideController)는 동기 인터페이스만 보므로 접속할 때 받은 목록을
    // 메모리에 두고 읽고, 마칠 때는 메모리에 먼저 반영한 뒤 서버에 비동기로 보낸다(같은 메뉴를 다시 보내도 서버 결과는 같다).
    // 목록을 받기 전에는 안내를 띄우지 않는다: 이미 마친 메뉴를 다시 안내하는 것보다 잠시 건너뛰는 편이 낫다.
    // 응답이 실패하면 몇 번 다시 보내고, 그래도 안 되면 메모리에만 남긴다(다음 접속의 목록이 서버 상태로 되돌린다)
    /// <summary> 서버 계정 데이터에 연결한 성장 메뉴 안내 완료 기록, 로비 REST 창구를 쓴다 </summary>
    public sealed class ServerMenuGuideRecord : IMenuGuideRecord
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    MAX_ATTEMPTS         = 3;
        private const float  RETRY_DELAY_SECONDS  = 2f;
        private const string RUNNER_OBJECT_NAME   = "ServerMenuGuideRecordRunner";

        /****************************************
        *                Types
        ****************************************/

        private sealed class Runner : MonoBehaviour
        {
        }

        private sealed class WorkItem
        {
            public string MenuId;   // null이면 목록을 새로 받는다
            public int    Attempts;
            public string AccountId;
            public int    Generation;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<string>     _completed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<WorkItem>     _work      = new Queue<WorkItem>();
        private readonly LobbyApiClient      _api       = new LobbyApiClient();

        private Runner _runner  = null;
        private bool   _pumping = false;
        private bool   _loaded  = false;
        private string _accountId = null;
        private int    _generation = 0;

        /****************************************
        *              Properties
        ****************************************/

        public static ServerMenuGuideRecord Instance { get; } = new ServerMenuGuideRecord();

        /// <summary> 서버 목록을 한 번이라도 받았는가 </summary>
        public bool IsLoaded => (EnsureContext()) && (_loaded);

        /// <summary> 완료 목록을 서버 응답으로 갱신했다. 목록을 기다리던 열린 창이 첫 안내 여부를 다시 확인한다 </summary>
        public event Action Changed;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 마친 메뉴인가. 목록을 받기 전에는 안내를 띄우지 않도록 마친 것으로 답한다 </summary>
        public bool IsCompleted(string menuId)
        {
            EnsureContext();
            return (!_loaded) || (_completed.Contains(menuId));
        }

        /// <summary> 마침으로 기록한다, 메모리에 먼저 반영하고 서버에 보낸다. 이미 마친 메뉴는 아무것도 하지 않는다 </summary>
        public void MarkCompleted(string menuId)
        {
            if ((!EnsureContext()) || (string.IsNullOrEmpty(menuId)) || (!_completed.Add(menuId)))
            {
                return;
            }

            Enqueue(new WorkItem { MenuId = menuId, Attempts = 0, AccountId = _accountId, Generation = _generation });
        }

        /// <summary> 서버의 마친 메뉴 목록을 새로 받는다, 접속할 때마다 부른다 </summary>
        public void Refresh()
        {
            if (EnsureContext())
            {
                Enqueue(new WorkItem { MenuId = null, Attempts = 0, AccountId = _accountId, Generation = _generation });
            }
        }

        // 로그인 계정이 바뀌면 이전 완료 목록과 전송 대기를 함께 버린다. 같은 계정의 토큰 갱신은 대기 중인 완료 기록을 유지한다.
        private bool EnsureContext()
        {
            LauncherSession.StateChanged -= OnSessionStateChanged;
            LauncherSession.StateChanged += OnSessionStateChanged;
            string current = LauncherSession.HasSession ? LauncherSession.AccountId : null;
            if (string.Equals(current, _accountId, StringComparison.Ordinal))
            {
                return !string.IsNullOrEmpty(current);
            }

            _accountId = current;
            ++_generation;
            _loaded = false;
            _completed.Clear();
            _work.Clear();
            _api.Abort();
            if (_runner != null)
            {
                _runner.StopAllCoroutines();
            }
            _pumping = false;
            Changed?.Invoke();
            return !string.IsNullOrEmpty(current);
        }

        private void OnSessionStateChanged(ELauncherSessionState state)
        {
            EnsureContext();
        }

        private bool IsCurrent(WorkItem item)
        {
            return (EnsureContext()) && (item.Generation == _generation)
                && (string.Equals(item.AccountId, _accountId, StringComparison.Ordinal));
        }

        private void Enqueue(WorkItem item)
        {
            _work.Enqueue(item);
            if (_pumping)
            {
                return;
            }

            if (_runner == null)
            {
                var host = new GameObject(RUNNER_OBJECT_NAME);
                UnityEngine.Object.DontDestroyOnLoad(host);
                _runner = host.AddComponent<Runner>();
            }

            _pumping = true;
            _runner.StartCoroutine(CoPump(_generation));
        }

        // LobbyApiClient는 요청 하나씩만 처리하므로 작업을 한 줄로 세워 차례로 보낸다
        private IEnumerator CoPump(int generation)
        {
            while ((generation == _generation) && (_work.Count > 0))
            {
                WorkItem item = _work.Dequeue();
                if (!IsCurrent(item))
                {
                    continue;
                }

                // SendGame은 실제 전송 때의 전역 토큰을 읽으므로 계정이 바뀐 뒤 이전 작업을 보낼 수 있다.
                // 작업 계정을 확인한 이 시점의 토큰과 주소를 고정해 중첩 코루틴에 넘긴다.
                string token = LauncherSession.GetGameSessionToken();
                string baseUrl = LauncherSession.ApiBaseUrl;
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }
                LobbyApiReply<MenuGuideListResponse> reply = null;
                if (item.MenuId == null)
                {
                    yield return _api.Send<MenuGuideListResponse>(baseUrl, token, PrologueRoutes.MENU_GUIDES, null, value => reply = value);
                }
                else
                {
                    string path = string.Format(CultureInfo.InvariantCulture, PrologueRoutes.MENU_GUIDE_COMPLETE_FORMAT,
                        Uri.EscapeDataString(item.MenuId));
                    yield return _api.Send<MenuGuideListResponse>(baseUrl, token, path, new object(), value => reply = value);
                }

                if (!IsCurrent(item))
                {
                    continue;
                }

                if ((reply != null) && (reply.IsSuccess) && (reply.Value != null))
                {
                    Apply(item, reply.Value);
                    continue;
                }

                ++item.Attempts;
                if (item.Attempts < MAX_ATTEMPTS)
                {
                    yield return new WaitForSecondsRealtime(RETRY_DELAY_SECONDS);
                    if (IsCurrent(item))
                    {
                        _work.Enqueue(item);
                    }
                }
                else
                {
                    Debug.LogWarning($"[MenuGuide] 서버 기록을 포기한다, menu={item.MenuId ?? "(목록)"}");
                }
            }

            if (generation == _generation)
            {
                _pumping = false;
            }
        }

        // 서버 목록과 아직 서버에 닿지 않은 메모리 쪽 값을 합친다
        private void Apply(WorkItem item, MenuGuideListResponse response)
        {
            if (!IsCurrent(item))
            {
                return;
            }
            string[] ids = response.MenuIds;
            if (ids != null)
            {
                for (int i = 0; i < ids.Length; ++i)
                {
                    if (!string.IsNullOrEmpty(ids[i]))
                    {
                        _completed.Add(ids[i]);
                    }
                }
            }

            _loaded = true;
            Changed?.Invoke();
        }
    }
}
