// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 보스를 만들 때 씬 탐색 없이 찾도록 켜질 때 자신을 정적 자리에 올린다. 던전 씬 하나에 하나만 둔다.
    // 표현이 아니라 판정 입력이므로 ClientView를 상속하지 않는다. 서버 빌드에서도 켜져 있어야 한다.
    // 표식 배열이 비어 있으면 자식의 표식을 한 번 모은다. 맵 담당이 배열을 채우지 않고 자식으로만 둬도 쓰게 한다
    /// <summary> 맵 담당이 둔 무철 돌진 구조물 표식 목록, 서버 구조물 조회와 클라이언트 구조물 표현이 읽는다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossChargeStructureSet : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구조물")]
        [SerializeField] private BossChargeStructureMarker[] _markers = null;

        private static BossChargeStructureSet _current;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 켜져 있는 목록, 없으면 null </summary>
        public static BossChargeStructureSet Current => _current;

        public int Count => (_markers == null) ? 0 : _markers.Length;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 목록이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _current = null;
        }

        private void Awake()
        {
            if ((_markers == null) || (_markers.Length == 0))
            {
                _markers = GetComponentsInChildren<BossChargeStructureMarker>(true);
            }
        }

        private void OnEnable()
        {
            if ((_current != null) && (_current != this))
            {
                Debug.LogError("[Boss] 돌진 구조물 목록이 씬에 둘 이상이다. 먼저 켜진 목록만 쓴다", this);
                return;
            }

            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 하나라도 어긋나면 아무것도 담지 않고 거짓이다. 일부만 쓰면 기믹 지정 수가 맵 의도와 달라진다
        /// <summary> 표식을 판정 값으로 모은다, 빈 칸, 식별자 0 이하, 중복, 잘못된 반경이 있으면 reason과 함께 거짓이다 </summary>
        public bool TryCollect(List<BossChargeStructureInfo> output, out string reason)
        {
            reason = string.Empty;
            if (output == null)
            {
                reason = "담을 목록이 없다";
                return false;
            }

            int start = output.Count;
            var seen  = new HashSet<int>();
            for (int i = 0; i < Count; ++i)
            {
                BossChargeStructureMarker marker = _markers[i];
                if (marker == null)
                {
                    reason = $"{i}번 표식이 비어 있다";
                    output.RemoveRange(start, output.Count - start);
                    return false;
                }

                BossChargeStructureInfo info = marker.ToInfo();
                if ((!info.IsValid) || (!seen.Add(info.Id)))
                {
                    reason = $"{i}번 표식의 식별자나 반경이 잘못됐거나 식별자가 겹친다, id={info.Id}, radius={info.Radius}";
                    output.RemoveRange(start, output.Count - start);
                    return false;
                }

                output.Add(info);
            }

            return true;
        }

        /// <summary> 식별자로 표식을 찾는다, 없으면 거짓이다 </summary>
        public bool TryGetMarker(int structureId, out BossChargeStructureMarker marker)
        {
            for (int i = 0; i < Count; ++i)
            {
                if ((_markers[i] != null) && (_markers[i].StructureId == structureId))
                {
                    marker = _markers[i];
                    return true;
                }
            }

            marker = null;
            return false;
        }
    }
}
