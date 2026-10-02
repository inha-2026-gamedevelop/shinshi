// System
using System;
using System.Diagnostics;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

// UnityEngine.Debug와 System.Diagnostics.Debug가 겹친다
using Debug = UnityEngine.Debug;

namespace Shinshi.Dev
{
    /// <summary> 개발 클라이언트가 소유 플레이어를 무작위로 이동, 회피시키고 예측 보정 통계를 로그로 남긴다 </summary>
    public sealed class DevMoveAutopilot : MonoBehaviour
    {
        // 사람이 창마다 클릭하지 않아도 지연 조건별 예측 품질을 같은 방식으로 비교하려고 만들었다.
        // 판정은 서버가 하므로 여기서는 일반 입력과 같은 요청 창구만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const double MOVE_INTERVAL_SECONDS   = 1.2;
        private const double DODGE_INTERVAL_SECONDS  = 5.5;
        private const double AFTER_DODGE_PAUSE       = 0.6;
        private const double REPORT_INTERVAL_SECONDS = 5.0;
        private const float  ROAM_RADIUS             = 10f;

        private PlayerInputState _input      = null;
        private PlayerPrediction _prediction = null;
        private Vector3          _origin     = Vector3.zero;
        private System.Random    _random     = null;
        private double           _nextMove   = 0.0;
        private double           _nextDodge  = 0.0;
        private double           _nextReport = 0.0;

        /****************************************
        *              Unity Event
        ****************************************/

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !UNITY_SERVER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (!DevArgs.Has(Constants.Network.ARG_DEV_AUTOPILOT))
            {
                return;
            }

            var host = new GameObject(nameof(DevMoveAutopilot));
            DontDestroyOnLoad(host);
            host.AddComponent<DevMoveAutopilot>();
        }
#endif

        private void Update()
        {
            if (_input == null)
            {
                TryBind();
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (now >= _nextDodge)
            {
                _input.RequestDodge(RandomDirection());
                _nextDodge = now + DODGE_INTERVAL_SECONDS;
                _nextMove  = now + AFTER_DODGE_PAUSE;
            }
            else if (now >= _nextMove)
            {
                Vector3 offset = RandomDirection() * ((float)_random.NextDouble() * ROAM_RADIUS);
                _input.RequestMove(_origin + offset);
                _nextMove = now + MOVE_INTERVAL_SECONDS;
            }

            if (now >= _nextReport)
            {
                _nextReport = now + REPORT_INTERVAL_SECONDS;
                Report("중간");
            }
        }

        private void OnApplicationQuit()
        {
            if (_input != null)
            {
                Report("종료");
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TryBind()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return;
            }

            if ((!localPlayer.TryGetComponent(out _input)) || (!localPlayer.TryGetComponent(out _prediction)))
            {
                _input = null;
                return;
            }

            // 여러 클라이언트가 같은 경로를 밟지 않게 프로세스마다 다른 씨앗을 쓴다
            _random = new System.Random(Process.GetCurrentProcess().Id);
            _origin = localPlayer.transform.position;

            double now = Time.unscaledTimeAsDouble;
            _nextMove   = now;
            _nextDodge  = now + DODGE_INTERVAL_SECONDS;
            _nextReport = now + REPORT_INTERVAL_SECONDS;
            Debug.Log($"[DevAutopilot] 시작, 원점 {_origin}");
        }

        private Vector3 RandomDirection()
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            return new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }

        // 서 있을 때의 서버 차이는 0이어야 한다. 움직이는 중에는 지연만큼 차이가 나는 게 정상이라 함께 표시만 한다
        private void Report(string label)
        {
            float               travelled = _prediction.TravelledDistance;
            float               corrected = _prediction.CorrectedDistance;
            float               ratio     = (travelled > 0f) ? (corrected / travelled) : 0f;
            PlayerMoveAuthority authority = _input.MoveAuthority;
            float               gap       = authority.IsValid ? Vector3.Distance(_input.transform.position, authority.Position) : -1f;

            Debug.Log($"[DevAutopilot] {label} 이동 {travelled:F2}m, 보정 {corrected:F3}m ({ratio:P2}), 스냅 {_prediction.SnapCount}회, " +
                $"서버와 현재 차이 {gap:F3}m (서버 이동 중 {authority.IsMoving}), RTT {NetworkTime.rtt * 1000.0:F0}ms");
        }
    }
}
