#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 공격 애니메이션 없이 넓게 베기 연출을 반복 재생해 화면에서 확인한다 </summary>
    public sealed class DevWideSlashPreviewLoop : MonoBehaviour
    {
        // 무사 공격 클립이 아직 없어 대역 칼 막대를 회전시켜 준비 0.2초와 휘두름을 흉내 낸다.
        // 서버 확정 타격도 없으므로 휘두름 중간 시각에 표적 위치로 직접 타격 연출을 부른다

        /****************************************
        *                Fields
        ****************************************/

        private const float HIT_SWEEP_RATIO = 0.5f;

        [SerializeField] private SlashVfxEmitter _emitter;
        [SerializeField] private Transform       _bladePivot;
        [SerializeField] private Transform[]     _targets;

        [Header("시간")]
        [SerializeField, Min(0.1f)]  private float _interval      = 1.6f;
        [SerializeField, Min(0f)]    private float _windupTime    = 0.2f;
        [SerializeField, Min(0.01f)] private float _sweepDuration = 0.09f;
        [SerializeField, Min(0f)]    private float _recoverTime   = 0.35f;

        [Header("각도")]
        [SerializeField] private float _restYaw   = 20f;
        [SerializeField] private float _windupYaw = 80f;
        [SerializeField] private float _endYaw    = -80f;

        private Sequence _loop;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if ((_emitter == null) || (_bladePivot == null))
            {
                Debug.LogError("[Dev] 넓게 베기 미리보기에 연출 진입점이나 칼 회전축이 없다", this);
                enabled = false;
                return;
            }

            _bladePivot.localRotation = Quaternion.Euler(0f, _restYaw, 0f);

            float holdTime = Mathf.Max(0f, _interval - _windupTime - _sweepDuration - _recoverTime);

            _loop = DOTween.Sequence()
                .AppendCallback(_emitter.PlayWideSlash)
                .Append(_bladePivot.DOLocalRotate(new Vector3(0f, _windupYaw, 0f), _windupTime).SetEase(Ease.OutQuad))
                .Append(_bladePivot.DOLocalRotate(new Vector3(0f, _endYaw, 0f), _sweepDuration).SetEase(Ease.OutCubic))
                .InsertCallback(_windupTime + (_sweepDuration * HIT_SWEEP_RATIO), HitTargets)
                .Append(_bladePivot.DOLocalRotate(new Vector3(0f, _restYaw, 0f), _recoverTime).SetEase(Ease.InOutSine))
                .AppendInterval(holdTime)
                .SetLoops(-1)
                .SetLink(gameObject);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HitTargets()
        {
            if (_targets == null)
            {
                return;
            }

            for (int i = 0; i < _targets.Length; ++i)
            {
                if (_targets[i] != null)
                {
                    _emitter.PlayHit(_targets[i].position, true);
                }
            }
        }
    }
}
#endif
