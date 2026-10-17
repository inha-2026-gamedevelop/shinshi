// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 맵 담당이 무철 전장에 두는 돌진 구조물 표식이다. 판정은 이 표식이 아니라 서버의 해석적 조회(BossChargeStructureField)가
    // 식별자, 위치, 반경만 읽어서 한다. 그래서 구조물 모델의 콜라이더는 보스 돌진 판정에 쓰이지 않는다.
    // 서버 판정 입력이기도 하므로 ClientView를 상속하지 않는다. 서버 빌드에서도 켜져 있어야 플레이어 차단 콜라이더를 함께 끈다.
    // 파괴 상태 전환은 서버(파괴 확정)와 클라이언트 표현(파괴 사건, 복원)이 각자 부른다. 같은 값으로 여러 번 불러도 결과가 같다
    /// <summary> 무철 돌진 구조물 하나의 맵 표식, 식별자와 반경과 지정 여부, 온전하거나 부서진 모습과 플레이어 차단 콜라이더 </summary>
    [DisallowMultipleComponent]
    public sealed class BossChargeStructureMarker : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float GIZMO_HEIGHT = 4f; // m, 편집기 표시 높이
        private const float HALF         = 0.5f;

        [Header("판정")]
        [SerializeField] private int   _structureId     = 0;
        [SerializeField] private float _radius          = 1.5f;
        [SerializeField] private bool  _isGimmickTarget = true;

        [Header("모습")]
        [SerializeField] private GameObject _intactObject   = null; // 온전할 때 켜고 부서지면 끈다
        [SerializeField] private GameObject _brokenObject   = null; // 부서지면 켠다
        [SerializeField] private Collider   _playerBlocker  = null; // 온전할 때만 플레이어를 막는다

        private bool _isDestroyed = false;

        /****************************************
        *              Properties
        ****************************************/

        public int StructureId => _structureId;

        /// <summary> m, 원기둥 수평 반경 </summary>
        public float Radius => _radius;

        public bool IsGimmickTarget => _isGimmickTarget;

        /// <summary> 마지막으로 맞춘 상태가 부서짐이다 </summary>
        public bool IsDestroyed => _isDestroyed;

        /****************************************
        *              Unity Event
        ****************************************/

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = _isGimmickTarget ? Color.red : Color.yellow;
            Vector3 center = transform.position;
            float   diameter = _radius / HALF;
            Gizmos.DrawWireCube(center + (Vector3.up * (GIZMO_HEIGHT * HALF)), new Vector3(diameter, GIZMO_HEIGHT, diameter));
        }
#endif

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 판정 값으로 옮긴다, 중심은 지금 위치다 </summary>
        public BossChargeStructureInfo ToInfo()
        {
            return new BossChargeStructureInfo(_structureId, transform.position, _radius, _isGimmickTarget);
        }

        /// <summary> 온전하거나 부서진 모습과 플레이어 차단을 맞춘다, 연출은 재생하지 않는다 </summary>
        public void SetDestroyed(bool destroyed)
        {
            _isDestroyed = destroyed;

            if (_intactObject != null)
            {
                _intactObject.SetActive(!destroyed);
            }

            if (_brokenObject != null)
            {
                _brokenObject.SetActive(destroyed);
            }

            if (_playerBlocker != null)
            {
                _playerBlocker.enabled = !destroyed;
            }
        }
    }
}
