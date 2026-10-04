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
    /// 선택을 바꾸면 준비가 풀리고, 방장은 전원이 준비하면 3초 카운트다운 뒤 시작한다. 키 L은 방장/참가자 전환
    /// </summary>
    public sealed class SortiePrepDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    COUNTDOWN_SECONDS = 3;
        private const int    TICKET_HAVE       = 2;
        private const int    TICKET_NEED       = 1;
        private const int    SMALL_STOCK       = 12;
        private const int    LARGE_STOCK       = 4;
        private const string WAVE_REGION       = "숲 토벌 (6웨이브)";
        private const string BOSS_REGION       = "월백 결계";

        [SerializeField] private SortiePrepView _view = null;

        private ESortieMode     _mode       = ESortieMode.Boss;
        private EBossDifficulty _difficulty = EBossDifficulty.Easy;
        private bool            _isLeader   = true;
        private bool            _isReady    = false;
        private int             _small      = 6;
        private int             _large      = 2;
        private Coroutine       _countdown  = null;

        private readonly List<SortieMemberData> _members = new List<SortieMemberData>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _view.ModeChanged       += OnModeChanged;
            _view.DifficultyChanged += OnDifficultyChanged;
            _view.PotionChanged     += OnPotionChanged;
            _view.ReadyClicked      += OnReadyClicked;
            _view.StartClicked      += OnStartClicked;
            _view.LeaveClicked      += OnLeaveClicked;
        }

        private void OnDisable()
        {
            _view.ModeChanged       -= OnModeChanged;
            _view.DifficultyChanged -= OnDifficultyChanged;
            _view.PotionChanged     -= OnPotionChanged;
            _view.ReadyClicked      -= OnReadyClicked;
            _view.StartClicked      -= OnStartClicked;
            _view.LeaveClicked      -= OnLeaveClicked;
        }

        private void Start()
        {
            _view.SetEquipment(BuildEquipment());
            _view.SetTalismans(BuildTalismans());
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

        private void OnModeChanged(ESortieMode mode)
        {
            _mode = mode;
            ClearReady();
        }

        private void OnDifficultyChanged(EBossDifficulty difficulty)
        {
            _difficulty = difficulty;
            ClearReady();
        }

        private void OnPotionChanged(EPotionSize size, int carry)
        {
            if (size == EPotionSize.Small)
            {
                _small = carry;
            }
            else
            {
                _large = carry;
            }

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
                yield return new WaitForSeconds(1f);
            }

            _view.SetCountdown(0);
            _view.SetNotice("출정합니다 (시험 화면이라 장면은 바뀌지 않음)");
            _countdown = null;
        }

        private void Refresh()
        {
            bool boss = (_mode == ESortieMode.Boss);
            _view.SetMode(_mode);
            _view.SetRegion(boss ? BOSS_REGION : WAVE_REGION);
            _view.SetDifficulty(_difficulty);
            _view.SetTicket(TICKET_HAVE, boss ? TICKET_NEED : 0);
            _view.SetPotion(EPotionSize.Small, _small, SMALL_STOCK);
            _view.SetPotion(EPotionSize.Large, _large, LARGE_STOCK);
            _view.SetCountdown(0);

            BuildMembers();
            _view.SetMembers(_members);
            _view.SetLocalState(_isLeader, _isReady);

            bool allReady = true;

            for (int i = 0; i < _members.Count; ++i)
            {
                if (!_members[i].IsLeader && !_members[i].IsReady)
                {
                    allReady = false;
                }
            }

            _view.SetStartEnabled(allReady && (!boss || (TICKET_HAVE >= TICKET_NEED)));
            _view.SetNotice(allReady ? ERejectReason.None : ERejectReason.NotAllReady);
        }

        private void BuildMembers()
        {
            _members.Clear();
            _members.Add(Member("진욱", EPlayerClass.Warrior, true, true));
            _members.Add(Member("민성", EPlayerClass.Sorcerer, false, true));
            _members.Add(Member("기웅", EPlayerClass.Summoner, false, _isReady));
            _members.Add(Member("서연", EPlayerClass.Warrior, false, false));
        }

        private SortieMemberData Member(string name, EPlayerClass playerClass, bool leader, bool ready)
        {
            SortieMemberData data = new SortieMemberData();
            data.Name              = name;
            data.PlayerClass       = playerClass;
            data.IsLeader          = leader;
            data.IsReady           = ready;
            data.IsConnected       = true;
            data.MeetsRequirements = true;
            return data;
        }

        private static List<ItemSlotData> BuildEquipment()
        {
            return new List<ItemSlotData>
            {
                Item("equip_weapon_epic", EItemGrade.Epic, "무기(영웅)"),
                Item("equip_head_rare", EItemGrade.Rare, "머리(희귀)"),
                Item("equip_body_epic", EItemGrade.Epic, "몸(영웅)"),
                Item("equip_hands_rare", EItemGrade.Rare, "손(희귀)"),
                Item("equip_feet_common", EItemGrade.Common, "발(일반)"),
                Item("equip_accessory_legendary", EItemGrade.Legendary, "장신구(전설)")
            };
        }

        private static List<ItemSlotData> BuildTalismans()
        {
            return new List<ItemSlotData>
            {
                Item("talisman_kind_00", EItemGrade.Rare, "쿨다운 감소"),
                Item("talisman_kind_08", EItemGrade.Rare, "피해 증가"),
                Item("talisman_kind_14", EItemGrade.Epic, "후속 타격"),
                Item("talisman_kind_16", EItemGrade.Epic, "관통 돌진")
            };
        }

        private static ItemSlotData Item(string id, EItemGrade grade, string title)
        {
            ItemSlotData data = new ItemSlotData();
            data.Id    = id;
            data.Grade = grade;
            data.Count = 1;
            data.Title = title;
            return data;
        }
    }
}
#endif
