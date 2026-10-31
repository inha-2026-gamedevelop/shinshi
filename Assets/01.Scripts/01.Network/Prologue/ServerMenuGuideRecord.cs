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

        /****************************************
        *              Properties
        ****************************************/

        public static ServerMenuGuideRecord Instance { get; } = new ServerMenuGuideRecord();

        /// <summary> 서버 목록을 한 번이라도 받았는가 </summary>
        public bool IsLoaded => _loaded;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 마친 메뉴인가. 목록을 받기 전에는 안내를 띄우지 않도록 마친 것으로 답한다 </summary>
        public bool IsCompleted(string menuId)
        {
            return (!_loaded) || (_completed.Contains(menuId));
        }

        /// <summary> 마침으로 기록한다, 메모리에 먼저 반영하고 서버에 보낸다. 이미 마친 메뉴는 아무것도 하지 않는다 </summary>
        public void MarkCompleted(string menuId)
        {
            if ((string.IsNullOrEmpty(menuId)) || (!_completed.Add(menuId)))
            {
                return;
            }

            Enqueue(new WorkItem { MenuId = menuId, Attempts = 0 });
        }

        /// <summary> 서버의 마친 메뉴 목록을 새로 받는다, 접속할 때마다 부른다 </summary>
        public void Refresh()
        {
            Enqueue(new WorkItem { MenuId = null, Attempts = 0 });
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
            _runner.StartCoroutine(CoPump());
        }

        // LobbyApiClient는 요청 하나씩만 처리하므로 작업을 한 줄로 세워 차례로 보낸다
        private IEnumerator CoPump()
        {
            while (_work.Count > 0)
            {
                WorkItem item = _work.Dequeue();
                LobbyApiReply<MenuGuideListResponse> reply = null;
                if (item.MenuId == null)
                {
                    yield return _api.SendGame<MenuGuideListResponse>(PrologueRoutes.MENU_GUIDES, null, value => reply = value);
                }
                else
                {
                    string path = string.Format(CultureInfo.InvariantCulture, PrologueRoutes.MENU_GUIDE_COMPLETE_FORMAT,
                        Uri.EscapeDataString(item.MenuId));
                    yield return _api.SendGame<MenuGuideListResponse>(path, new object(), value => reply = value);
                }

                if ((reply != null) && (reply.IsSuccess) && (reply.Value != null))
                {
                    Apply(reply.Value);
                    continue;
                }

                ++item.Attempts;
                if (item.Attempts < MAX_ATTEMPTS)
                {
                    yield return new WaitForSecondsRealtime(RETRY_DELAY_SECONDS);
                    _work.Enqueue(item);
                }
                else
                {
                    Debug.LogWarning($"[MenuGuide] 서버 기록을 포기한다, menu={item.MenuId ?? "(목록)"}");
                }
            }

            _pumping = false;
        }

        // 서버 목록과 아직 서버에 닿지 않은 메모리 쪽 값을 합친다
        private void Apply(MenuGuideListResponse response)
        {
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
        }
    }
}
