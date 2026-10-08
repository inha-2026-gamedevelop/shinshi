// System
using System;

namespace Shinshi.Editor
{
    /// <summary> 계약 전용 요청이 성공했을 때 시나리오에 남기는 변화 종류, 실제 경제 규칙은 계산하지 않는다 </summary>
    public enum EContractEffect
    {
        Record,
        AppendResult,
        RemoveInput
    }

    /// <summary> 계약 전용 화면이 보내는 요청 하나, 단추 글자와 성공 때 남길 변화 </summary>
    public sealed class EconomyContractOperation
    {
        /****************************************
        *              Properties
        ****************************************/

        public string          Label  { get; }
        public EContractEffect Effect { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EconomyContractOperation(string label, EContractEffect effect)
        {
            Label  = label;
            Effect = effect;
        }
    }

    /// <summary> 실제 View가 없는 앞으로의 화면 하나의 계약, 편집 값 · 입력 목록 · 가짜 요청 이름만 담는다 </summary>
    public sealed class ContractOnlyDefinition
    {
        /****************************************
        *              Properties
        ****************************************/

        public string                     Id          { get; }
        public string                     Title       { get; }
        public string                     Requirement { get; }
        public EconomyField[]             Fields      { get; }
        public EconomyListSchema          InputList   { get; }
        public EconomyContractOperation[] Operations  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ContractOnlyDefinition(string id, string title, string requirement, EconomyField[] fields,
            EconomyListSchema inputList, EconomyContractOperation[] operations)
        {
            Id          = id;
            Title       = title;
            Requirement = requirement;
            Fields      = fields ?? Array.Empty<EconomyField>();
            InputList   = inputList;
            Operations  = operations ?? Array.Empty<EconomyContractOperation>();
        }
    }

    /// <summary> docs/design 요구에서 뽑은 8개 미래 화면의 계약 정의, 수치 규칙은 서버 몫이라 여기에 두지 않는다 </summary>
    public static class ContractOnlyDefinitions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ID_PREFIX = "economy_contract_";

        private const int  LIST_MAX  = 30;
        private const long VALUE_MAX = 999999999L;

        private const string GROWTH_DOC = "docs/design/growth-economy.md";
        private const string LIFE_DOC   = "docs/design/life-training.md";

        /****************************************
        *                Methods
        ****************************************/

        public static ContractOnlyDefinition[] All()
        {
            return new[]
            {
                Growth(),
                Equipment(),
                Enhancement(),
                Inheritance(),
                Training(),
                Talisman(),
                Crafting(),
                Life()
            };
        }

        private static EconomyListSchema List(string id, string title, string nameLabel, string valueLabel, string[][] rows)
        {
            return new EconomyListSchema(
                id + ".input", title, LIST_MAX,
                new[]
                {
                    new EconomyField("name", nameLabel, EEconomyFieldKind.Text, "항목 {n}"),
                    new EconomyField("value", valueLabel, EEconomyFieldKind.Int, "1", 0, int.MaxValue)
                },
                rows);
        }

        private static ContractOnlyDefinition Growth()
        {
            string id = ID_PREFIX + "growth";
            return new ContractOnlyDefinition(id, "성장 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 성장 · 경험치 요구를 따른다. 규칙 계산은 서버 몫이다.",
                new[]
                {
                    new EconomyField("level", "현재 레벨", EEconomyFieldKind.Int, "1", 1, 999),
                    new EconomyField("exp", "누적 경험치", EEconomyFieldKind.Long, "0", 0, VALUE_MAX)
                },
                List(id, "경험치 획득 내역", "획득처", "경험치", new[] { new[] { "일반 사냥", "100" }, new[] { "퀘스트", "300" } }),
                new[]
                {
                    new EconomyContractOperation("성장 반영 요청", EContractEffect.Record),
                    new EconomyContractOperation("성장 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("획득 내역 줄 제거 요청", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Equipment()
        {
            string id = ID_PREFIX + "equipment";
            return new ContractOnlyDefinition(id, "장비 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 장비 부품 · 재료 · 0강 생성 요구를 따른다.",
                new[]
                {
                    new EconomyField("slot", "대상 슬롯 번호", EEconomyFieldKind.Int, "0", 0, 5),
                    new EconomyField("equipped", "장착 중", EEconomyFieldKind.Bool, "false")
                },
                List(id, "보유 장비", "장비 이름", "강화 수치", new[] { new[] { "낡은 검", "0" }, new[] { "가죽 모자", "3" } }),
                new[]
                {
                    new EconomyContractOperation("장착 요청", EContractEffect.Record),
                    new EconomyContractOperation("해제 요청", EContractEffect.Record),
                    new EconomyContractOperation("장비 생성 결과 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("장비 분해 요청 (줄 제거)", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Enhancement()
        {
            string id = ID_PREFIX + "enhancement";
            return new ContractOnlyDefinition(id, "강화 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 강화 성공률 · 비용 · 보호 요구를 따른다. 확률과 비용은 서버가 정하고 이 화면은 값을 계산하지 않는다.",
                new[]
                {
                    new EconomyField("target", "대상 장비", EEconomyFieldKind.Text, "낡은 검"),
                    new EconomyField("current", "현재 강화", EEconomyFieldKind.Int, "0", 0, 99),
                    new EconomyField("cost", "표시용 비용", EEconomyFieldKind.Long, "100", 0, VALUE_MAX),
                    new EconomyField("protect", "보호 사용", EEconomyFieldKind.Bool, "false")
                },
                List(id, "강화 재료", "재료 이름", "수량", new[] { new[] { "강화석", "2" } }),
                new[]
                {
                    new EconomyContractOperation("강화 요청", EContractEffect.Record),
                    new EconomyContractOperation("강화 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("재료 줄 제거 요청", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Inheritance()
        {
            string id = ID_PREFIX + "inheritance";
            return new ContractOnlyDefinition(id, "계승 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 계승 조건(원본 강화 수치 · 대상 0강 · 미장착) 요구를 따른다. 조건 판정은 서버 몫이다.",
                new[]
                {
                    new EconomyField("source", "원본 장비", EEconomyFieldKind.Text, "원본 검"),
                    new EconomyField("sourceLevel", "원본 강화", EEconomyFieldKind.Int, "17", 0, 99),
                    new EconomyField("targetName", "대상 장비", EEconomyFieldKind.Text, "대상 검"),
                    new EconomyField("targetEquipped", "대상 장착 중", EEconomyFieldKind.Bool, "false")
                },
                List(id, "계승 후보", "장비 이름", "강화 수치", new[] { new[] { "원본 검", "17" }, new[] { "대상 검", "0" } }),
                new[]
                {
                    new EconomyContractOperation("계승 요청", EContractEffect.Record),
                    new EconomyContractOperation("계승 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("후보 줄 제거 요청", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Training()
        {
            string id = ID_PREFIX + "training";
            return new ContractOnlyDefinition(id, "수련 (계약 전용)",
                "실제 화면 없음. " + LIFE_DOC + "의 수련 능력치 · 단계 · 재분배 요구를 따른다. 비용과 한도는 서버가 정한다.",
                new[]
                {
                    new EconomyField("points", "남은 수련 단계", EEconomyFieldKind.Int, "10", 0, 999)
                },
                List(id, "능력치별 단계", "능력치", "단계", new[] { new[] { "능력치 1", "3" }, new[] { "능력치 2", "2" } }),
                new[]
                {
                    new EconomyContractOperation("수련 요청", EContractEffect.Record),
                    new EconomyContractOperation("재분배 요청", EContractEffect.Record),
                    new EconomyContractOperation("수련 결과 기록 추가", EContractEffect.AppendResult)
                });
        }

        private static ContractOnlyDefinition Talisman()
        {
            string id = ID_PREFIX + "talisman";
            return new ContractOnlyDefinition(id, "부적 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 부적 등록 · 효과 단계 · 해체 요구를 따른다. 확률은 서버가 정한다.",
                new[]
                {
                    new EconomyField("slotCount", "장착 칸 수", EEconomyFieldKind.Int, "3", 0, 10)
                },
                List(id, "부적 카드", "카드 이름", "효과 단계", new[] { new[] { "부적 카드 1", "2" }, new[] { "부적 카드 2", "1" } }),
                new[]
                {
                    new EconomyContractOperation("부적 등록 요청", EContractEffect.Record),
                    new EconomyContractOperation("등록 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("부적 해체 요청 (줄 제거)", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Crafting()
        {
            string id = ID_PREFIX + "crafting";
            return new ContractOnlyDefinition(id, "제작 (계약 전용)",
                "실제 화면 없음. " + GROWTH_DOC + "의 제작 · 분해 요구를 따른다. 재료 수와 환급 비율은 서버가 정한다.",
                new[]
                {
                    new EconomyField("recipe", "제작 대상", EEconomyFieldKind.Text, "낡은 검")
                },
                List(id, "투입 재료", "재료 이름", "수량", new[] { new[] { "철 조각", "3" }, new[] { "가죽", "2" } }),
                new[]
                {
                    new EconomyContractOperation("제작 요청", EContractEffect.Record),
                    new EconomyContractOperation("제작 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("분해 요청 (줄 제거)", EContractEffect.RemoveInput)
                });
        }

        private static ContractOnlyDefinition Life()
        {
            string id = ID_PREFIX + "life";
            return new ContractOnlyDefinition(id, "생활 (계약 전용)",
                "실제 화면 없음. " + LIFE_DOC + "의 채집 · 행동력 · 물약 제작 요구를 따른다. 수량과 소모량은 서버가 정한다.",
                new[]
                {
                    new EconomyField("actionPoints", "행동력", EEconomyFieldKind.Int, "20", 0, 999)
                },
                List(id, "채집물", "채집물 이름", "수량", new[] { new[] { "약초", "5" } }),
                new[]
                {
                    new EconomyContractOperation("채집 요청", EContractEffect.Record),
                    new EconomyContractOperation("채집 결과 기록 추가", EContractEffect.AppendResult),
                    new EconomyContractOperation("물약 제작 요청 (줄 제거)", EContractEffect.RemoveInput)
                });
        }
    }
}
