// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Art
{
    // 운영 PlayerFemaleCharacterAppearanceView와 같은 조립기(PlayerCharacterLookAssembler)를 같은 카탈로그로 불러 직업 3종의
    // 기본 외형을 두 성별로 모두 만든다. P09 외형에는 무기 모델이 없고(무기 표시 코드도 없다) 직업 차이는 기본 얼굴, 머리, 색뿐이다.
    // 직업별 전투 애니메이션은 PlayerCombatState에 묶여 있어 여기서 재생하지 않는다
    /// <summary> 검사, 마검사, 궁수 기본 외형을 남녀로 조립해 한 번에 그린다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLookPrewarmTarget : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SLOT_SPACING    = 1f;
        private const int   MALE_BUST       = CharacterAppearanceCatalog.P09_BUST_NONE;
        private const int   CENTER_COLUMN   = 1;
        private const int   FEMALE_ROW      = 0;
        private const int   MALE_ROW        = 1;
        private const int   BEARD_ID_OFFSET = 1;

        private static readonly int[] G_ClassIds = { (int)EPlayerClass.Swordsman, (int)EPlayerClass.Mageknight, (int)EPlayerClass.Archer };

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerCharacterLookCatalogSO _catalog;

        private readonly List<GameObject> _slots = new List<GameObject>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            EndPrewarm();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void BeginPrewarm()
        {
            EndPrewarm();

            if (_catalog == null)
            {
                Debug.LogWarning("[Prewarm] 외형 카탈로그가 비어 있다", this);
                return;
            }

            for (int c = 0; c < G_ClassIds.Length; ++c)
            {
                CharacterAppearance appearance = CharacterAppearanceCatalog.CreateDefault(G_ClassIds[c]);
                if ((appearance == null) || (!CharacterP09Look.TryFrom(appearance, out CharacterP09Look female)))
                {
                    continue;
                }

                // 수염 ID는 직업 순번에 오프셋을 더해 달리해 수염 메시도 한 번씩 그린다
                CharacterP09Look male = new CharacterP09Look(CharacterAppearance.BODY_TYPE_MALE, female.FaceId, female.HairId,
                    female.HairColor, female.EyeColor, female.SkinTone, c + BEARD_ID_OFFSET, MALE_BUST, female.OutfitMask);

                Assemble(female, c, FEMALE_ROW);
                Assemble(male, c, MALE_ROW);
            }
        }

        public void TickPrewarm(float elapsedSeconds)
        {
        }

        public void EndPrewarm()
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                if (_slots[i] != null)
                {
                    Destroy(_slots[i]);
                }
            }

            _slots.Clear();
        }

        private void Assemble(CharacterP09Look look, int column, int row)
        {
            GameObject slot = new GameObject("PlayerLook Prewarm " + column + "_" + row);
            slot.transform.SetParent(transform, false);
            slot.transform.localPosition = new Vector3((column - CENTER_COLUMN) * SLOT_SPACING, 0f, row * SLOT_SPACING);
            _slots.Add(slot);

            if (!PlayerCharacterLookAssembler.TryAssemble(_catalog, look, slot.transform, out GameObject result, out string error))
            {
                Debug.LogError($"[Prewarm] 외형 조립 실패: {error}", this);
                return;
            }

            // 운영 AppearanceView와 같이 시각 인스턴스의 물리를 끈다
            Collider[] colliders = result.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }

            if ((result.TryGetComponent(out Animator animator)) && (animator.isActiveAndEnabled))
            {
                animator.Update(0f);
            }
        }
    }
}
