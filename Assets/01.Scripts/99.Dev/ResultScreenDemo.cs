#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Collections;

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

        private const float SAVING_SECONDS   = 1.2f;
        private const float RETRYING_SECONDS = 1.2f;
        private const int   BASE_ESSENCE     = 600;
        private const int   EXTRA_ESSENCE    = 1500;
        private const string REGION_NAME     = "숲 토벌";

        [SerializeField] private ResultScreenView _view = null;

        private Coroutine _saveRoutine = null;

        /****************************************
        *              Unity Event
        ****************************************/

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

            ResultScreenData data = new ResultScreenData();
            data.Reason       = reason;
            data.RegionName   = REGION_NAME;
            data.BaseEssence  = BASE_ESSENCE;
            data.ExtraEssence = success ? EXTRA_ESSENCE : (EXTRA_ESSENCE / 2);
            data.LostEssence  = success ? 0 : (EXTRA_ESSENCE / 2);

            data.ConfirmedItems.Add(Item("item_essence", EItemGrade.Common, data.BaseEssence + data.ExtraEssence, "정기"));
            data.ConfirmedItems.Add(Item("item_refine_shard", EItemGrade.Common, 35, "정련편"));

            if (success)
            {
                data.ChanceItems.Add(Item("item_boss_ticket", EItemGrade.Rare, 1, "보스 입장권"));
                data.ChanceItems.Add(Item("talisman_kind_08", EItemGrade.Epic, 1, "부적: 피해 증가"));
            }

            _view.SetResult(data);

            if (_saveRoutine != null)
            {
                StopCoroutine(_saveRoutine);
            }

            _saveRoutine = StartCoroutine(RunSave());
        }

        private IEnumerator RunSave()
        {
            _view.SetSaveStatus(ESaveStatus.Saving);
            yield return new WaitForSeconds(SAVING_SECONDS);
            _view.SetSaveStatus(ESaveStatus.Retrying);
            yield return new WaitForSeconds(RETRYING_SECONDS);
            _view.SetSaveStatus(ESaveStatus.Saved);
            _saveRoutine = null;
        }

        private void OnReturnClicked() { Debug.Log("[ResultScreenDemo] 귀환 (시험 화면이라 장면은 바뀌지 않음)"); }

        private static ItemSlotData Item(string id, EItemGrade grade, int count, string title)
        {
            ItemSlotData data = new ItemSlotData();
            data.Id    = id;
            data.Grade = grade;
            data.Count = count;
            data.Title = title;
            return data;
        }
    }
}
#endif
