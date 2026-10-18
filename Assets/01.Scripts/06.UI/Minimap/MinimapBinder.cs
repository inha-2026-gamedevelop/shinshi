// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 미니맵 창에 내 위치와 파티원, 보스 위치를 넣는다. 접속 전 개발 씬에서는 직접 연결한 Transform을 쓴다 </summary>
    // 위치는 서버가 복제한 개체의 Transform만 읽는다. 다른 플레이어와 보스 탐색은 Mirror의 스폰 목록을 주기마다 한 번만 돈다
    public sealed class MinimapBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float COLLECT_INTERVAL_SECONDS = 0.25f;

        [SerializeField] private MinimapView  _view = null;
        [SerializeField] private MinimapMapSO _map  = null;

        [Header("개발 씬 전용")]
        [SerializeField] private Transform _devTarget = null;
        [SerializeField] private Transform _devBoss   = null;

        private readonly List<Transform> _remotes = new List<Transform>(Constants.Session.MAX_PARTY_SIZE);

        private Transform _boss          = null;
        private float     _nextCollectAt = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB와 달리 지도 설정은 씬에 직접 연결된 에셋이라 Start에서 한 번만 넣는다
        private void Start()
        {
            if (_view != null)
            {
                _view.SetMap(_map);
            }
        }

        // 내 이동과 카메라가 Update에서 끝난 뒤에 읽는다. 같은 Update에서 돌면 한 프레임 뒤처져 표시가 떨린다
        private void LateUpdate()
        {
            if ((_view == null) || (_map == null))
            {
                return;
            }

            bool connected = NetworkClient.isConnected;
            if (!TryGetSelf(connected, out Vector3 position, out float yaw))
            {
                _view.SetSelfVisible(false);
                _view.SetCenter(_map.WorldCenter);
                _view.HideMarkersFrom(0);
                return;
            }

            _view.SetSelfVisible(true);
            _view.SetSelfYaw(yaw);
            _view.SetCenter(MinimapRules.ToXz(position));

            if (connected)
            {
                CollectIfDue();
            }

            int used = 0;
            for (int i = 0; i < _remotes.Count; ++i)
            {
                if (_remotes[i] == null)
                {
                    continue;
                }

                if (_view.SetMarker(used, MinimapRules.ToXz(_remotes[i].position), EMinimapMarker.Party))
                {
                    ++used;
                }
            }

            Transform boss = connected ? _boss : _devBoss;
            if ((boss != null) && (_view.SetMarker(used, MinimapRules.ToXz(boss.position), EMinimapMarker.Boss)))
            {
                ++used;
            }

            _view.HideMarkersFrom(used);
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool TryGetSelf(bool connected, out Vector3 position, out float yaw)
        {
            Transform self = null;
            if (connected)
            {
                NetworkIdentity local = NetworkClient.localPlayer;
                self = (local != null) ? local.transform : null;
            }
            else
            {
                self = _devTarget;
            }

            if (self == null)
            {
                position = Vector3.zero;
                yaw      = 0f;
                return false;
            }

            position = self.position;
            yaw      = self.eulerAngles.y;
            return true;
        }

        // 스폰 목록을 매 프레임 돌지 않는다. 파티원과 보스가 바뀌는 속도는 이 주기로 충분하다
        private void CollectIfDue()
        {
            if (Time.unscaledTime < _nextCollectAt)
            {
                return;
            }

            _nextCollectAt = Time.unscaledTime + COLLECT_INTERVAL_SECONDS;
            _remotes.Clear();
            _boss = null;
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if ((!identity.isOwned) && (identity.TryGetComponent(out PlayerCombatState _)))
                {
                    _remotes.Add(identity.transform);
                    continue;
                }

                if ((_boss == null) && (identity.TryGetComponent(out BossState _)))
                {
                    _boss = identity.transform;
                }
            }
        }
    }
}
