// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 서버가 확정한 이동 상태, 소유 클라이언트가 예측을 맞추는 기준이다 </summary>
    public readonly struct PlayerMoveAuthority : IEquatable<PlayerMoveAuthority>
    {
        // 예측 비교는 "같은 이동을 시작하고 같은 시간이 지난 시점"끼리 한다. 서버와 클라이언트는 이동을 시작한
        // 실제 시각이 지연만큼 다르므로 절대 시각 대신 이동 순번과 시작 뒤 경과 시간을 보낸다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _position;
        private readonly uint    _moveSequence;
        private readonly float   _moveElapsed;
        private readonly bool    _isMoving;
        private readonly bool    _isValid;

        /****************************************
        *              Properties
        ****************************************/

        public Vector3 Position => _position;

        // 서버가 마지막으로 경로를 시작한 이동 입력의 순번, 아직 없으면 0
        public uint MoveSequence => _moveSequence;

        // 그 이동을 시작한 뒤 이 위치까지 걸린 시간(초). 멈춘 뒤에는 멈춘 시점 값으로 고정된다
        public float MoveElapsed => _moveElapsed;

        public bool IsMoving => _isMoving;

        // 기본값(서버가 아직 보내지 않은 상태)과 원점에 선 실제 상태를 가른다
        public bool IsValid => _isValid;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerMoveAuthority(Vector3 position, uint moveSequence, float moveElapsed, bool isMoving)
        {
            _position     = position;
            _moveSequence = moveSequence;
            _moveElapsed  = moveElapsed;
            _isMoving     = isMoving;
            _isValid      = true;
        }

        // SyncVar가 매 틱 같은 값인지 비교한다. IEquatable이 없으면 비교마다 박싱이 생긴다
        public bool Equals(PlayerMoveAuthority other)
        {
            return (_position == other._position) && (_moveSequence == other._moveSequence)
                && (_moveElapsed == other._moveElapsed) && (_isMoving == other._isMoving) && (_isValid == other._isValid);
        }

        public override bool Equals(object obj)
        {
            return (obj is PlayerMoveAuthority other) && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_position, _moveSequence, _moveElapsed, _isMoving, _isValid);
        }
    }
}
