# AC_PlayerP09 기술 배정과 시각 지연

`Assets/Editor/Art/PlayerP09AssetBuilder.cs`가 만든다. 속도 = 클립 길이 / 목표 시간을 0.5 ~ 3으로 자른 값, 지연 = 클립 길이 / 속도 - 목표 시간.
지연이 0.5초를 넘으면 지연이 가장 작은 기술 클립으로 바꾼다. 도술사, 신령사는 검 모션 임시 배정이다.

| 직업 | 종류 | 슬롯 | 목표(초) | 기본 클립 | 최종 클립 | 속도 | 지연(초) |
|---|---|---|---|---|---|---|---|
| Warrior | Regular | 0 | 0.40 | S_Attack_3 | S_Attack_3 | 3.00 | 0.32 |
| Warrior | Regular | 1 | 0.50 | S_QuickShift_F | S_QuickShift_F | 2.00 | 0.00 |
| Warrior | Regular | 2 | 0.80 | S_Sp_Skill_3 | S_Attack_3 | 2.71 | 0.00 |
| Warrior | Regular | 3 | 0.70 | S_Sp_Skill_1 | S_QuickShift_F | 1.43 | 0.00 |
| Sorcerer | Regular | 0 | 0.30 | S_Sp_Skill_1 | S_QuickShift_F | 3.00 | 0.03 |
| Sorcerer | Regular | 1 | 0.45 | S_Sp_Skill_2 | S_Sp_Skill_2 | 3.00 | 0.35 |
| Sorcerer | Regular | 2 | 0.45 | S_Sp_Skill_3 | S_QuickShift_F | 2.22 | 0.00 |
| Sorcerer | Regular | 3 | 0.85 | S_Sp_Skill_1 | S_Sp_Skill_1 | 3.00 | 0.48 |
| Summoner | Regular | 0 | 0.65 | S_Sp_Skill_1 | S_QuickShift_F | 1.54 | 0.00 |
| Summoner | Regular | 1 | 0.65 | S_Sp_Skill_2 | S_Sp_Skill_2 | 3.00 | 0.15 |
| Summoner | Regular | 2 | 0.35 | S_Sp_Skill_3 | S_QuickShift_F | 2.86 | 0.00 |
| Summoner | Regular | 3 | 0.45 | S_Sp_Skill_1 | S_QuickShift_F | 2.22 | 0.00 |
| Warrior | Ascension | 0 | 0.75 | S_Sp_Skill_1 | S_Attack_3 | 2.89 | 0.00 |
| Warrior | Ascension | 1 | 0.85 | S_Sp_Skill_2 | S_Sp_Skill_2 | 2.82 | 0.00 |
| Warrior | Ascension | 2 | 0.55 | S_Sp_Skill_3 | S_Attack_1 | 2.55 | 0.00 |
| Sorcerer | Ascension | 0 | 0.75 | S_Sp_Skill_1 | S_Attack_3 | 2.89 | 0.00 |
| Sorcerer | Ascension | 1 | 0.85 | S_Sp_Skill_2 | S_Sp_Skill_2 | 2.82 | 0.00 |
| Sorcerer | Ascension | 2 | 0.55 | S_Sp_Skill_3 | S_Attack_1 | 2.55 | 0.00 |
| Summoner | Ascension | 0 | 0.75 | S_Sp_Skill_1 | S_Attack_3 | 2.89 | 0.00 |
| Summoner | Ascension | 1 | 0.85 | S_Sp_Skill_2 | S_Sp_Skill_2 | 2.82 | 0.00 |
| Summoner | Ascension | 2 | 0.55 | S_Sp_Skill_3 | S_Attack_1 | 2.55 | 0.00 |
