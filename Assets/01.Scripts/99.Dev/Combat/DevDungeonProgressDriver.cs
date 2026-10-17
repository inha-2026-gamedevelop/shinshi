// Unity
using UnityEngine;

// 프로젝트
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 시험 스포너의 구역별 처치 상황을 던전 진행 패널에 그대로 옮겨 주는 시험용 연결부 </summary>
    public sealed class DevDungeonProgressDriver : MonoBehaviour
    {
        // 실제로는 서버가 웨이브 진행을 내려 준다. 서버가 없는 시험 씬에서는 스포너가 센 값을 그 대역으로 쓴다.
        // 한 칸은 한 구역이며, 앞에서부터 모두 처치한 구역은 완료, 그 다음 구역이 현재, 나머지는 대기다

        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private DevHitFeedbackSpawner _spawner = null;
        [SerializeField] private DungeonProgressView   _view    = null;

        [Header("규칙")]
        [SerializeField, Min(1f)] private float _timeLimitSeconds = 420f; // 첫 웨이브 시작부터의 제한 시간

        private float _finishedRemain = -1f; // 전부 처치한 순간의 남은 시간, 이후 시간을 멈춘다

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_spawner == null) || (_view == null))
            {
                Debug.LogError("[DevProgress] 스포너나 진행 패널이 연결되지 않았다", this);
                enabled = false;
            }
        }

        private void Update()
        {
            int zoneCount = _spawner.ZoneCount;
            if (zoneCount == 0)
            {
                return;
            }

            int   totalPlanned = 0;
            int   totalKilled  = 0;
            int   current      = -1; // 앞에서부터 처음으로 처치가 끝나지 않은 구역
            for (int zone = 0; zone < zoneCount; ++zone)
            {
                int planned = _spawner.GetZonePlanned(zone);
                int killed  = _spawner.GetZoneKilled(zone);
                totalPlanned += planned;
                totalKilled  += killed;

                if ((current < 0) && (!_spawner.IsZoneCleared(zone)))
                {
                    current = zone;
                }
            }

            bool allCleared = (current < 0);

            for (int zone = 0; zone < zoneCount; ++zone)
            {
                EWaveNodeState state = (zone == current) ? EWaveNodeState.Current
                                     : (allCleared || (zone < current)) ? EWaveNodeState.Cleared
                                     : EWaveNodeState.Pending;

                _view.SetNode(zone, state, _spawner.GetZoneKilled(zone), _spawner.GetZonePlanned(zone));
            }

            _view.SetWaveCount(allCleared ? zoneCount : (current + 1), zoneCount);
            _view.SetOverall((totalPlanned > 0) ? ((float)totalKilled / totalPlanned) : 0f);
            _view.SetTime(GetRemainSeconds(allCleared), _timeLimitSeconds);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 첫 무리를 세우기 전에는 제한 시간이 흐르지 않고, 모두 처치하면 그 순간에 멈춘다
        private float GetRemainSeconds(bool allCleared)
        {
            if (_finishedRemain >= 0f)
            {
                return _finishedRemain;
            }

            float first = _spawner.FirstSpawnTime;
            if (first < 0f)
            {
                return _timeLimitSeconds;
            }

            float remain = Mathf.Max(0f, _timeLimitSeconds - (Time.time - first));
            if (allCleared)
            {
                _finishedRemain = remain;
            }

            return remain;
        }
    }
}
