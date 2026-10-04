// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 채집 판정 수치, 원본은 LifeConfigSO다. 시험은 값을 직접 넣는다 </summary>
    public readonly struct GatherSettings
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _interactRange;
        private readonly float _gatherSeconds;
        private readonly float _claimTimeoutSeconds;
        private readonly float _disconnectReleaseSeconds;
        private readonly float _regrowSeconds;

        /****************************************
        *              Properties
        ****************************************/

        public float InteractRange            => _interactRange;
        public float GatherSeconds            => _gatherSeconds;
        public float ClaimTimeoutSeconds      => _claimTimeoutSeconds;
        public float DisconnectReleaseSeconds => _disconnectReleaseSeconds;
        public float RegrowSeconds            => _regrowSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public GatherSettings(float interactRange, float gatherSeconds, float claimTimeoutSeconds, float disconnectReleaseSeconds,
            float regrowSeconds)
        {
            _interactRange            = interactRange;
            _gatherSeconds            = gatherSeconds;
            _claimTimeoutSeconds      = claimTimeoutSeconds;
            _disconnectReleaseSeconds = disconnectReleaseSeconds;
            _regrowSeconds            = regrowSeconds;
        }

        public static GatherSettings FromConfig(LifeConfigSO life)
        {
            if (life == null)
            {
                throw new ArgumentNullException(nameof(life));
            }

            return new GatherSettings(life.InteractRange, life.GatherSeconds, life.ClaimTimeoutSeconds, life.DisconnectReleaseSeconds,
                life.RegrowSeconds);
        }
    }
}
