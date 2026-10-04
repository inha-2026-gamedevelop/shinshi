// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>저장 중, 완료, 재시도 중을 짧은 글자로 보여 주며 Idle이면 숨긴다</summary>
    public sealed class SaveStatusView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SAVING_TEXT   = "저장 중...";
        private const string SAVED_TEXT    = "저장 완료";
        private const string RETRYING_TEXT = "저장 재시도 중...";

        [Header("표시")]
        [SerializeField] private GameObject _root  = null;
        [SerializeField] private TMP_Text   _label = null;

        private ESaveStatus _status = ESaveStatus.Idle;

        /****************************************
        *              Properties
        ****************************************/

        public ESaveStatus Status => _status;

        /****************************************
        *                Methods
        ****************************************/

        public void SetStatus(ESaveStatus status)
        {
            _status = status;

            if (_root != null)
            {
                _root.SetActive(status != ESaveStatus.Idle);
            }

            if (_label != null)
            {
                _label.text = ToText(status);
            }
        }

        private static string ToText(ESaveStatus status)
        {
            switch (status)
            {
                case ESaveStatus.Saving:
                    return SAVING_TEXT;
                case ESaveStatus.Saved:
                    return SAVED_TEXT;
                case ESaveStatus.Retrying:
                    return RETRYING_TEXT;
                default:
                    return string.Empty;
            }
        }
    }
}
