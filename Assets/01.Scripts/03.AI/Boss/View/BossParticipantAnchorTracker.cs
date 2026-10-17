// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 월백 기믹 표현(50% 연결선, 25% 추적 여우불, 0% 추적 빨간 여우불)은 서버가 사건에 실은 참가자 전투 번호를 화면의 몸체에 이어야 그린다.
    // 클라이언트가 가진 참가자 몸체는 스폰된 PlayerCombatState뿐이라 그 전투 번호와 Transform을 짝지어 표현에 넣는다(앵커).
    // 앵커는 위치를 보여 주는 데만 쓰고 판정에는 쓰지 않는다. 위치 판정은 서버 사건(고정, 분리 위치)이 정한다.
    //
    // 스폰 목록을 매 프레임 돌지 않고 REFRESH_SECONDS마다 한 번 본다. 새 참가자, 사라진 참가자, 전투 번호가 늦게 복제된 참가자를
    // 그 주기 안에 따라잡는다. 표현은 기믹 시작과 복원 때 앵커를 비우므로, 그 직후에는 주기를 기다리지 않고 전부 다시 넣는다(InjectAll).
    // 처음 본 개체만 TryGetComponent로 확인하고 결과를 netId로 캐시한다
    /// <summary> 스폰된 참가자의 전투 번호와 발밑 Transform을 월백 기믹 표현의 앵커로 공급한다 </summary>
    public sealed class BossParticipantAnchorTracker
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REFRESH_SECONDS = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 앵커를 넣는다, anchor가 null이면 그 참가자의 앵커를 지운다 </summary>
        public delegate void AnchorSetter(int combatId, Transform anchor);

        private readonly AnchorSetter                          _setter;
        private readonly Dictionary<uint, PlayerCombatState>   _byNetId   = new Dictionary<uint, PlayerCombatState>();
        private readonly Dictionary<int, Transform>            _anchors   = new Dictionary<int, Transform>();
        private readonly Dictionary<int, Transform>            _scan      = new Dictionary<int, Transform>();
        private readonly List<uint>                            _staleIds  = new List<uint>();
        private readonly List<int>                             _removed   = new List<int>();

        private double _nextRefresh = double.NegativeInfinity;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 알고 있는 앵커 수 </summary>
        public int Count => _anchors.Count;

        /****************************************
        *                Methods
        ****************************************/

        public BossParticipantAnchorTracker(AnchorSetter setter)
        {
            _setter = setter;
        }

        /// <summary> 주기가 됐으면 클라이언트 스폰 목록을 다시 본다, now는 로컬 시각이다 </summary>
        public void Tick(double now)
        {
            if (now < _nextRefresh)
            {
                return;
            }

            _nextRefresh = now + REFRESH_SECONDS;
            Refresh(NetworkClient.spawned);
        }

        // 바뀐 앵커만 넣고 사라진 참가자의 앵커는 지운다
        /// <summary> 스폰 목록으로 앵커를 다시 맞춘다 </summary>
        public void Refresh(Dictionary<uint, NetworkIdentity> spawned)
        {
            _scan.Clear();
            if (spawned != null)
            {
                Collect(spawned);
            }

            _removed.Clear();
            foreach (KeyValuePair<int, Transform> pair in _anchors)
            {
                if (!_scan.ContainsKey(pair.Key))
                {
                    _removed.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removed.Count; ++i)
            {
                _anchors.Remove(_removed[i]);
                _setter?.Invoke(_removed[i], null);
            }

            foreach (KeyValuePair<int, Transform> pair in _scan)
            {
                if (_anchors.TryGetValue(pair.Key, out Transform known) && (known == pair.Value))
                {
                    continue;
                }

                _anchors[pair.Key] = pair.Value;
                _setter?.Invoke(pair.Key, pair.Value);
            }
        }

        /// <summary> 알고 있는 앵커를 모두 다시 넣는다, 표현이 기믹 시작이나 복원으로 앵커를 비운 직후에 부른다 </summary>
        public void InjectAll()
        {
            if (_setter == null)
            {
                return;
            }

            foreach (KeyValuePair<int, Transform> pair in _anchors)
            {
                _setter(pair.Key, pair.Value);
            }
        }

        /// <summary> 다음 Tick에 바로 스폰 목록을 다시 보게 한다 </summary>
        public void RequestRefresh()
        {
            _nextRefresh = double.NegativeInfinity;
        }

        /// <summary> 연결이 끊기거나 다른 보스에 붙을 때 기록을 비운다, 표현의 앵커는 표현이 스스로 비운다 </summary>
        public void Clear()
        {
            _byNetId.Clear();
            _anchors.Clear();
            _scan.Clear();
            _nextRefresh = double.NegativeInfinity;
        }

        private void Collect(Dictionary<uint, NetworkIdentity> spawned)
        {
            _staleIds.Clear();
            foreach (KeyValuePair<uint, PlayerCombatState> pair in _byNetId)
            {
                if ((!spawned.TryGetValue(pair.Key, out NetworkIdentity identity)) || (identity == null))
                {
                    _staleIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < _staleIds.Count; ++i)
            {
                _byNetId.Remove(_staleIds[i]);
            }

            foreach (KeyValuePair<uint, NetworkIdentity> pair in spawned)
            {
                NetworkIdentity identity = pair.Value;
                if (identity == null)
                {
                    continue;
                }

                if (!_byNetId.TryGetValue(pair.Key, out PlayerCombatState player))
                {
                    // 플레이어가 아닌 개체도 null로 캐시해 다음 주기에 다시 찾지 않는다
                    identity.TryGetComponent(out player);
                    _byNetId[pair.Key] = player;
                }

                if ((player == null) || (player.CombatId == CombatIds.NONE))
                {
                    continue;
                }

                _scan[player.CombatId] = player.transform;
            }
        }
    }
}
