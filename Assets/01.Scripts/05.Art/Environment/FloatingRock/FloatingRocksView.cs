// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 마을 둘레에 떠 있는 바위들이 제자리에서 위아래로 천천히 떠다니며 돈다. 클라이언트 표현 전용이다 </summary>
    [DisallowMultipleComponent]
    public sealed class FloatingRocksView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_TURN         = 360f;
        private const float REVERSE_CHANCE    = 0.5f;
        private const float BOB_LEGS_PER_LOOP = 2f; // 올라갔다 내려오는 한 번이 요요 두 구간이다

        [SerializeField] private Transform[] _rocks = System.Array.Empty<Transform>();

        // 바위마다 주기와 위상을 달리해 한꺼번에 오르내리지 않게 한다
        [Header("위아래 부유")]
        [SerializeField] private float _bobHeight      = 0.6f; // m, 기준 높이에서 위아래로 움직이는 폭
        [SerializeField] private float _bobDurationMin = 3.5f; // 초, 한 번 오르는 데 걸리는 시간
        [SerializeField] private float _bobDurationMax = 6f;

        [Header("느린 회전")]
        [SerializeField] private float _spinDurationMin = 70f;  // 초, 한 바퀴 도는 데 걸리는 시간
        [SerializeField] private float _spinDurationMax = 130f;

        /****************************************
        *              Unity Event
        ****************************************/

        // 트윈은 만든 뒤 각자 무한 반복한다. 오브젝트가 파괴되면 SetLink가 함께 정리한다
        protected override void OnViewAwake()
        {
            foreach (Transform rock in _rocks)
            {
                if (rock == null)
                {
                    continue;
                }

                float baseY = rock.localPosition.y;
                float duration = Random.Range(_bobDurationMin, _bobDurationMax);

                // 기준 높이 아래에서 위로 오가므로 시작 시점을 흩뜨려야 바위마다 다른 높이에서 출발한다
                rock.localPosition = new Vector3(rock.localPosition.x, baseY - _bobHeight, rock.localPosition.z);
                rock.DOLocalMoveY(baseY + _bobHeight, duration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(rock.gameObject)
                    .Goto(Random.Range(0f, duration * BOB_LEGS_PER_LOOP), true);

                float spinDuration = Random.Range(_spinDurationMin, _spinDurationMax);
                float spinSign = (Random.value < REVERSE_CHANCE) ? -1f : 1f;
                rock.DOLocalRotate(new Vector3(0f, FULL_TURN * spinSign, 0f), spinDuration, RotateMode.LocalAxisAdd)
                    .SetEase(Ease.Linear)
                    .SetLoops(-1, LoopType.Restart)
                    .SetLink(rock.gameObject);
            }
        }
    }
}
