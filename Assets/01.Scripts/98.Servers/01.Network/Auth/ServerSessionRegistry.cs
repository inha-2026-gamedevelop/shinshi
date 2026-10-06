// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 참가자 명단, 참가자 키에 번호를 붙이고 동시 접속과 유예 만료 뒤 재합류를 막는다 </summary>
    public sealed class ServerSessionRegistry : IParticipantRoster
    {
        // 던전은 같은 키가 프로세스 수명 동안 같은 번호를 받는다. 재접속한 참가자를 전투 상태 보존 기록과 이어 주는 열쇠다.
        // 던전 프로세스는 출정 하나만 맡고 끝나면 내려가므로 키가 쌓여도 파티 인원을 넘지 않는다.
        // 마을은 프로세스가 오래 살고 사람이 계속 바뀐다. 끊길 때 Forget으로 키를 지워야 상한이 동시 접속 수가 되고,
        // 마을에는 정산할 출정이 없으므로 Close로 재입장을 막지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<string, int> _idsByKey;
        private readonly HashSet<int>            _connected;
        private readonly HashSet<int>            _closed;
        private readonly HashSet<int>            _auctionReadmit;
        private readonly int                     _capacity;

        private int _lastParticipantId = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int ParticipantCount => _idsByKey.Count;
        public int ConnectedCount   => _connected.Count;

        /****************************************
        *                Methods
        ****************************************/

        public ServerSessionRegistry(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "참가자 상한은 1 이상이다");
            }

            _capacity  = capacity;
            _idsByKey  = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
            _connected = new HashSet<int>();
            _closed    = new HashSet<int>();

            _auctionReadmit = new HashSet<int>();
        }

        /// <summary> 검증된 참가자를 받는다, 받으면 연결에 붙일 결합을 돌려준다 </summary>
        public ERejectReason TryAdmit(in JoinTokenClaims claims, out SessionBinding binding)
        {
            binding = null;
            string key = claims.ParticipantKey;
            if (string.IsNullOrEmpty(key))
            {
                return ERejectReason.TokenRevoked;
            }

            if (!_idsByKey.TryGetValue(key, out int participantId))
            {
                if (_idsByKey.Count >= _capacity)
                {
                    return ERejectReason.NoCapacity;
                }

                ++_lastParticipantId;
                participantId = _lastParticipantId;
                _idsByKey.Add(key, participantId);
            }

            // 유예가 끝난 참가자는 개인 실패 정산 대상이라 다시 들이지 않는다. 출정이 끝나 닫힌 참가자는 경매가 열려 있는 동안만
            // 경매 전용으로 다시 받는다(던전 호스트가 경매 시작 때 허용한 번호)
            bool auctionOnly = false;
            if (_closed.Contains(participantId))
            {
                if (!_auctionReadmit.Contains(participantId))
                {
                    return ERejectReason.AlreadySettled;
                }

                auctionOnly = true;
            }

            // 이전 연결의 끊김을 서버가 아직 감지하지 못했으면 여기서 거절된다. 클라이언트는 잠시 뒤 다시 시도한다
            if (_connected.Contains(participantId))
            {
                return ERejectReason.AlreadyConnected;
            }

            _connected.Add(participantId);
            binding = new SessionBinding(key, participantId, claims.AccountLeaseId, claims.PlayerClass,
                claims.CharacterName, claims.Appearance);
            binding.AccountId   = claims.AccountId;
            binding.CharacterId = claims.CharacterId;

            binding.CombatLoadout   = claims.CombatLoadout;
            binding.ReservedPotions = claims.ReservedPotions;
            binding.Sortie          = claims.Sortie;
            binding.IsAuctionOnly   = auctionOnly;
            return ERejectReason.None;
        }

        /// <summary> 연결이 끊길 때 부른다, 같은 결합으로 두 번 불러도 한 번만 처리한다 </summary>
        public void Release(SessionBinding binding)
        {
            if ((binding == null) || (!binding.IsActive))
            {
                return;
            }

            binding.IsActive = false;
            _connected.Remove(binding.ParticipantId);
        }

        /// <summary> 마을에서 연결이 끊길 때 부른다, 접속 표시와 키 번호를 함께 지워 같은 키는 다음 입장에 새 번호를 받는다 </summary>
        public void Forget(SessionBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            Release(binding);

            // 같은 키로 새 연결이 이미 다른 번호를 받았으면 그 번호를 지우지 않는다
            if ((_idsByKey.TryGetValue(binding.ParticipantKey, out int participantId))
                && (participantId == binding.ParticipantId) && (!_connected.Contains(participantId)))
            {
                _idsByKey.Remove(binding.ParticipantKey);
            }
        }

        public bool IsConnected(int participantId)
        {
            return _connected.Contains(participantId);
        }

        public bool IsClosed(int participantId)
        {
            return _closed.Contains(participantId);
        }

        public void Close(int participantId)
        {
            _closed.Add(participantId);
        }

        /// <summary> 출정 종료로 닫힌 참가자를 경매 동안 경매 전용으로 다시 받게 한다, 던전 호스트가 경매 시작 때 부른다 </summary>
        public void AllowAuctionReadmission(int participantId)
        {
            _auctionReadmit.Add(participantId);
        }

        /// <summary> 경매가 끝나면 부른다, 이후 닫힌 참가자는 다시 AlreadySettled로 거절한다 </summary>
        public void RevokeAuctionReadmission()
        {
            _auctionReadmit.Clear();
        }

        public bool AllowsAuctionReadmission(int participantId)
        {
            return _auctionReadmit.Contains(participantId);
        }
    }
}
