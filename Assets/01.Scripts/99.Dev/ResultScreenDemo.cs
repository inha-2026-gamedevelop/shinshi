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
    /// Dev_Result 씬에서 결과 화면을 가짜 정산값으로 시험한다.
    /// 키: 1 성공, 2 시간 초과, 3 전멸, 4 중도 포기. 열면 저장 중, 재시도 중, 저장 완료 순으로 바뀐다
    /// </summary>
    public sealed class ResultScreenDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  SAVING_SECONDS   = 1.2f;
        private const float  RETRYING_SECONDS = 1.2f;
        private const int    BASE_ESSENCE     = 600;
        private const int    EXTRA_ESSENCE    = 1500;
        private const int    LOSS_DIVISOR     = 2;
        private const int    SHARD_COUNT      = 35;
        private const string REGION_NAME      = "숲 토벌";

        [SerializeField] private ResultScreenView _view = null;

        private Coroutine      _saveRoutine  = null;
        private WaitForSeconds _waitSaving   = null;
        private WaitForSeconds _waitRetrying = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _waitSaving   = new WaitForSeconds(SAVING_SECONDS);
            _waitRetrying = new WaitForSeconds(RETRYING_SECONDS);
        }

        private void OnEnable() { _view.ReturnClicked += OnReturnClicked; }

        private void OnDisable() { _view.ReturnClicked -= OnReturnClicked; }

        private void Start() { Show(ESessionEndReason.Success); }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Show(ESessionEndReason.Success);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Show(ESessionEndReason.Timeout);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Show(ESessionEndReason.AllDead);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                Show(ESessionEndReason.Abandon);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Show(ESessionEndReason reason)
        {
            bool success = (reason == ESessionEndReason.Success);
            int  extra   = success ? EXTRA_ESSENCE : (EXTRA_ESSENCE / LOSS_DIVISOR);
            int  lost    = success ? 0 : (EXTRA_ESSENCE / LOSS_DIVISOR);

            List<ItemSlotData> confirmed = new List<ItemSlotData>
            {
                Item("item_essence", EItemGrade.Common, BASE_ESSENCE + extra, "정기"),
                Item("item_refine_shard", EItemGrade.Common, SHARD_COUNT, "정련편")
            };

            List<ItemSlotData> chance = new List<ItemSlotData>();

            if (success)
            {
                chance.Add(Item("item_boss_ticket", EItemGrade.Rare, 1, "보스 입장권"));
                chance.Add(Item("talisman_kind_08", EItemGrade.Epic, 1, "부적: 피해 증가"));
            }

            _view.SetResult(new ResultScreenData(reason, REGION_NAME, BASE_ESSENCE, extra, lost, confirmed, chance));

            if (_saveRoutine != null)
            {
                StopCoroutine(_saveRoutine);
            }

            _saveRoutine = StartCoroutine(RunSave());
        }

        private IEnumerator RunSave()
        {
            _view.SetSaveStatus(ESaveStatus.Saving);
            yield return _waitSaving;
            _view.SetSaveStatus(ESaveStatus.Retrying);
            yield return _waitRetrying;
            _view.SetSaveStatus(ESaveStatus.Saved);
            _saveRoutine = null;
        }

        private void OnReturnClicked() { Debug.Log("[ResultScreenDemo] 귀환 (시험 화면이라 장면은 바뀌지 않음)"); }

        private static ItemSlotData Item(string id, EItemGrade grade, int count, string title)
        {
            return new ItemSlotData(id, grade, count, title, string.Empty);
        }
    }
}
#endif
