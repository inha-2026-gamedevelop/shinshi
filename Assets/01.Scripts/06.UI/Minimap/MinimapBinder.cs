// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;
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

            // 붕괴 연출은 서버 시각으로 평가되고 되돌리면 시각이 0이 되므로, 이 시각으로 지금 붕괴한 상태인지 본다
            BossArenaShatterView arena = BossArenaShatterView.Current;
            _view.SetCollapsed((arena != null) && (arena.EvaluatedTime > 0f));

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

            // 부적진과 색 결계는 표시 중인 목록을 그대로 읽고 기믹이 배정한 색으로 찍는다. 씬을 탐색하지 않는다
            IReadOnlyList<WolbaekTalismanPlayer> talismans = WolbaekTalismanPlayer.RunningPlayers;
            for (int i = 0; i < talismans.Count; ++i)
            {
                if ((talismans[i] != null)
                    && (_view.SetMarker(used, MinimapRules.ToXz(talismans[i].transform.position), EMinimapMarker.Gimmick, OpaqueColor(talismans[i].CurrentSnapshot.Color))))
                {
                    ++used;
                }
            }

            IReadOnlyList<WolbaekWardPlayer> wards = WolbaekWardPlayer.RunningPlayers;
            for (int i = 0; i < wards.Count; ++i)
            {
                if ((wards[i] != null)
                    && (_view.SetMarker(used, MinimapRules.ToXz(wards[i].transform.position), EMinimapMarker.Gimmick, OpaqueColor(wards[i].CurrentSnapshot.Color))))
                {
                    ++used;
                }
            }

            // 봉인 핵은 파괴되거나 만료되면 목록에서 빠지므로 끝난 핵은 자연히 사라진다
            IReadOnlyList<WolbaekSealCorePlayer> cores = WolbaekSealCorePlayer.RunningPlayers;
            for (int i = 0; i < cores.Count; ++i)
            {
                if ((cores[i] != null)
                    && (cores[i].CurrentSnapshot.Phase != WolbaekSealCorePlayer.EPhase.Destroyed)
                    && (_view.SetMarker(used, MinimapRules.ToXz(cores[i].transform.position), EMinimapMarker.Gimmick, OpaqueColor(cores[i].MarkerColor))))
                {
                    ++used;
                }
            }

            _view.HideMarkersFrom(used);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 기믹 색의 알파는 연출용이라 표시에는 쓰지 않고, 밝기 값이 1을 넘어도 한 번 눌러 화면 색으로 맞춘다
        private static Color OpaqueColor(Color color)
        {
            return new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
        }

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

            // 플레이어 루트는 회전하지 않고 모델만 복제된 바라보는 방향으로 돌기 때문에, 접속 중에는 그 값을 쓴다
            if ((connected) && (self.TryGetComponent(out PlayerCombatState combat)))
            {
                yaw = MinimapRules.DirectionToYaw(FacingRules.DecodeYaw(combat.FacingYaw));
            }

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
