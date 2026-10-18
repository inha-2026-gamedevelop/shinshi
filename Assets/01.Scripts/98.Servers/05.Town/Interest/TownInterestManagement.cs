// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 마을 전용이다. 던전은 4인이라 붙이지 않는다. 마을의 네트워크 객체는 사실상 플레이어뿐이라(NPC와 상점은 클라이언트 쪽 객체다)
    // 플레이어가 아닌 객체는 관측자 반경으로만 처리한다. 연결이 소유한 부속 객체는 소유자의 관측자를 따른다
    /// <summary> 접속자마다 가까운 K명과 파티원만 보이게 하는 마을 관심 영역 </summary>
    public sealed class TownInterestManagement : InterestManagement
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TownVisibilitySolver _solver = new TownVisibilitySolver(Constants.Town.AOI_VIEW_RADIUS,
            Constants.Town.AOI_CELL_SIZE, Constants.Town.AOI_MAX_VISIBLE, Constants.Session.MAX_SESSION_PARTICIPANTS);

        private readonly PeerMap   _peers   = new PeerMap();
        private readonly List<int> _scratch = new List<int>();

        private IChatPartyLookup _party;
        private double           _lastTickTime;
        private int              _lastRefreshFrame = -1;
        private bool             _tickRequested;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            double now = NetworkTime.localTime;
            if ((!_tickRequested) && (now < (_lastTickTime + Constants.Town.AOI_REFRESH_SECONDS)))
            {
                return;
            }

            _tickRequested = false;
            _lastTickTime  = now;
            Refresh();
            RebuildAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 파티원 조회를 건다. 걸지 않으면 파티원 예외 없이 거리만 쓴다 </summary>
        public void Bind(IChatPartyLookup party)
        {
            _party = party;
        }

        [ServerCallback]
        public override void ResetState()
        {
            _lastTickTime     = 0D;
            _lastRefreshFrame = -1;
            _tickRequested    = false;
            _solver.Clear();
            _peers.Clear();
        }

        // 새 플레이어는 다음 프레임에 바로 전체 재구축을 받아 다른 접속자와 서로 보이게 한다
        public override void OnSpawned(NetworkIdentity identity)
        {
            if (IsPlayerObject(identity))
            {
                _tickRequested = true;
            }
        }

        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
        {
            NetworkConnectionToClient owner = identity.connectionToClient;
            if (owner != null)
            {
                if (owner.connectionId == newObserver.connectionId)
                {
                    return true;
                }

                RefreshIfMissing(owner, newObserver);
                return _solver.IsVisible(newObserver.connectionId, owner.connectionId);
            }

            if (newObserver.identity == null)
            {
                return true;
            }

            Vector3 a = identity.transform.position;
            Vector3 b = newObserver.identity.transform.position;
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return ((dx * dx) + (dz * dz)) <= (Constants.Town.AOI_VIEW_RADIUS * Constants.Town.AOI_VIEW_RADIUS);
        }

        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
        {
            _scratch.Clear();
            NetworkConnectionToClient owner = identity.connectionToClient;
            if (owner != null)
            {
                RefreshIfMissing(owner, null);
                _solver.GetObserversOf(owner.connectionId, _scratch);
            }
            else
            {
                Vector3 position = identity.transform.position;
                _solver.GetViewersNear(position.x, position.z, _scratch);
            }

            for (int i = 0; i < _scratch.Count; ++i)
            {
                if (NetworkServer.connections.TryGetValue(_scratch[i], out NetworkConnectionToClient connection))
                {
                    newObservers.Add(connection);
                }
            }
        }

        // 막 들어온 연결이나 막 스폰된 소유자는 아직 표에 없다. 한 프레임에 한 번만 다시 계산해 가입이 몰려도 비용이 묶인다
        private void RefreshIfMissing(NetworkConnectionToClient owner, NetworkConnectionToClient viewer)
        {
            bool missing = (!_solver.Contains(owner.connectionId)) || ((viewer != null) && (!_solver.Contains(viewer.connectionId)));
            if ((missing) && (_lastRefreshFrame != Time.frameCount))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            _lastRefreshFrame = Time.frameCount;
            _solver.Clear();
            _peers.Clear();

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((!connection.isAuthenticated) || (connection.identity == null))
                {
                    continue;
                }

                Vector3 position = connection.identity.transform.position;
                _solver.Add(connection.connectionId, position.x, position.z);

                if (connection.authenticationData is SessionBinding binding)
                {
                    _peers.Register(connection.connectionId, binding.ParticipantId);
                }
            }

            _peers.Party = _party;
            _solver.Compute(_peers);
        }

        private static bool IsPlayerObject(NetworkIdentity identity)
        {
            return (identity.connectionToClient != null) && (identity.connectionToClient.identity == identity);
        }

        // 파티 조회는 참가자 번호로 하고 풀은 연결 번호로 센다. 갱신마다 두 번호를 맞춰 준다
        private sealed class PeerMap : ITownPartyPeers
        {
            private readonly Dictionary<int, int> _participantByConnection = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _connectionByParticipant = new Dictionary<int, int>();
            private readonly List<int>            _members                 = new List<int>();

            public IChatPartyLookup Party { get; set; }

            public void Clear()
            {
                _participantByConnection.Clear();
                _connectionByParticipant.Clear();
            }

            public void Register(int connectionId, int participantId)
            {
                _participantByConnection[connectionId]  = participantId;
                _connectionByParticipant[participantId] = connectionId;
            }

            public bool TryGetPeers(int id, List<int> into)
            {
                if ((Party == null) || (!_participantByConnection.TryGetValue(id, out int participantId)))
                {
                    return false;
                }

                _members.Clear();
                if (!Party.TryGetPartyMembers(participantId, _members))
                {
                    return false;
                }

                for (int i = 0; i < _members.Count; ++i)
                {
                    if (_connectionByParticipant.TryGetValue(_members[i], out int connectionId))
                    {
                        into.Add(connectionId);
                    }
                }

                return true;
            }
        }
    }
}
