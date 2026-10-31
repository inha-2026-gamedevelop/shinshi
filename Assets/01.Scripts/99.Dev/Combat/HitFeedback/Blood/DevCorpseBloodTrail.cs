// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 모델 복제본의 골반을 혈흔 풀에 연결하고 사체가 사라지면 추적을 끊는다 </summary>
    public sealed class DevCorpseBloodTrail : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PELVIS_BONE = "pelvis";
        private const string HIPS_BONE   = "hips";

        [SerializeField] private HitFeedbackBlood _blood;
        [SerializeField] private Transform           _anchor;
        [SerializeField] private int                 _freezeFrames;

        private int _handle = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_blood == null) || (_anchor == null))
            {
                return;
            }
            _handle = _blood.FollowCorpse(_anchor, _freezeFrames);
        }

        private void OnDisable()
        {
            if (_blood != null)
            {
                _blood.StopFollowing(_handle);
            }
            _handle = 0;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 원본은 재생하지 않는다. EnemyCorpse가 모델을 복제할 때 뼈 참조가 복제본 안으로 다시 연결된다 </summary>
        public void PrepareClone(HitFeedbackBlood blood, Transform anchor, int freezeFrames)
        {
            _blood = blood;
            _anchor = anchor;
            _freezeFrames = freezeFrames;
        }

        /// <summary> 초기화 때 사체의 골반을 한 번 캐싱한다. 일반 뼈 리그라 Humanoid 전용 API를 쓰지 않는다 </summary>
        public static Transform ResolveAnchor(Transform model)
        {
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in renderer.bones)
                {
                    if ((bone != null) && ((bone.name.IndexOf(PELVIS_BONE, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (bone.name.IndexOf(HIPS_BONE, StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        return bone;
                    }
                }
            }
            Rigidbody[] bodies = model.GetComponentsInChildren<Rigidbody>(true);
            return bodies.Length > 0 ? bodies[0].transform : model;
        }
    }
}
