#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>
    /// Dev_SortiePrep 씬에서 출정 준비 화면을 가짜 파티로 시험한다.
    /// 난이도를 바꾸면 준비가 풀리고, 방장은 전원이 준비하면 3초 카운트다운 뒤 시작한다. 키 L은 방장/참가자 전환
    /// </summary>
    public sealed class SortiePrepDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    COUNTDOWN_SECONDS = 3;
        private const float  COUNTDOWN_STEP    = 1f;
        private const string DUNGEON_NAME      = "월백 결계";
        private const string DUNGEON_SUMMARY   = "보스 토벌 · 최대 4인";
        private const int    LEADER_LEVEL      = 32;
        private const int    LEADER_POWER      = 15820;
        private const int    READY_LEVEL       = 30;
        private const int    READY_POWER       = 14260;
        private const int    GUEST_LEVEL       = 28;
        private const int    GUEST_POWER       = 12940;

        [SerializeField] private SortiePrepView _view       = null;
        [SerializeField] private Sprite         _dungeonArt = null;

        private EBossDifficulty _difficulty    = EBossDifficulty.Easy;
        private bool            _isLeader      = true;
        private bool            _isReady       = false;
        private Coroutine       _countdown     = null;
        private WaitForSeconds  _waitCountdown = null;

        private readonly List<SortieMemberData> _members = new List<SortieMemberData>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { _waitCountdown = new WaitForSeconds(COUNTDOWN_STEP); }

        private void OnEnable()
        {
            _view.DifficultyChanged += OnDifficultyChanged;
            _view.ReadyClicked      += OnReadyClicked;
            _view.StartClicked      += OnStartClicked;
            _view.LeaveClicked      += OnLeaveClicked;
        }

        private void OnDisable()
        {
            _view.DifficultyChanged -= OnDifficultyChanged;
            _view.ReadyClicked      -= OnReadyClicked;
            _view.StartClicked      -= OnStartClicked;
            _view.LeaveClicked      -= OnLeaveClicked;
        }

        private void Start()
        {
            _view.SetDungeon(DUNGEON_NAME, DUNGEON_SUMMARY, _dungeonArt);
            Refresh();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L))
            {
                _isLeader = !_isLeader;
                Refresh();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnDifficultyChanged(EBossDifficulty difficulty)
        {
            _difficulty = difficulty;
            ClearReady();
        }

        private void OnReadyClicked()
        {
            _isReady = !_isReady;
            Refresh();
        }

        private void OnStartClicked()
        {
            if (_countdown != null)
            {
                return;
            }

            _countdown = StartCoroutine(RunCountdown());
        }

        private void OnLeaveClicked() { _view.SetNotice("방에서 나갑니다 (시험 화면이라 동작하지 않음)"); }

        private void ClearReady()
        {
            _isReady = false;

            if (_countdown != null)
            {
                StopCoroutine(_countdown);
                _countdown = null;
            }

            Refresh();
        }

        private IEnumerator RunCountdown()
        {
            for (int s = COUNTDOWN_SECONDS; s > 0; --s)
            {
                _view.SetCountdown(s);
                yield return _waitCountdown;
            }

            _view.SetCountdown(0);
            _view.SetNotice("출정합니다 (시험 화면이라 장면은 바뀌지 않음)");
            _countdown = null;
        }

        private void Refresh()
        {
            _view.SetDifficulty(_difficulty);
            _view.SetCountdown(0);

            BuildMembers();
            _view.SetMembers(_members);
            _view.SetLocalState(_isLeader, _isReady);

            bool allReady = true;

            for (int i = 0; i < _members.Count; ++i)
            {
                if (!_members[i].IsEmpty && !_members[i].IsLeader && !_members[i].IsReady)
                {
                    allReady = false;
                }
            }

            _view.SetStartEnabled(allReady);
            _view.SetNotice(allReady ? ERejectReason.None : ERejectReason.NotAllReady);
        }

        private void BuildMembers()
        {
            _members.Clear();
            _members.Add(Member("진욱", EPlayerClass.Warrior, true, true, LEADER_LEVEL, LEADER_POWER));
            _members.Add(Member("민성", EPlayerClass.Sorcerer, false, true, READY_LEVEL, READY_POWER));
            _members.Add(Member("기웅", EPlayerClass.Summoner, false, _isReady, GUEST_LEVEL, GUEST_POWER));
            // 네 번째 자리는 빈 자리 표시를 보려고 비워 둔다
            _members.Add(default(SortieMemberData));
        }

        private static SortieMemberData Member(string name, EPlayerClass playerClass, bool leader, bool ready,
            int level, int combatPower)
        {
            return new SortieMemberData(name, playerClass, leader, ready, true, true, level, combatPower);
        }
    }
}
#endif
