// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 등록된 부적을 시전 전에 합성하는 창구, 기본 구현은 원본 수치 그대로다 </summary>
    public interface ITalismanModifierResolver
    {
        /****************************************
        *                Methods
        ****************************************/

        SkillModifiers Resolve(int participantId, SkillCatalogSO.SkillDefinition skill);
    }
}
