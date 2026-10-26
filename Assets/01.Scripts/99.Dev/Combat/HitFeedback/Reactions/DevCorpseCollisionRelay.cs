// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 래그돌 뼈의 충돌을 사체 단위 첫 착지 감지기로 전달한다 </summary>
    public sealed class DevCorpseCollisionRelay : MonoBehaviour
    {
        private DevCorpseLanding _owner;

        public void Init(DevCorpseLanding owner) { _owner = owner; }

        private void OnCollisionEnter(Collision collision)
        {
            if (_owner != null)
            {
                _owner.OnBoneCollision(collision);
            }
        }
    }
}
