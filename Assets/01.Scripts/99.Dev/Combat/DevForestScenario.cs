#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.Dev
{
    /// <summary> 독립 개발 클라이언트로 파티 출정과 숲 전투를 수행하고 서버가 보낸 종료 결과를 기록한다 </summary>
    public sealed class DevForestScenario : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TIMEOUT_SECONDS = 1200f;
        private const float INPUT_INTERVAL = 0.65f;
        private readonly Dictionary<uint, EnemyNetState> _enemies = new Dictionary<uint, EnemyNetState>();
        private readonly HashSet<uint> _dead = new HashSet<uint>();
        private string _output;
        private string _gate;
        private string _step = "session";
        private bool _finished;
        private bool _intro;
        private int _zone;
        private float _started;
        private float _lastReport;
        private float _nextSkill;
        private float _nextBurst;
        private byte _elementSlot;
        private bool _sawEnemies;
        private bool _movingToNext;
        private string _group;
        private int _groupRoom;
        private bool _leader;
        private string _progressFile;
        private NetworkIdentity _identity;
        private PlayerInputState _input;
        private PlayerCombatState _combat;
        private DungeonSpawnLayout _layout;
        private ShinshiNetworkManager _manager;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (!DevArgs.Has("-dev-forest-scenario"))
            {
                return;
            }

            var host = new GameObject(nameof(DevForestScenario));
            DontDestroyOnLoad(host);
            host.AddComponent<DevForestScenario>();
        }

        private void Start()
        {
            DevArgs.TryReadString("-dev-forest-output", out _output);
            DevArgs.TryReadString("-dev-forest-gate", out _gate);
            DevArgs.TryReadString("-dev-forest-group", out _group);
            DevArgs.TryReadString("-dev-forest-progress", out _progressFile);
            _leader = string.IsNullOrEmpty(_group) || DevArgs.Has("-dev-forest-leader");
            _started = Time.realtimeSinceStartup;
            Application.targetFrameRate = 30;
            TownProgressClient.IntroResultReceived += OnIntro;
            TownPartyClient.SortieTicketReceived += OnTicket;
            TownPartyClient.RoomListReceived += OnRooms;
            SortieClient.StatusChanged += OnStatus;
            StartCoroutine(Run());
        }

        private void Update()
        {
            if ((!_finished) && ((Time.realtimeSinceStartup - _started) > TIMEOUT_SECONDS))
            {
                Finish(false, "timeout:" + _step);
            }
        }

        private void OnDestroy()
        {
            TownProgressClient.IntroResultReceived -= OnIntro;
            TownPartyClient.SortieTicketReceived -= OnTicket;
            TownPartyClient.RoomListReceived -= OnRooms;
            SortieClient.StatusChanged -= OnStatus;
        }

        /****************************************
        *                Methods
        ****************************************/

        private IEnumerator Run()
        {
            yield return new WaitUntil(() => LauncherSession.HasSession);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == Constants.Scene.LOBBY);
            var lobby = FindAnyObjectByType<LobbyScreen>();
            yield return new WaitUntil(() => lobby.StartButton.interactable);
            lobby.StartButton.onClick.Invoke();
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == Constants.Scene.CHARACTER_SELECT);
            yield return null;
            var select = FindAnyObjectByType<CharacterSelectScreen>();
            yield return new WaitUntil(() => (select.IsLoaded) && (!select.IsBusy));
            int selectedSlot = 0;
            if (DevArgs.TryReadString("-dev-forest-character", out string characterName))
            {
                selectedSlot = -1;
                for (int i = 0; i < select.Characters.Count; ++i)
                {
                    if (select.Characters[i].Name == characterName)
                    {
                        selectedSlot = i;
                        break;
                    }
                }
            }

            if (selectedSlot < 0)
            {
                Finish(false, "character-not-found");
                yield break;
            }

            select.Selection.Select(selectedSlot);
            yield return new WaitUntil(() => select.StartButton.interactable);
            select.StartButton.onClick.Invoke();
            _step = "town";
            yield return new WaitUntil(() => NetworkClient.localPlayer != null);
            _manager = (ShinshiNetworkManager)NetworkManager.singleton;
            TownProgressClient.CompleteIntro();
            yield return new WaitUntil(() => _intro);
            if (_leader)
            {
                TownPartyClient.CreateRoom(_group ?? ("ForestTest" + System.Diagnostics.Process.GetCurrentProcess().Id),
                    EPartyMode.Normal, "region_forest", null);
            }
            else
            {
                while (_groupRoom == 0)
                {
                    TownPartyClient.RequestRoomList();
                    yield return new WaitForSecondsRealtime(1f);
                }

                TownPartyClient.JoinRoom(_groupRoom, null);
            }

            yield return new WaitUntil(() => TownPartyClient.Room.InRoom);
            int memberCount = string.IsNullOrEmpty(_group) ? 1 : 4;
            yield return new WaitUntil(() => TownPartyClient.Room.Members.Length == memberCount);
            TownPartyClient.SetReady(true, 0, 0);
            yield return new WaitUntil(AllReady);
            _step = "gate";
            Record("ready room=" + TownPartyClient.Room.RoomId);
            File.WriteAllText(_output + ".ready", "ready");
            yield return new WaitUntil(() => File.Exists(_gate));
            if (_leader)
            {
                TownPartyClient.StartSortie();
            }
            _step = "depart";
            yield return new WaitUntil(() => (SortieClient.HasStatus) && (SortieClient.Current.Phase == ESortiePhase.Running));
            _step = "combat";
            Record("running");
            while ((!_finished) && (!SortieClient.Current.CanReturn))
            {
                Fight();
                yield return new WaitForSecondsRealtime(INPUT_INTERVAL);
            }

            bool success = (SortieClient.Current.EndReason == ESessionEndReason.Success)
                && (SortieClient.Current.SettlementState == ESortieSettlementState.Settled);
            string result = "end=" + SortieClient.Current.EndReason + " settlement=" + SortieClient.Current.SettlementState;
            _step = "return";
            var flow = new SortieReturnFlow(new LobbyApiClient(), _manager,
                (ShinshiClientAuthenticator)_manager.authenticator);
            ERejectReason returned = ERejectReason.BackendUnavailable;
            yield return flow.Run(LobbyFlow.ActiveCharacterId, reason => returned = reason);
            Finish((success) && (returned == ERejectReason.None), result + " return=" + returned);
        }

        private void Fight()
        {
            if (NetworkClient.localPlayer != _identity)
            {
                _identity = NetworkClient.localPlayer;
                if (_identity != null)
                {
                    _input = _identity.GetComponent<PlayerInputState>();
                    _combat = _identity.GetComponent<PlayerCombatState>();
                    _layout = DungeonSpawnLayout.Current;
                }
            }

            if ((_input == null) || (_layout == null))
            {
                return;
            }

            if (_combat.LifeState != EParticipantLifeState.Alive)
            {
                _input.RequestRevive();
                return;
            }

            if ((string.IsNullOrEmpty(_progressFile)) && (_movingToNext) && (_layout.TryGetNextWave(_zone, out Vector3 arrival))
                && (Vector3.Distance(_input.transform.position, arrival) < 3.5f))
            {
                ++_zone;
                _movingToNext = false;
                _sawEnemies = false;
                Record("arrival zone=" + _zone);
            }

            foreach (var pair in NetworkClient.spawned)
            {
                if (!_enemies.ContainsKey(pair.Key))
                {
                    _enemies.Add(pair.Key, pair.Value.GetComponent<EnemyNetState>());
                }
            }

            EnemyNetState nearest = null;
            float distance = float.MaxValue;
            foreach (var pair in _enemies)
            {
                EnemyNetState enemy = pair.Value;
                if (enemy == null)
                {
                    continue;
                }

                if (enemy.LifeState != EParticipantLifeState.Alive)
                {
                    _dead.Add(pair.Key);
                    continue;
                }

                float candidate = (enemy.transform.position - _input.transform.position).sqrMagnitude;
                if (candidate < distance)
                {
                    distance = candidate;
                    nearest = enemy;
                }
            }

            if ((Time.realtimeSinceStartup - _lastReport) > 10f)
            {
                _lastReport = Time.realtimeSinceStartup;
                Record("combat zone=" + _zone + " dead=" + _dead.Count + " hp=" + _combat.Health
                    + " pos=" + _input.transform.position + " nearest=" + Mathf.Sqrt(distance));
            }

            if (nearest != null)
            {
                _sawEnemies = true;
                Vector3 aim = nearest.transform.position - _input.transform.position;
                aim.y = 0f;
                bool ranged = _combat.PlayerClass == EPlayerClass.Archer;
                if ((distance < 4f) && (_combat.Health < _combat.MaxHealth * 0.3f)
                    && (NetworkTime.time >= _input.DodgeReadyAt))
                {
                    _input.RequestDodge(-aim.normalized);
                }
                else if ((string.IsNullOrEmpty(_group)) && (distance < 16f) && NavMesh.SamplePosition(_input.transform.position - aim.normalized * 6f,
                    out NavMeshHit retreat, 3f, NavMesh.AllAreas))
                {
                    _input.RequestMove(retreat.position);
                }
                else if (distance > (ranged ? 81f : 4f))
                {
                    MoveToward(nearest.transform.position - aim.normalized * (ranged ? 7f : 1.5f));
                }
                else if (((!ranged) || (_combat.ClassStacks >= 20)) && (NetworkTime.time >= _input.SkillRReadyAt)
                    && (Time.realtimeSinceStartup >= _nextBurst))
                {
                    _input.RequestSkill(ESkillRequestKind.Regular, 3, nearest.transform.position, aim.normalized);
                    _nextBurst = Time.realtimeSinceStartup + 5f;
                }
                else if (Time.realtimeSinceStartup >= _nextSkill)
                {
                    _input.RequestSkill(ESkillRequestKind.Regular, _elementSlot, _input.transform.position, aim.normalized);
                    _elementSlot = (byte)((_elementSlot + 1) % 3);
                    _nextSkill = Time.realtimeSinceStartup + (ranged ? 8f : 2f);
                }
                else
                {
                    _input.RequestAttack(aim.normalized);
                }
            }
            else if (!string.IsNullOrEmpty(_progressFile))
            {
                if ((TryReadProgress(out int cleared))
                    && (cleared > 0) && (cleared < _layout.ZoneCount)
                    && (_layout.TryGetNextWave(cleared - 1, out Vector3 destination)))
                {
                    _zone = cleared;
                    MoveToward(destination);
                }
            }
            else if ((_sawEnemies) && (_layout.TryGetNextWave(_zone, out Vector3 next)))
            {
                _movingToNext = true;
                MoveToward(next);
            }
        }

        private void MoveToward(Vector3 destination)
        {
            if (!NavMesh.SamplePosition(destination, out NavMeshHit target, 3f, NavMesh.AllAreas))
            {
                return;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(_input.transform.position, target.position, NavMesh.AllAreas, path))
            {
                return;
            }

            Vector3[] corners = path.corners;
            if (corners.Length < 2)
            {
                return;
            }

            Vector3 step = Vector3.MoveTowards(_input.transform.position, corners[1],
                Constants.Player.MAX_MOVE_DESTINATION_DISTANCE * 0.5f);
            _input.RequestMove(step);
        }

        private bool TryReadProgress(out int cleared)
        {
            cleared = 0;
            try
            {
                return (File.Exists(_progressFile)) && (int.TryParse(File.ReadAllText(_progressFile), out cleared));
            }
            catch (IOException)
            {
                return false;
            }
        }

        private void OnIntro(IntroCompleteResultMessage message)
        {
            _intro = message.IsAccepted;
            if (!_intro)
            {
                Finish(false, "intro=" + message.Reason);
            }
        }

        private static bool AllReady()
        {
            foreach (PartyMemberView member in TownPartyClient.Room.Members)
            {
                if (member.Ready != EPartyReadyState.Ready)
                {
                    return false;
                }
            }

            return TownPartyClient.Room.Members.Length > 0;
        }

        private void OnRooms(PartyRoomListMessage message)
        {
            foreach (PartyRoomSummary room in message.Rooms)
            {
                if (room.Name == _group)
                {
                    _groupRoom = room.RoomId;
                    return;
                }
            }
        }

        private void OnTicket(SortieTicketMessage message)
        {
            Record("ticket session=" + message.SessionId + " instance=" + message.InstanceId + " port=" + message.Port);
        }

        private void OnStatus(SortieStatusMessage message)
        {
            Record("status=" + message.Phase + " end=" + message.EndReason + " settled=" + message.SettlementState
                + " canReturn=" + message.CanReturn);
        }

        private void Record(string message)
        {
            string line = DateTime.UtcNow.ToString("O") + " " + message;
            Debug.Log("[ForestScenario] " + line);
            if (!string.IsNullOrEmpty(_output))
            {
                File.AppendAllText(_output, line + Environment.NewLine);
            }
        }

        private void Finish(bool success, string reason)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Record("finished success=" + success + " " + reason);
            NetworkManager.singleton?.StopClient();
            Application.Quit(success ? 0 : 1);
        }
    }
}
#endif
