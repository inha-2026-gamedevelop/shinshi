// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 개발 씬에서 서버 상태가 없는 신체 강화 창에 저장하지 않는 예시 데이터를 제공한다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class TrainingPreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        [Header("샘플 데이터 (개발 전용)")]
        [SerializeField] private long  _sampleEssence  = 18400;
        [SerializeField] private int[] _sampleTraining = { 14, 9, 6, 3, 2 };

        [Header("씬 미리보기 참조")]
        [SerializeField] private TrainingView   _view         = null;
        [SerializeField] private GrowthConfigSO _growthConfig = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_view == null) || (_growthConfig == null) || ((Application.isPlaying) && (TownGrowthClient.HasSnapshot)))
            {
                return;
            }

            _view.ApplyPreviewSnapshot(_sampleEssence, _sampleTraining, _growthConfig);
        }
#endif
    }
}
