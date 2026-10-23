# 소셜(친구·귓속말·길드·길드 채팅) 1차 설계

> 상태(2026-10-16): **구현됨·실행 미검증.** 사용자 결정 11건(11절)과 Codex 검증 지적 반영(12절)을 거쳐 코드·마이그레이션 `0019_social`·프리팹(`PF_FriendWindow`, `PF_GuildWindow`)·`ShinshiTown`/`DungeonForest` 씬 연결을 넣었다. 자동 검증은 같은 날 Unity EditMode 6,427건 중 6,426건 통과(1건은 이 작업과 무관한 기존 제외 항목)와 백엔드 실제 PostgreSQL 1,607건 통과다. **게임 실행(Play 모드), 로컬 스택 기동, 다인 접속(M2)은 하지 않았다.** 9절의 로컬 스택(L2)·다인(M2) 검증은 아직 남았다. 이 문서 본문은 설계 시점의 계획이며 구현에서 달라진 이름·경로는 13절 구현 메모가 정한다. [기획서](../02-game-design.md)에는 "친구 초대·공개 방 목록으로 1–4인 파티를 만든다"(파티 절) 외에 소셜 규칙이 없어, 이 문서의 상한·기한·이름 규칙 수치는 기획 근거 없이 사용자가 초안 값으로 확정한 것이다(11절). 권위·통신 원칙은 [기술 설계서](../03-technical-design.md) 3·4·5장과 [네트워크 권위](../unity/NETWORK_AUTHORITY.md)를 따른다. 규칙이 다르면 그 문서가 우선한다.

## 1. 목적과 범위

마을·던전 어디에 있든 같은 친구·길드 관계를 보고, 캐릭터 이름으로 1:1 대화를 하고, 길드원끼리 채팅할 수 있게 한다. 관계는 **캐릭터 단위**다(계정은 동시에 캐릭터 하나만 접속하므로 접속 표시가 캐릭터와 1:1로 맞는다).

| 포함 | 제외(2차 이후) |
|---|---|
| 친구 요청·수락·거절·요청 취소·삭제, 친구 목록과 접속 위치(오프라인/신시/출정 중) 표시 | 차단 목록, 친구 메모, 친구 그룹 |
| 친구 목록에서 파티 초대(기존 `PartyRequestMessage(Invite)` 재사용) | 다른 마을 채널 간 파티 초대(현재 마을 프로세스 1개) |
| 귓속말: 캐릭터 이름으로 1:1 채팅, 상대 오프라인이면 실패 안내, 마을↔던전 사이 전달 | 오프라인 쪽지 보관, 귓속말 기록 영속 |
| 길드 생성·가입 신청·초대·탈퇴·추방·길드장 위임, 마지막 1인 탈퇴 시 해체 | 부길드장 등 직급, 길드 레벨·창고·공지·마크, 길드 검색 목록 |
| 길드원 목록과 접속 위치 표시 | 길드 생성 비용(무료로 결정, 11절) |
| 채팅에 길드 채널, 귓속말 채널 추가 | 친구 접속·종료 실시간 알림(넣지 않기로 결정, 11절) |
| 던전에서 귓속말·길드 채팅 송수신, 소셜 알림 수신 | 던전에서 친구·길드 창 조작(신시 전용으로 결정, 5.9절) |

## 2. 데이터 원천

| 화면 값 | 원천 | 비고 |
|---|---|---|
| 친구·받은 요청·보낸 요청 목록 | 새 백엔드 테이블 `friendships`, `friend_requests`(3절) | 마을 서버가 `POST .../social/snapshot`으로 받아 `SocialSnapshotMessage`로 소유자에게만 보낸다 |
| 길드 이름·길드장·길드원·내 직급, 가입 신청(길드장만), 받은 길드 초대 | 새 테이블 `guilds`, `guild_members`, `guild_join_requests` | 같은 스냅샷에 포함 |
| 상대 이름·직업 | `characters.name`, `characters.class_id`(살아 있는 행만) | 이름은 `CharacterNameRules`로 정규화한 `name_key`로 찾는다 |
| 접속 위치 | `account_leases`에 새로 붙이는 `character_id` + `server_instances` 생존(5.6절) | 기존 접속 판정 질의(만료 전 임대, 인스턴스 Offline 아님, heartbeat 15초 안)와 같은 조건 |
| "같은 서버" 여부(파티 초대 버튼) | 상대 임대의 `instance_id == 호출 마을 instance_id` | 파티 초대는 같은 마을 프로세스에서만 대상 이름을 찾는다(`TownServerRuntime.TryFindByName`) |
| 내 신원(캐릭터 ID, 이름) | 서버 연결의 `SessionBinding.CharacterId`, `CharacterName` | 클라이언트 메시지에 actor 자리를 두지 않는다(5.8절) |
| 상한·이름 규칙 수치 | 새 `Contracts/Social/SocialLimits.cs`, `GuildNameRules.cs` | Unity·백엔드 공용 소스(`Shinshi.Contracts.csproj`가 링크). DB CHECK의 같은 숫자는 패리티 시험으로 묶는다 |

GameDB ScriptableObject는 추가하지 않는다. 소셜 상한은 밸런스가 아니라 저장 계약이라 `Contracts` 상수로 두며 콘텐츠 해시가 바뀌지 않는다.

## 3. 영속 데이터(마이그레이션 `0019_social`)

가장 큰 기존 번호는 `0018_equipment_options`이므로 `Backend/src/Shinshi.Data/Migrations/0019_social.up.sql`·`.down.sql`을 만든다(`Migrations/*.sql` 임베디드 리소스라 csproj 수정 없음). 다른 작업이 먼저 `0019`를 쓰면 다음 번호로 민다.

### 3.1 상한(2026-10-16 확정)

| 항목 | 값 | 근거 |
|---|---|---|
| 친구 수 | 50 | 사용자 결정(기획 근거 없음) |
| 보낸 친구 요청(대기) | 20 | 사용자 결정, 스팸 방지 |
| 받은 친구 요청(대기) | 50, 넘으면 새 요청 거절 | 사용자 결정 |
| 친구 요청 기한 | 7일 | 사용자 결정 |
| 길드 정원 | 30(길드장 포함) | 사용자 결정, 마을 목표 동접 10~100명 기준 |
| 캐릭터당 대기 가입 신청 | 5 | 사용자 결정 |
| 캐릭터당 받은 길드 초대(대기) | 5, 넘으면 새 초대 거절 | 스냅샷 배열 상한(5)과 맞춤(Codex 2) |
| 길드당 대기 신청+초대 | 50 | 사용자 결정 |
| 가입 신청·초대 기한 | 7일 | 사용자 결정 |
| 길드 이름 | 정규화 뒤 2~8자, 완성형 한글·영문·숫자, 공백·기호 없음, 대소문자·전각만 다른 이름은 같은 이름 | 사용자 결정. 캐릭터 이름 규칙(2~12자)과 같은 처리 순서, 길이만 줄임 |
| 금칙어 | 1차 없음 | 사용자 결정. 캐릭터 이름 금칙어(`CharacterNameRules`의 TODO)를 정할 때 같은 목록을 함께 적용 |
| 해체 길드 이름 재사용 | 즉시 허용 | 사용자 결정 |
| 탈퇴·추방 후 재가입 대기 | 없음 | 사용자 결정 |

### 3.2 DDL

```sql
-- 접속 표시: 계정 임대가 어느 캐릭터의 접속인지. 마을 입장 교환이 채우고 던전 이관(UPDATE)은 그대로 둔다.
-- 0019 전 행과 캐릭터 없이 얻은 임대(개발 경로)는 NULL이며 접속 표시에서 오프라인이다
ALTER TABLE account_leases
    ADD COLUMN character_id uuid NULL REFERENCES characters (character_id);
-- 계정당 임대 1행이고 캐릭터는 한 계정 소유라 캐릭터당 최대 1행이다
CREATE UNIQUE INDEX ux_account_leases_character ON account_leases (character_id) WHERE character_id IS NOT NULL;

-- 친구 관계는 방향마다 한 행(A->B, B->A)으로 저장해 목록과 수 세기를 한 인덱스로 끝낸다. 두 행은 항상 같은 트랜잭션에서 넣고 지운다
CREATE TABLE friendships
(
    character_id uuid NOT NULL REFERENCES characters (character_id),
    friend_character_id uuid NOT NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (character_id, friend_character_id),
    CHECK (character_id <> friend_character_id)
);
CREATE INDEX ix_friendships_friend ON friendships (friend_character_id, character_id);

CREATE TABLE friend_requests
(
    requester_character_id uuid NOT NULL REFERENCES characters (character_id),
    target_character_id uuid NOT NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (requester_character_id, target_character_id),
    CHECK (requester_character_id <> target_character_id),
    CHECK (expires_at > created_at)
);
CREATE INDEX ix_friend_requests_target ON friend_requests (target_character_id, created_at);
CREATE INDEX ix_friend_requests_expiry ON friend_requests (expires_at);

CREATE TABLE guilds
(
    guild_id uuid PRIMARY KEY,
    name text NOT NULL CHECK (name ~ '^[가-힣A-Za-z0-9]{2,8}$'),
    name_key text NOT NULL CHECK (name_key = lower(name)),
    leader_character_id uuid NULL REFERENCES characters (character_id),
    member_count integer NOT NULL CHECK (member_count BETWEEN 0 AND 30),
    -- 생성 재전송 확인용. 같은 생성자가 같은 값으로 다시 보내면 처음 만든 길드를 돌려준다
    creator_character_id uuid NOT NULL REFERENCES characters (character_id),
    create_request_id uuid NOT NULL,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    -- 위임마다 +1. 위임 요청이 기대 값을 함께 보내 늦게 도착한 재전송이 그 뒤의 위임을 되돌리지 못하게 한다(3.3절)
    leader_version bigint NOT NULL DEFAULT 0 CHECK (leader_version >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    disbanded_at timestamptz NULL,
    UNIQUE (creator_character_id, create_request_id),
    -- 살아 있는 길드는 길드장과 1명 이상, 해체한 길드는 길드장 없음과 0명
    CHECK ((disbanded_at IS NULL AND leader_character_id IS NOT NULL AND member_count >= 1)
        OR (disbanded_at IS NOT NULL AND leader_character_id IS NULL AND member_count = 0))
);
-- 이름은 살아 있는 길드 사이에서만 유일하다
CREATE UNIQUE INDEX ux_guilds_name_key_alive ON guilds (name_key) WHERE disbanded_at IS NULL;

-- 캐릭터는 길드 하나에만 든다(PK). role 0 길드원, 1 길드장.
-- membership_id는 가입할 때마다 새로 만든다. 탈퇴·추방·위임 요청이 이 값을 함께 보내 재전송이 그 뒤의 새 가입을 건드리지 못하게 한다(3.3절)
CREATE TABLE guild_members
(
    character_id uuid PRIMARY KEY REFERENCES characters (character_id),
    guild_id uuid NOT NULL REFERENCES guilds (guild_id),
    membership_id uuid NOT NULL UNIQUE,
    role smallint NOT NULL CHECK (role IN (0, 1)),
    joined_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ix_guild_members_guild ON guild_members (guild_id, role, joined_at);
CREATE UNIQUE INDEX ux_guild_members_leader ON guild_members (guild_id) WHERE role = 1;

-- 가입 신청(kind 0, 캐릭터가 보냄, 길드장이 수락)과 초대(kind 1, 길드장이 보냄, 캐릭터가 수락)를 한 표에 둔다.
-- (길드, 캐릭터)당 한 행이라 반대 종류가 이미 있으면 새 요청은 곧바로 가입으로 처리한다(5.4절)
CREATE TABLE guild_join_requests
(
    guild_id uuid NOT NULL REFERENCES guilds (guild_id),
    character_id uuid NOT NULL REFERENCES characters (character_id),
    kind smallint NOT NULL CHECK (kind IN (0, 1)),
    inviter_character_id uuid NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (guild_id, character_id),
    CHECK ((kind = 1) = (inviter_character_id IS NOT NULL)),
    CHECK (expires_at > created_at)
);
CREATE INDEX ix_guild_join_requests_character ON guild_join_requests (character_id, kind, created_at);
CREATE INDEX ix_guild_join_requests_expiry ON guild_join_requests (expires_at);

-- 서버 프로세스 사이 전달함(5.5절). 대상 캐릭터로 주소를 매기고, 그 캐릭터의 임대가 있는 서버가 꺼내 간 뒤 확인으로 지운다
CREATE TABLE social_deliveries
(
    delivery_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    target_character_id uuid NOT NULL REFERENCES characters (character_id),
    -- 0 귓속말, 1 길드 채팅, 2 소셜 변경 알림
    kind smallint NOT NULL CHECK (kind IN (0, 1, 2)),
    -- 채팅 행(kind 0, 1)만 채운다. EChatChannel 값(Whisper 4, Guild 3)과 보낸 캐릭터, 보낸 때의 표시 이름
    channel smallint NULL CHECK (channel IS NULL OR channel IN (3, 4)),
    sender_character_id uuid NULL REFERENCES characters (character_id),
    sender_name text NULL CHECK (sender_name IS NULL OR length(sender_name) BETWEEN 1 AND 24),
    -- 채팅은 { text }, 알림은 { notice, actorName, guildName }
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    -- 다음에 꺼내기로 내줄 수 있는 시각. 꺼낼 때 같은 문장에서 5초 뒤로 민다(재전송 간격을 DB가 관리)
    next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CHECK (expires_at > created_at),
    CHECK ((kind = 2) = (channel IS NULL)),
    CHECK ((kind = 2) OR (sender_character_id IS NOT NULL AND sender_name IS NOT NULL))
);
CREATE INDEX ix_social_deliveries_target ON social_deliveries (target_character_id, next_attempt_at, delivery_id);
CREATE INDEX ix_social_deliveries_expiry ON social_deliveries (expires_at);
```

- `down.sql`은 역순으로 표·인덱스를 지우고 `account_leases.character_id`를 뗀다.
- 친구 수 50·대기 요청 수는 행 수라 CHECK로 못 건다. 5.7절의 잠금 아래 세서 막고, 시험으로 상한을 확인한다. 길드 정원은 `member_count` CHECK(30)가 마지막 방어선이다.
- `member_count`는 `guild_members` 행 수와 항상 같아야 한다. 모든 가입·탈퇴·추방·해체가 길드 행 잠금 아래 두 값을 함께 바꾸고, 시험이 불변식을 확인한다.
- 마이그레이션 시험은 `MarketMigrationTests` 방식(검증 안 된 제약 0건, 인덱스 존재, down 뒤 재적용)을 따른다.

### 3.3 멱등과 원장

경제 효과(재화·아이템 변동)가 없으므로 `operationId`·작업 저장소(`IdempotentOperationExecutor`)·원장·outbox를 쓰지 않는다. 대신 **상태 기준 멱등**으로 결과 불명 재시도를 안전하게 만든다.

| 동작 | 이미 목표 상태일 때 | 결과 |
|---|---|---|
| 친구 요청 | 같은 요청이 이미 있음 | 기한만 갱신, 성공 |
| 친구 수락 | 이미 친구(요청 행 없음) | 성공 |
| 친구 거절·취소·삭제 | 요청·관계가 이미 없음 | 성공 |
| 길드 생성 | `(creator_character_id, create_request_id)` 행이 이미 있음 | 그 길드로 성공 |
| 가입 신청 수락·초대 수락 | 대상이 이미 그 길드원 | 성공 |
| 탈퇴 | 내 현재 가입의 `(guildId, membershipId)`가 요청 값과 다름(이미 탈퇴했거나 그 뒤 다른 가입) | 성공, 현재 가입은 건드리지 않음 |
| 추방 | 대상의 현재 `membershipId`가 요청 값과 다름(이미 나갔거나 재가입) | 성공, 재가입한 대상은 건드리지 않음 |
| 위임 | 현재 `leader_version`이 요청의 기대 값+1이고 대상(같은 `membershipId`)이 이미 길드장 | 성공(이 요청이 이미 적용됨). `leader_version`이 기대 값과도 기대 값+1과도 다르거나, +1인데 길드장이 대상이 아니면 `GUILD_LEADER_CHANGED`. `membershipId`가 다르면 `GUILD_NOT_MEMBER` |

- 길드 생성만 상태로 구분이 안 되어 클라이언트가 만든 `createRequestId`(UUID)를 마을 서버가 그대로 전달한다. actor(생성자)는 서버가 연결에서 정하므로 다른 캐릭터의 요청 ID를 재사용해도 다른 행이 된다.
- 위임은 추가로 **기대 `leader_version`을 묶는다**(Codex 2차 3). A→B 위임 성공 뒤 B→A 위임이 일어나고 늦게 도착한 A→B 재전송이 다시 실행되는 경우, `membershipId`만으로는 B가 여전히 같은 가입이라 막지 못한다. 위임마다 `leader_version`을 1 올리고 요청의 기대 값과 맞을 때만 적용하므로 늦은 재전송은 `GUILD_LEADER_CHANGED`로 거절된다(A가 다시 길드장이어도 버전이 2 앞서 있다).
- 탈퇴·추방·위임은 **기대 길드 ID와 가입 버전(`membershipId`)을 요청에 묶는다**(Codex 4). 스냅샷이 내 가입과 길드원마다 `membershipId`를 주고, 클라이언트는 화면에 보인 값을 그대로 보낸다. 결과 불명 뒤 재전송이 늦게 도착해도 같은 가입에만 작용하므로, 그사이 다시 가입한 길드나 재가입한 길드원을 탈퇴·추방하지 않는다.

## 4. 화면 구성

### 4.1 친구 창

```
┌ 친구 ─────────────────────────── 12/50 [X] ┐
│ [이름 입력____________] [친구 요청]          │
│ ┌탭: 친구 │ 받은 요청(3) │ 보낸 요청┐         │
│ ● 홍길동  궁수   신시      [귓속말][파티초대][삭제] │
│ ● 김철수  마검사 출정 중   [귓속말][  -  ][삭제] │
│ ○ 이영희  검사   오프라인  [  -  ][  -  ][삭제] │
│ ...                                        │
│ (받은 요청 탭) 박민수  [수락][거절]  6일 남음  │
│ (보낸 요청 탭) 최지우  [취소]        2일 남음  │
│ 갱신: 방금 / 결과 대기 중 안내 한 줄           │
└────────────────────────────────────────────┘
```

- 정렬: 신시 → 출정 중 → 오프라인, 같은 위치 안에서는 이름순(`FriendsViewRules`).
- [파티초대]는 상대가 **같은 마을 서버**에 있고 내가 파티 방에 있고 방 단계가 Open일 때만 켠다. 누르면 `TownPartyClient.Invite(name)`을 부른다(기존 경로, 서버가 다시 검증).
- [귓속말]은 상대가 접속 중일 때만 켜고, 채팅 입력 칸에 `/w 이름 `을 채워 포커스를 준다.
- [삭제]·[거절]은 확인 대화상자를 거친다.

### 4.2 길드 창

```
┌ 길드: 청룡단 ──────────── 18/30  [X] ┐
│ (길드 없음) [이름____] [길드 만들기]    │
│            [이름____] [가입 신청]      │
│            받은 초대: 백호회 [수락][거절] │
│ ─ 길드원 ──────────────────────────── │
│ ★ 홍길동 궁수   신시   (길드장)         │
│ ● 김철수 마검사 출정 중 [위임][추방]     │  ← 길드장에게만 버튼
│ ○ 이영희 검사   오프라인 [위임][추방]    │
│ ─ 가입 신청(길드장만) ────────────────── │
│ 박민수 [수락][거절]                    │
│ [이름____] [초대하기] (길드장)            │
│ [길드 탈퇴]  (길드장은 위임 뒤, 혼자면 해체) │
└──────────────────────────────────────┘
```

- 길드 없음/있음 두 상태를 한 창에서 전환한다(`GuildViewRules.ModeOf(snapshot)`).
- 가입 신청은 정확한 길드 이름으로만 보낸다(1차에는 길드 검색 목록이 없다).
- 길드장이 혼자일 때 [길드 탈퇴]는 "길드가 해체된다" 확인을 띄운다.

### 4.3 채팅

- `EChatTab`에 `Guild = 3`, `Whisper = 4`를 끝에 붙이고, 전체 탭은 모든 채널을 보인다(기존 규칙 유지).
- 입력 채널 목록에 "길드"를 추가한다(전체/파티/길드). 귓속말은 명령어로 보낸다: `/w 이름 내용`, `/귓 이름 내용`, `/r 내용`(마지막으로 받은 귓속말 상대에게 답장). 파싱은 `ChatViewRules.TryParseCommand`에 순수 규칙으로 둔다.
- 줄 서식: `[길드] 이름 : 내용`, 받은 귓속말 `[귓속말] 이름 : 내용`, 보낸 귓속말 `[귓속말] 이름에게 : 내용`. 색은 `HudChatView`에 길드·귓속말 색 필드를 추가한다.
- 상호작용 패널(`PlayerInteractionPanelOpener`)의 비활성 [귓속말] 버튼을 켜고 `/w 이름 `을 채운다. 같은 패널에 [친구 요청]·[길드 초대] (길드장) 버튼을 추가한다.

### 4.4 알림

친구 요청·길드 초대·가입 신청 도착, 수락됨, 추방됨, 길드장 위임은 시스템 채팅 한 줄(`ChatLog.AddSystem`)과 HUD 메뉴 버튼의 빨간 점(받은 요청 수 > 0)으로 알린다. 별도 팝업은 1차에 만들지 않는다.

## 5. 요청 흐름

### 5.1 경로 개요

```
클라이언트 ──SocialRequestMessage──▶ 마을 서버 SocialRelayServer ──HTTPS(서버 임대 Bearer)──▶ API ──▶ PostgreSQL
          ◀─SocialResult/Snapshot/Notice─┘                         ◀────────────── 결과·대상 캐릭터
클라이언트 ──ChatWhisperRequest / ChatRequest(Guild)──▶ 마을·던전 ChatServer ──▶ API(대상 해석·전달함 기록)
다른 서버 프로세스 ◀── SocialDeliveryPump(1초마다 전달함 꺼내기) ◀── API
```

친구·길드 **관리 요청은 마을 서버만** 받는다(성장·거래와 같은 경로, 5.9절). 귓속말·길드 채팅·알림 수신은 마을과 던전 모두에서 된다.

### 5.2 메시지(Mirror)

`01.Network/Social/`(클라이언트·서버 공용, `Shinshi.Runtime`)에 둔다. 모두 `readonly struct : NetworkMessage`이고 `SocialMessageSerializer`가 필드 순서를 고정한다(채팅·성장 메시지와 같은 방식).

| 메시지 | 방향 | 필드 |
|---|---|---|
| `SocialRequestMessage` | C→S | `ESocialAction Action`, `Guid RequestId`, `string Name`(대상 캐릭터 이름 또는 길드 이름), `string TargetId`(스냅샷에서 받은 대상 캐릭터 ID 또는 길드 ID), `string GuildId`(탈퇴·추방·위임의 기대 길드), `string MembershipId`(탈퇴는 내 가입, 추방·위임은 대상 가입, 3.3절), `long ExpectedLeaderVersion`(위임만, 그 외 0) |
| `SocialDeliveryAckMessage` | C→S | `long[] DeliveryIds`(받아서 `ChatLog`에 처리한 전달 ID, 한 메시지 최대 64개, 5.5절) |
| `SocialResultMessage` | S→C | `ESocialAction Action`, `Guid RequestId`, `ERejectReason Reason` |
| `SocialSnapshotMessage` | S→C | `long ServerTimeUnixMs`, `SocialFriendEntry[] Friends`, `SocialRequestEntry[] Incoming`, `SocialRequestEntry[] Outgoing`, `SocialGuildView Guild`(없으면 `HasGuild=false`), `SocialGuildMemberEntry[] Members`, `SocialRequestEntry[] GuildApplications`(길드장만, 그 외 빈 배열), `SocialGuildInviteEntry[] GuildInvites` |
| `SocialNoticeMessage` | S→C | `ESocialNotice Kind`, `string ActorName`, `string GuildName`, `long DeliveryId`(전달함 행 ID, 수신 확인 대상) |
| `ChatWhisperRequestMessage` | C→S | `string TargetName`, `string Text`(01.Network/Chat) |

- 항목 구조체: `SocialFriendEntry(characterId, name, classId, EPresence presence, bool sameServer)`, `SocialRequestEntry(characterId, name, classId, expiresAtUnixMs)`, `SocialGuildView(guildId, name, memberCount, EGuildRole myRole, myMembershipId, leaderVersion)`, `SocialGuildMemberEntry(characterId, membershipId, name, classId, EGuildRole role, EPresence presence, bool sameServer)`, `SocialGuildInviteEntry(guildId, guildName, inviterName, expiresAtUnixMs)`.
- `ESocialAction`: `Snapshot = 0, FriendRequest = 1, FriendAccept = 2, FriendDecline = 3, FriendCancel = 4, FriendRemove = 5, GuildCreate = 6, GuildApply = 7, GuildApplicationAccept = 8, GuildApplicationDecline = 9, GuildInvite = 10, GuildInviteAccept = 11, GuildInviteDecline = 12, GuildLeave = 13, GuildKick = 14, GuildDelegate = 15`. 값은 직렬화되므로 끝에만 붙인다.
- `EPresence`: `Offline = 0, Town = 1, Dungeon = 2`. `EGuildRole`: `Member = 0, Leader = 1`(DB `role`과 같은 값).
- `ESocialNotice`: `FriendRequested = 0, FriendAccepted = 1, GuildInvited = 2, GuildApplied = 3, GuildJoined = 4, GuildKicked = 5, GuildLeaderChanged = 6, GuildDisbanded = 7, GuildMemberLeft = 8`.
- 채팅 열거형은 끝에 붙인다: `EChatChannel.Guild = 3, Whisper = 4(받은 것), WhisperSent = 5(보낸 것의 확인 줄)`. `ChatBroadcastMessage`를 그대로 쓰고 `Sender`는 귓속말이면 **상대 이름**이다.
- `ChatBroadcastMessage`에 `long DeliveryId`를 끝에 추가한다. 전달함을 거친 줄(귓속말·길드 채팅)은 API의 `delivery_id`, 전체·파티·`WhisperSent`는 0이다. 클라이언트가 이 값으로 중복을 버린다(5.5절, Codex 6).
- **송신 허용과 수신 표시 규칙을 나눈다**(Codex 1). 지금 `ChatLog.AddMessage`는 `ChatRules.IsSendable`로 받은 줄의 채널을 걸러 `Whisper`·`WhisperSent`를 버린다. `ChatRules.IsSendable`(클라이언트가 `ChatRequestMessage`로 보낼 수 있는 채널: `All`·`Party`·`Guild`)과 `ChatRules.IsDisplayable`(서버가 보낸 줄로 표시할 채널: `All`·`Party`·`Guild`·`Whisper`·`WhisperSent`, `System`은 `AddSystem` 전용이라 제외)을 따로 두고 `ChatLog.AddMessage`는 `IsDisplayable`을 쓴다. 귓속말은 `ChatWhisperRequestMessage`로만 보낸다.
- `EChatReject` 끝에 `NoGuild = 8, WhisperTargetNotFound = 9, WhisperTargetOffline = 10, WhisperSelf = 11, BackendUnavailable = 12, FeatureDisabled = 13`을 붙이고 `ChatRules.Describe`에 안내 문구를 넣는다.
- 배열 길이 상한(직렬화 읽기 검사): 친구 50, 요청 50, 길드원 30, 신청 50, 받은 초대 5(`SocialLimits`). 받은 초대 5는 API가 초대할 때 대상 캐릭터 잠금 아래 세서 보장한다(5.4절). 넘는 길이는 읽기에서 실패시켜 변조 패킷으로 큰 배열을 만들지 못하게 한다.

`PROTOCOL_VERSION`은 **올린다**(현재 9 → 10, 다른 작업이 먼저 올리면 그다음 번호). 새 메시지 종류와 열거형 값이 추가되어 구 클라이언트는 받은 메시지를 해석하지 못한다. `Constants.network.cs` 주석에 "10: 소셜 메시지, 길드·귓속말 채팅 채널 추가"를 남긴다.

### 5.3 API 경로(서버 전용)

모든 경로는 `GameServerControllerBase` 계열로 **서버 임대 Bearer + `X-Shinshi-Instance-Id`**가 필요하다. 경로 상수는 `Contracts/Social/SocialRoutes.cs`, ASP.NET 템플릿은 `Shinshi.Api/Social/SocialRouteTemplates.cs`(테스트로 두 값 일치 확인, `TradeRouteTemplates` 방식). 요청 본문의 행위 캐릭터는 경로의 `{characterId}`이며, API는 **그 캐릭터의 살아 있는 임대가 호출 인스턴스에 있는지**를 먼저 확인한다(없으면 409 `SOCIAL_NOT_CONNECTED`). 마을 서버가 다른 캐릭터를 사칭하지 못하게 하는 두 번째 방어선이다.

| 경로 | 호출 역할 | 본문 → 응답 |
|---|---|---|
| `POST /v1/social/characters/{characterId}/snapshot` | 마을 | 없음 → `SocialSnapshotResponse`(2절 전체) |
| `POST /v1/social/characters/{characterId}/friends/requests` | 마을 | `{ targetName }` → `SocialActionResponse` |
| `POST /v1/social/characters/{characterId}/friends/requests/{otherId}/accept` · `/decline` | 마을 | 없음 |
| `POST /v1/social/characters/{characterId}/friends/requests/{targetId}/cancel` | 마을 | 없음 |
| `POST /v1/social/characters/{characterId}/friends/{friendId}/remove` | 마을 | 없음 |
| `POST /v1/social/characters/{characterId}/guilds` | 마을 | `{ name, createRequestId }` |
| `POST /v1/social/characters/{characterId}/guild-applications` | 마을 | `{ guildName }` |
| `POST /v1/social/characters/{characterId}/guild/applications/{applicantId}/accept` · `/decline` | 마을(길드장) | 없음 |
| `POST /v1/social/characters/{characterId}/guild/invites` | 마을(길드장) | `{ targetName }` |
| `POST /v1/social/characters/{characterId}/guild-invites/{guildId}/accept` · `/decline` | 마을 | 없음 |
| `POST /v1/social/characters/{characterId}/guild/leave` | 마을 | `{ guildId, membershipId }` |
| `POST /v1/social/characters/{characterId}/guild/members/{memberId}/kick` | 마을(길드장) | `{ guildId, membershipId }`(대상 가입) |
| `POST /v1/social/characters/{characterId}/guild/leader` | 마을(길드장) | `{ guildId, newLeaderCharacterId, membershipId, expectedLeaderVersion }`(대상 가입) |
| `POST /v1/social/characters/{characterId}/whispers` | 마을·던전 | `{ targetName, text }` → `{ result, targetName, localDeliveries: SocialDeliveryRow[] }` |
| `POST /v1/social/characters/{characterId}/guild/chat` | 마을·던전 | `{ text }` → `{ result, senderName, localDeliveries: SocialDeliveryRow[] }` |
| `POST /v1/social/instances/{instanceId}/deliveries/pull` | 마을·던전 | `{ bootId, ackDeliveries[{ deliveryId, recipientCharacterId }] }` → `{ deliveries: SocialDeliveryRow[] }` |

- `SocialDeliveryRow`(로컬 응답과 꺼내기 응답의 공통 행 계약, Codex 3차 1): `{ deliveryId, targetCharacterId, kind, channel, senderCharacterId, senderName, payload, createdAt }`. 채팅 행은 `channel`·보낸 사람·`payload.text`, 알림 행(`kind 2`)은 `channel`·보낸 사람이 null이고 `payload`에 `notice`·`actorName`·`guildName`이 있다. 서버는 **행에 저장된 내용으로** 메시지를 만들고 현재 요청 본문을 재사용하지 않는다(로컬 응답에는 다른 발신자의 앞선 행도 들어 있기 때문). 계약 파일은 `Contracts/Social/SocialDeliveryRow.cs`.
- `SocialActionResponse`: `{ status(Committed/Rejected), reasonCode, serverTimeUnixMs, changedCharacterIds[] }`. `changedCharacterIds`는 관계가 바뀐 **상대** 캐릭터로, API가 같은 트랜잭션에서 그들에게 알림 전달함 행(`kind 2`)을 넣었다는 표시다.
- HTTP 상태는 기존 `ApiResponseFactory.FromOperation` 규칙(200 성공, 409 규칙 거절, 400 형식 오류, 401/403 자격·역할, 429 제한, 503 일시 장애)을 따른다.
- 마을·던전 두 역할을 받는 경로는 `GameServerControllerBase.RequireCallerAsync(int role)`가 역할 하나만 받으므로 `RequireGameServerCallerAsync()`(마을 또는 던전) 오버로드를 추가한다. 관리 경로는 기존 `RequireCallerAsync(TOWN)`을 쓴다.
- 기능 차단 키 `social`을 `ServiceFeatureKeys`에 추가한다. 꺼지면 관리 경로와 **귓속말·길드 채팅도** `FEATURE_DISABLED(1100)`이다(11절 결정). 스냅샷·전달함 꺼내기·추방·탈퇴는 막지 않는다(관계 정리와 이미 쌓인 전달은 차단 중에도 허용). 채팅 쪽 거절은 `EChatReject.FeatureDisabled = 13`으로 안내한다.
- `whispers`·`guild/chat`의 `text`는 API가 길이(1~`Constants.Chat.MAX_MESSAGE_LENGTH`와 같은 `SocialLimits.MAX_CHAT_LENGTH` 100)만 다시 보고, 정리(`ChatRules.TryNormalize`)는 게임 서버가 끝낸 값을 받는다. `ChatRules`는 `Shinshi.Runtime`이라 백엔드가 쓸 수 없다.

### 5.4 동작별 규칙

| 동작 | 서버(게임) 검사 | API 규칙(트랜잭션 안) | 주요 거절 |
|---|---|---|---|
| 친구 요청 | 이름 정규화(`CharacterNameRules`), 본인 이름 아님 | 대상 살아 있음, 이미 친구 아님, 반대 방향 요청이 있으면 **즉시 친구 성립**, 양쪽 친구 수 < 50(성립 시), 내 보낸 요청 < 20, 대상 받은 요청 < 50 | `SOCIAL_TARGET_NOT_FOUND`, `SOCIAL_TARGET_SELF`, `FRIEND_ALREADY`, `FRIEND_LIMIT`, `FRIEND_TARGET_LIMIT`, `FRIEND_REQUEST_LIMIT`, `FRIEND_TARGET_REQUESTS_FULL` |
| 친구 수락 | `TargetId` GUID 형식 | 요청 행(상대→나, 만료 전) 있음, 양쪽 친구 수 < 50, 요청 삭제 + 친구 두 행 | `FRIEND_REQUEST_NOT_FOUND`, `FRIEND_LIMIT`, `FRIEND_TARGET_LIMIT` |
| 친구 거절·취소·삭제 | 형식 | 행 삭제(없으면 성공) | — |
| 길드 생성 | 이름 정규화(`GuildNameRules`), `RequestId` 비어 있지 않음 | 나 길드 없음, 이름 키 중복 없음, 길드 행(1명) + 길드장 행, 내가 보낸 가입 신청·받은 초대 모두 삭제 | `GUILD_NAME_INVALID`, `GUILD_NAME_TAKEN`, `GUILD_ALREADY_MEMBER` |
| 가입 신청 | 이름 정규화 | 길드 살아 있음, 나 길드 없음, 이미 초대받았으면 **즉시 가입**, 내 대기 신청 < 5, 길드 대기 < 50 | `GUILD_NOT_FOUND`, `GUILD_ALREADY_MEMBER`, `GUILD_APPLICATION_LIMIT`, `GUILD_PENDING_FULL`, `GUILD_FULL`(즉시 가입 시) |
| 신청 수락 | 형식 | 나 길드장, 신청 행 있음(만료 전), 신청자 길드 없음, 정원 < 30 → 가입 | `GUILD_NOT_LEADER`, `GUILD_APPLICATION_NOT_FOUND`, `GUILD_TARGET_IN_GUILD`, `GUILD_FULL` |
| 초대 | 이름 정규화 | 나 길드장, 대상 살아 있음·길드 없음, 이미 신청했으면 **즉시 가입**, 길드 대기 < 50, **대상 캐릭터 잠금 아래** 대상의 만료 전 받은 초대 < 5 | `GUILD_NOT_LEADER`, `SOCIAL_TARGET_NOT_FOUND`, `GUILD_TARGET_IN_GUILD`, `GUILD_PENDING_FULL`, `GUILD_TARGET_INVITES_FULL` |
| 초대 수락 | 형식 | 초대 행 있음, 나 길드 없음, 길드 살아 있음, 정원 < 30 → 가입, 내 다른 신청·초대 모두 삭제 | `GUILD_INVITE_NOT_FOUND`, `GUILD_ALREADY_MEMBER`, `GUILD_NOT_FOUND`, `GUILD_FULL` |
| 탈퇴 | 형식(`GuildId`, `MembershipId`) | 내 가입이 요청의 `(guildId, membershipId)`와 같을 때만 삭제·정원 −1(다르면 성공, 효과 없음). 길드장이고 다른 길드원이 있으면 거절, 혼자면 **해체**(길드 `disbanded_at`, 대기 신청·초대 삭제) | `GUILD_LEADER_MUST_DELEGATE` |
| 추방 | 형식, 본인 아님 | 나 길드장(요청 `guildId`의), 대상의 현재 가입이 요청 `membershipId`와 같을 때만 삭제·정원 −1(다르면 성공, 효과 없음) | `GUILD_NOT_LEADER`, `SOCIAL_TARGET_SELF` |
| 위임 | 형식, 본인 아님 | `leader_version`이 기대 값과 같고(3.3절의 이미 적용 판정 먼저), 나 길드장(요청 `guildId`의), 대상의 현재 가입이 요청 `membershipId`와 같음 → 역할 교환, `leader_character_id` 갱신, `leader_version + 1` | `GUILD_LEADER_CHANGED`, `GUILD_NOT_LEADER`, `GUILD_NOT_MEMBER` |

위임은 `ux_guild_members_leader`(길드당 길드장 1명) 때문에 한 트랜잭션 안에서 **기존 길드장 강등 → 새 길드장 승격 → `guilds.leader_character_id` 갱신** 순서로 쓴다(순서를 바꾸면 유일 위반). 가입(생성 포함)이 성립할 때마다 `membership_id`를 새 UUID로 만든다. 가입이 성립하면 그 캐릭터의 다른 길드 신청·초대 행을 모두 지운다. 만료 행은 조회·검사에서 없는 것으로 보고 정리 작업(`SocialMaintenanceService`, 6절)이 지운다.

### 5.5 귓속말·길드 채팅의 서버 간 전달

현재 서버 프로세스는 마을 1개(다중 마을 채널은 초기 구성에 없음)와 출정마다 던전 1개다. 서버끼리 직접 연결하는 경로는 없고 모든 서버가 API만 부른다(`BackendGateway`, 5초 heartbeat). 귓속말 상대는 마을·다른 던전 어디에나 있을 수 있다.

| 대안 | 내용 | 장점 | 단점 |
|---|---|---|---|
| A. 같은 프로세스 안만 | `TownServerRuntime.TryFindByName`처럼 같은 서버 접속자에게만 전달 | 구현 최소, API 부하 없음 | 출정 중 친구·길드원에게 닿지 않음. 길드 채팅은 마을에 없는 길드원이 못 받음. 요구 범위("오프라인이면 실패 안내")와 맞지 않음(출정 중인데 오프라인으로 보임) |
| **B. 백엔드 전달함 + 짧은 꺼내기(권장)** | API가 대상 캐릭터 앞으로 `social_deliveries` 행을 넣고, 각 서버가 1초마다 자기 인스턴스 접속 캐릭터 앞 행을 꺼내 전달 후 확인 삭제 | 기존 서버→API 패턴 그대로, 새 인프라 없음, 대상이 마을→던전으로 옮겨도 행이 캐릭터를 따라감, DB 트랜잭션으로 접속·길드원 판정이 정확 | 다른 프로세스 전달 지연 최대 약 1초+왕복. 인스턴스 수만큼 초당 꺼내기 요청(최대 마을 1 + 던전 25~100) |
| C. API 롱폴(LISTEN/NOTIFY) | 꺼내기 요청을 API가 최대 20초 붙잡고 PostgreSQL `NOTIFY`로 깨움 | 지연 수십 ms, 빈 요청 적음 | API 연결 수·차단기 정책 새로 정해야 함. 전송 시도 하나의 시간 상한이 `Constants.Network.MAX_BACKEND_TIMEOUT_SECONDS` 30초라(재시도마다 따로 적용) 한 번의 대기는 그보다 짧아야 함 |
| D. 서버 간 직접 연결 또는 Redis 발행 | 마을↔던전 직접 소켓, 또는 Redis pub/sub | 지연 최소 | 새 인프라·인증·배포. 기술 설계서가 Redis를 초기 필수 구성에서 뺐음 |

**결정: B**(11절). 대상이 같은 서버에 있든 다른 서버에 있든 **모든 귓속말·길드 채팅 줄을 전달함 행으로 먼저 기록**한다(Codex 5). API 응답의 `localDeliveries`는 호출 서버에 임대가 있는 수신자 앞으로 **아직 남아 있는 행 중 `delivery_id`가 새 행 이하인 것 전부를 수신자별 오름차순으로** 담는다(아래 순서 절). 서버는 꺼내기를 기다리지 않고 이 행들을 바로 보낸다. 행은 **클라이언트가 수신 확인을 보낸 뒤에만** 지운다(아래 수신 확인 절). 바로 보내려는 순간 대상이 이미 떠났으면 보내지 않고 행이 남아, 대상이 새로 접속한 서버가 꺼내 간다. 꺼내기 계약은 C로 바꿀 수 있게 `waitMs`(1차는 0 고정) 자리를 남긴다.

**귓속말 순서**

1. 클라이언트 `ChatClient.SendWhisper(target, text)`가 같은 정리 규칙과 간격 검사 후 `ChatWhisperRequestMessage`를 보낸다.
2. `ChatServer`가 채팅 속도 제한(기존 1초 1건, 귓속말·길드 채팅 공유)·본문 정리·대상 이름 정규화(`CharacterNameRules.TryNormalize`, 실패면 `WhisperTargetNotFound`)를 하고, 본인 이름 키면 `WhisperSelf`.
3. `SocialChatRelay`가 `POST .../whispers`를 보낸다. API는 `name_key`로 살아 있는 캐릭터를 찾고(없으면 `SOCIAL_TARGET_NOT_FOUND`), 살아 있는 임대가 없으면 `WHISPER_TARGET_OFFLINE`. 있으면 전달함 행(`kind 0`, 기한 60초)을 넣고, 대상 임대가 호출 인스턴스에 있으면 그 행을 `localDeliveries`로 돌려준다.
4. 서버는 성공이면 보낸 사람에게 `ChatBroadcastMessage(WhisperSent, 대상 표시 이름, text, 0)`을 보내고, `localDeliveries`의 각 행(이 요청의 새 행과 그 수신자 앞 앞선 대기 행, 오름차순)을 **행에 저장된 채널·보낸 사람 이름·본문으로** `ChatBroadcastMessage(row.channel, row.senderName, row.payload.text, row.deliveryId)`로 만들어 대상 연결에 보내고 수신 확인 대기에 넣는다. 알림 행이 섞여 있으면 `SocialNoticeMessage`로 보낸다. 실패면 `ChatRejectMessage`.
5. 다른 서버는 `SocialDeliveryPump`가 꺼낸 행을 대상 캐릭터의 연결에 같은 메시지(같은 `deliveryId`)로 보낸다.

**길드 채팅 순서**: `ChatRequestMessage(Guild, text)` → `ChatServer`가 검사 → `POST .../guild/chat`. API는 길드원이 아니면 `GUILD_NOT_MEMBER`(→ `EChatReject.NoGuild`), 아니면 살아 있는 임대가 있는 길드원(보낸 사람 포함) 모두에게 전달함 행(`kind 1`, 기한 60초)을 넣고, 호출 인스턴스 길드원의 행을 `localDeliveries`로 돌려준다. 서버는 수신자별 오름차순으로 각 행의 저장된 내용(`SocialDeliveryRow`)으로 메시지를 만들어 보내고 수신 확인 대기에 넣는다. 요청 본문은 재사용하지 않는다. 길드원 판정을 게임 서버 캐시가 아니라 API 트랜잭션에서 하므로 방금 추방된 캐릭터에게 새 줄이 가지 않는다.

**수신 확인(Codex 2차 1)**: Mirror `NetworkConnection.Send`는 보내기 버퍼에 넣을 뿐이고 연결 정리 때 버퍼를 버리므로, 서버가 `Send` 직후 API에 확인을 보내면 클라이언트가 받기 전에 행이 지워져 유실될 수 있다. 그래서 **클라이언트가 받은 `DeliveryId`를 확인해 준 행만 지운다.**

- 클라이언트: 0이 아닌 `DeliveryId`가 실린 채팅 줄(`ChatLog`)과 소셜 알림(`SocialClient`, `SocialNoticeMessage`) 모두 아래 공통 중복 검사 뒤 처리(표시 또는 중복으로 버림)하고 그 ID를 확인 대기에 넣는다. `SocialClient`가 0.5초(`Constants.Social.DELIVERY_ACK_INTERVAL_SECONDS`)마다 또는 64개가 차면 `SocialDeliveryAckMessage(long[] DeliveryIds)` 하나로 묶어 보낸다. 중복으로 버린 것도 확인한다(그래야 행이 지워진다). 알림(`kind 2`)도 같은 경로로 확인한다(Codex 3차 2).
- 공통 중복 제거 기억은 **하나를 공유**한다: 새 `01.Network/Social/DeliveryDedupe.cs`(정적, 최근 256개 고리 버퍼, `TryMarkSeen(long id)`)를 `ChatLog`와 `SocialClient`가 함께 쓰고, 확인 대기 목록도 이 파일이 갖는다. 전달 ID는 한 표의 IDENTITY라 채팅과 알림 사이에 겹치지 않으므로 한 기억으로 충분하고, 수명 규칙(서버 이동 중 유지, 완전 종료·로그아웃 때만 비움)을 한 곳에서 지킨다.
- 서버: 연결마다 "보냈고 확인 대기 중인 `deliveryId` → 수신 캐릭터" 표를 둔다(연결당 최대 256개, 넘으면 오래된 것부터 표에서 뺀다. 행은 DB에 남아 재전송된다). 확인 메시지의 ID 중 **그 연결에 보낸 기록이 있는 것만** 받아 API 확인 목록에 넣고 나머지는 버린다. 재전송 간격은 서버 메모리가 아니라 DB의 `next_attempt_at`이 관리한다(꺼낸 행은 5초 동안 다시 꺼내지지 않음, Codex 3차 3).
- 속도 제한: 확인 메시지는 연결당 1초 4건, 한 메시지 64개. 넘는 메시지와 배열 길이 초과는 읽기·처리에서 버린다(거절 안내 없음).
- 변조 방어: 남의 `DeliveryId`를 확인해도 서버 표에 없으면 버려진다. API 삭제는 `delivery_id`와 `target_character_id`(서버 표에 적어 둔 수신 캐릭터)가 둘 다 맞고 그 캐릭터의 임대가 호출 인스턴스에 있는 행만 지운다. 클라이언트가 보낸 값으로 수신 캐릭터를 정하지 않는다. 확인을 안 보내는 변조 클라이언트의 행은 꺼낼 때마다 `next_attempt_at`이 5초 뒤로 밀려 그동안 꺼내기 대상에서 빠지고, 꺼내기가 **수신자별 20개**로 잘리므로 한 클라이언트의 미확인 행이 인스턴스 공통 200개 상한을 독점하지 못한다. 따라서 같은 서버 다른 수신자의 전달은 늦어지지 않으며, 그 클라이언트는 자기 앞 행이 5초마다 다시 오다 60초 뒤 만료되는 것 외에 영향을 만들지 못한다.
- 연결이 끊기면 확인 대기 표를 버린다. 확인받지 못한 행은 남아 다음 서버(재접속 서버)가 꺼내 다시 보내고, 클라이언트 중복 제거가 이미 본 줄을 거른다.

**중복 제거(Codex 6, 2차 5)**: 같은 행이 두 번 올 수 있다(확인 유실, 5초 재전송, 대상이 서버를 옮긴 뒤 새 서버가 다시 꺼냄). **클라이언트가 `DeliveryId`로 버린다.** `ChatLog`가 최근 `deliveryId` 256개(`Constants.Chat.DELIVERY_DEDUPE_CAPACITY`)를 기억하고 0이 아닌 같은 값은 넣지 않는다. 이 기억은 `ChatLog`의 대화 기록과 수명이 같다. `ShinshiNetworkManager.OnClientDisconnect`는 서버 이동(마을→던전, 던전→마을) 중에는 `ChatClient.ClearHistory`를 건너뛰므로 **이동 중에는 기억이 유지되어** 새 서버가 다시 보낸 줄도 걸러진다. 완전 종료·로그아웃·캐릭터 선택 복귀로 대화 기록을 비울 때만 함께 비운다. 서버는 같은 프로세스 안 재전송을 위 확인 대기 표로 줄인다.

**순서(Codex 7)**: `BackendGateway`는 요청을 병렬로 보낼 수 있어 같은 사람이 연달아 보낸 두 줄이 뒤바뀔 수 있다. **발신자별 직렬 처리**를 택한다. `SocialChatRelay`는 발신 캐릭터마다 채팅 API 호출을 동시에 1건만 보내고, 그동안 온 줄은 발신자별 대기열(최대 3줄, `Constants.Social.MAX_QUEUED_CHAT_PER_SENDER`)에 두며 넘치면 `RateLimited`로 거절한다. 근거: 채팅 속도 제한이 1초 1줄이라 대기열이 거의 차지 않고, 같은 발신자 줄의 `delivery_id`가 커밋 순서대로 커지며, 꺼내기가 `delivery_id` 순이므로 받는 쪽에서도 발신자 순서가 유지된다. **서로 다른 발신자 사이의 순서는 보장하지 않는다**(동시 커밋의 ID 순서와 커밋 순서가 다를 수 있음). 시험: 같은 발신자 두 줄을 느린 가짜 게이트웨이로 보내도 받는 순서가 같다.

**로컬 즉시 전달과 꺼내기 사이의 순서(Codex 2차 2)**: 수신자 R 앞에 이전 행 m1이 남아 있는데(이동 직후, 재전송 대기) 같은 서버에서 새 줄 m2를 바로 보내면 m2가 m1을 앞지른다. 두 방법 중 **API가 앞선 대기 행을 함께 돌려주는 방식**을 택한다. API는 발신 트랜잭션에서 새 행을 넣은 뒤, 호출 인스턴스에 임대가 있는 각 수신자에 대해 그 수신자의 남은 행 중 `delivery_id <= 새 행`인 것을 오름차순으로(수신자당 최대 20개, 넘으면 그 수신자는 즉시 전달 없이 꺼내기에 맡긴다) `localDeliveries`에 담는다. API는 담은 행의 `next_attempt_at`도 꺼내기와 같이 5초 뒤로 민다. 서버는 수신자별로 이 순서대로, 행에 저장된 내용으로 보낸다. 같은 연결에 이미 보낸 기록이 있는 행도 다시 보내며 클라이언트 중복 제거가 거른다. 근거: 로컬 즉시 전달을 없애면 같은 마을 귓속말(가장 흔한 경우)도 최대 1초 늦어지고, 수신자 기준으로 한 번에 정렬해 보내면 같은 서버 안에서는 꺼내기와 같은 순서가 된다.

클라이언트 표시 규칙: `ChatLog`는 **받은 순서대로 붙인다.** 수신자 기준으로 지금까지 받은 최대 `DeliveryId`보다 작은 줄이 늦게 와도(다른 발신자 사이 동시 커밋, 재전송) 끼워 넣거나 버리지 않고 맨 아래에 그대로 붙이며, 중복 여부만 위 기억으로 판단한다. 같은 발신자 줄의 순서는 발신자별 직렬 처리와 위 정렬로 지켜진다.

**꺼내기(`SocialDeliveryPump`)**

- 인스턴스에 `CharacterId`가 있는 인증 연결이 1개 이상일 때만 `Constants.Social.DELIVERY_PULL_INTERVAL_SECONDS`(1.0, 11절 결정)마다 보낸다. 요청 하나가 끝나야 다음을 보낸다(동시 1건).
- API 질의: 호출 인스턴스의 살아 있는 임대 캐릭터 앞 행 중 `next_attempt_at <= clock_timestamp()`인 것을 **수신자별 `delivery_id` 순 최대 20개**(`SocialLimits.MAX_DELIVERIES_PER_RECIPIENT`), 전체 최대 200개(`SocialLimits.MAX_DELIVERIES_PER_PULL`)로 고르고, **같은 문장에서** 고른 행의 `next_attempt_at`을 5초(`SocialLimits.DELIVERY_RESEND_SECONDS`) 뒤로 민다.
  ```sql
  WITH ranked AS (
      SELECT d.delivery_id,
          row_number() OVER (PARTITION BY d.target_character_id ORDER BY d.delivery_id) AS recipient_rank
      FROM social_deliveries AS d
      JOIN account_leases AS lease ON lease.character_id = d.target_character_id
      WHERE lease.instance_id = @Instance AND lease.expires_at > clock_timestamp()
          AND d.expires_at > clock_timestamp() AND d.next_attempt_at <= clock_timestamp()
  ), picked AS (
      SELECT delivery_id FROM ranked WHERE recipient_rank <= @PerRecipient ORDER BY delivery_id LIMIT @Limit
  )
  UPDATE social_deliveries AS d SET next_attempt_at = clock_timestamp() + @Resend
  FROM picked WHERE d.delivery_id = picked.delivery_id
  RETURNING d.delivery_id, d.target_character_id, d.kind, d.channel, d.sender_character_id, d.sender_name,
      d.payload::text, d.created_at;
  ```
  응답은 `delivery_id` 순으로 정렬해 돌려준다(`RETURNING`은 순서를 보장하지 않으므로 API가 정렬). 인덱스 `ix_social_deliveries_target (target_character_id, next_attempt_at, delivery_id)`가 수신자별 조건과 정렬을 받는다. 같은 인스턴스의 꺼내기는 동시 1건이라 같은 행을 두 번 고르지 않는다.
- 확인: 다음 꺼내기 요청의 `ackDeliveries[{ deliveryId, recipientCharacterId }]`에는 **클라이언트가 수신 확인을 보낸 행만** 넣는다. API는 `delivery_id`와 `target_character_id`가 둘 다 맞고 그 캐릭터의 임대가 호출 인스턴스에 있는 행만 지운다. 응답을 잃으면 같은 확인을 다음 요청에 다시 넣는다.
- 대상이 이동 중(마을 연결 종료 → 던전 접속 전)이면 행이 남아 있다가 새 서버가 꺼낸다. 60초 안에 아무 서버에도 나타나지 않으면 만료로 버린다(보낸 사람은 이미 성공 안내를 받았다. 60초를 넘는 이동 중 유실은 알려진 한계로 둔다).
- API 장애·차단기 열림이면 꺼내기를 쉬고 `BackendCircuitBreaker` 규칙을 따른다. 새 귓속말·길드 채팅은 `EChatReject.BackendUnavailable`로 거절한다.

### 5.6 접속 표시 갱신

- 접속 = `account_leases`에 `character_id`가 그 캐릭터이고 `expires_at > clock_timestamp()`이며 인스턴스가 Offline이 아니고 heartbeat가 15초 안(기존 `ALREADY_CONNECTED` 판정과 같은 조건). 위치는 임대 `role`(0 마을 → `Town`, 1 던전 → `Dungeon`).
- 임대 INSERT 경로는 두 곳이다. 마을 입장 교환 `PostgresAuthRepository.TownJoin.cs`(219행 부근 `INSERT ... ON CONFLICT DO UPDATE`)에 `character_id`를 넣고 `DO UPDATE SET`에도 `character_id = EXCLUDED.character_id`를 넣는다. `PostgresAccountLeaseRepository.TryAcquireAsync`(306행 부근)는 운영 호출자가 없지만(시험·이전 경로용, Codex 확인) 캐릭터를 모르므로 `ON CONFLICT DO UPDATE SET`에서 `character_id = NULL`을 명시해 이전 캐릭터 값이 남지 않게 한다(오프라인 표시). 던전 이관(`TransferAsync`의 UPDATE)은 `character_id`를 건드리지 않아 그대로 이어진다.
- 해제(마을 연결 종료 release, 출정 종료 `ReleaseForClosedSessionAsync`, 만료)는 행 삭제나 만료라 별도 처리 없이 오프라인이 된다.
- **던전 연결 종료는 곧바로 오프라인이 아니다**(Codex 2차 4). `ShinshiServerAuthenticator`는 연결 종료 때 마을 임대만 해제하고, 던전 임대는 서버 heartbeat(`PostgresServerPoolRepository`의 임대 연장)가 연결과 무관하게 계속 늘린다. 따라서 던전에서 연결이 끊긴 캐릭터는 재접속 유예 동안과 출정이 끝나 임대가 반환될 때까지 **"출정 중"으로 표시**된다. 이 동안 귓속말은 성공으로 처리되어 전달함에 남고, 재접속하지 않으면 60초 뒤 만료된다(알려진 한계).
- 화면 갱신은 **당겨 오기**다: 창을 열 때, 창이 열려 있는 동안 `Constants.Social.SNAPSHOT_REFRESH_SECONDS`(30초)마다, 소셜 알림(`kind 2`)을 받았을 때 스냅샷을 다시 요청한다. 접속·종료 순간의 밀어 주기 알림은 넣지 않는다(11절 결정, 임대 만료에는 사건이 없어 정확히 만들 수도 없다).
- 지연은 두 단계로 나눠 본다(Codex 12, 2차 4). **서버 판정 지연**은 경우별로 다르다.

| 경우 | API가 오프라인으로 판정하는 시점 |
|---|---|
| 마을 정상 종료(연결 종료) | 마을 서버의 임대 해제 요청이 처리된 즉시 |
| 마을 서버 비정상 종료 | 임대·heartbeat 기준 최대 15초 뒤 |
| 던전 연결 종료 | 출정 종료로 임대가 반환(`ReleaseForClosedSessionAsync`)되거나 마을로 돌아와 새 임대가 생길 때까지 "출정 중". 던전 서버 비정상 종료는 최대 15초 뒤 |

  **화면 반영 지연**: 창이 열려 있으면 판정 뒤 최대 30초(갱신 주기)+왕복, 창을 새로 열면 왕복만. 마을 비정상 종료를 창을 열어 둔 채 보는 최악은 약 45초다.

### 5.7 동시성

모든 쓰기는 기존 저장소와 같이 `SessionDb.BeginAsync`(READ COMMITTED)로 연 한 트랜잭션이며 `SessionDb.RetryAsync`·`PostgresTransactionPolicy.ApplyAsync`로 일시 오류를 재시도한다. **잠금 순서는 `계정(캐릭터 삭제만) → 캐릭터 행 → 길드 행`**으로 고정하고, 캐릭터 행 잠금은 소셜·삭제 모두 `FOR NO KEY UPDATE`로 맞춘다. FK 검사가 거는 `FOR KEY SHARE`는 `FOR NO KEY UPDATE`와 충돌하지 않으므로 전달함·관계 행 INSERT의 FK 검사가 잠금 대기를 만들지 않는다(5.11절).

- 캐릭터 잠금: 관련 캐릭터 전부(행위자, 상대)를 `character_id` 오름차순으로 `SELECT ... FROM characters WHERE character_id = ANY(@Ids) AND deleted_at IS NULL ORDER BY character_id FOR NO KEY UPDATE`로 잡는다. 같은 캐릭터를 건드리는 소셜 작업이 줄을 서고(친구 수·대기 수·길드 소속 검사가 원자적), 캐릭터 삭제의 UPDATE와도 충돌해 삭제 중인 캐릭터와 관계가 생기지 않는다. 성장 작업의 `FOR UPDATE OF character`와 잠깐 경합하지만 소셜 트랜잭션은 짧다.
- 길드 잠금: 캐릭터 다음에 `SELECT ... FROM guilds WHERE guild_id = @G FOR UPDATE`. 정원 증가는 `UPDATE guilds SET member_count = member_count + 1, revision = revision + 1 WHERE guild_id = @G AND disbanded_at IS NULL AND member_count < @Max`의 영향 행 수로 판정한다.
- 경합별 결과

| 경합 | 결과 |
|---|---|
| 같은 캐릭터를 두 길드가 동시에 수락 | 신청자 캐릭터 잠금에서 줄을 선다. 뒤 트랜잭션은 소속을 보고 `GUILD_TARGET_IN_GUILD`. 그래도 `guild_members` PK가 최종 방어 |
| 정원 29에서 두 명 동시 수락 | 길드 행 잠금으로 직렬, 뒤는 `GUILD_FULL`. CHECK 30이 최종 방어 |
| 길드장 위임 두 건 동시 | 길드 행 잠금으로 직렬, 뒤 요청은 기대 `leader_version`이 이미 올라가 `GUILD_LEADER_CHANGED`(버전 검사가 길드장 검사보다 먼저) |
| 위임과 추방 동시 | 직렬. 위임 뒤 이전 길드장의 추방은 `GUILD_NOT_LEADER` |
| 마지막 1인 탈퇴(해체)와 가입 수락 동시 | 해체가 먼저면 수락은 `GUILD_NOT_FOUND`, 수락이 먼저면 길드원이 2명이라 탈퇴는 `GUILD_LEADER_MUST_DELEGATE` |
| 같은 이름 길드 동시 생성 | `ux_guilds_name_key_alive` 유일 위반을 `GUILD_NAME_TAKEN`으로 바꾼다(SQLSTATE 23505, 제약 이름으로 구분) |
| A→B, B→A 친구 요청 동시 | {A, B} 잠금으로 직렬, 뒤 요청이 반대 요청을 보고 친구 성립 |
| 친구 수 49에서 두 수락 동시 | 행위자 잠금으로 직렬, 뒤는 `FRIEND_LIMIT` |
| 소셜 작업과 캐릭터 삭제 동시 | 삭제가 캐릭터 행을 UPDATE하므로 직렬. 삭제가 먼저면 소셜은 `SOCIAL_TARGET_NOT_FOUND` |

### 5.8 변조 클라이언트 방어

- 행위 캐릭터는 서버가 `SessionBinding.CharacterId`에서만 꺼낸다. 메시지에는 대상(이름 또는 스냅샷의 ID)만 있다. `CharacterId`가 없는 연결(개발 토큰)은 `SessionMismatch`로 거절한다.
- API는 경로 캐릭터의 임대가 호출 인스턴스에 있는지 확인한다(`SOCIAL_NOT_CONNECTED`). 대상 ID는 관계 행으로만 의미가 있어(내 요청·내 길드원인지 트랜잭션에서 확인) 임의 ID를 넣어도 남의 관계를 바꾸지 못한다.
- 이름·길드 이름은 서버가 정규화한 뒤 보낸다. 본문은 기존 `ChatRules`(제어·서식 문자 거절, 꺾쇠 전각 치환)로 정리하고 보낸 사람 이름은 서버가 결합에서 넣는다.
- 배열·문자열 길이는 직렬화 읽기와 서버 검사에서 상한을 둔다(`Constants.Town.MAX_GROWTH_ID_LENGTH`처럼 `SocialLimits.MAX_NAME_INPUT_LENGTH = CharacterNameRules.MAX_INPUT_LENGTH`).
- 스냅샷은 소유자 연결에만 보낸다. 다른 캐릭터의 친구 목록·길드 신청 목록을 요청할 경로가 없다. 신청 목록은 API가 길드장일 때만 채운다.
- 남의 접속 위치는 친구·같은 길드원에게만 보인다(스냅샷 질의가 관계 행으로만 대상 캐릭터를 고른다). 이름으로 접속 여부를 떠보는 경로는 귓속말 실패 안내뿐이며, 이는 요구 범위상 허용한다.

### 5.9 속도 제한과 역할별 허용

| 단계 | 제한 | 값(기획 근거 없는 구현 값) |
|---|---|---|
| 클라이언트 | 버튼 잠금(응답 대기 중), 채팅 송신 간격 | 기존 `ChatClient` 간격 |
| 게임 서버 `SocialRelayServer` | 연결마다 소셜 관리 요청 창 | 1초 2건(`Constants.Social.REQUESTS_PER_WINDOW`), 스냅샷은 2초 1건, 쓰기는 연결당 동시 1건(처리 중 다른 요청은 `PendingCommit`, 같은 `RequestId` 재전송은 무시) |
| 게임 서버 `ChatServer` | 귓속말·길드 채팅·전체·파티가 같은 창 | 기존 1초 1건 |
| API | 캐릭터별 인메모리 제한(`InboxRequestLimiter` 방식) | 관리 쓰기 1분 30회, 친구 요청·길드 초대 1시간 30회, 귓속말·길드 채팅 1분 60회. 넘으면 429 `RATE_LIMITED` |

- 거절 안내는 기존 `REJECT_NOTICE_INTERVAL_SECONDS` 간격 안에서만 보낸다(변조 클라이언트가 안내를 폭주시키지 못하게).
- 관리 요청은 마을 역할만 받는다. 던전의 `SocialRelayServer`는 `Snapshot`을 포함한 관리 요청을 `WrongLocation`으로 거절하고 채팅 중계와 알림 수신만 한다. 던전에서는 친구·길드 창을 열지 않는다(11절 결정, 채팅·알림만).
- 출정 대기·카운트다운 중에도 소셜 관리는 막지 않는다(경제·준비 상태와 무관).

### 5.10 실패 경로

| 상황 | 서버 처리 | 화면 |
|---|---|---|
| API 응답 전 타임아웃·전송 실패 | 요청 결과 불명. 서버가 `SocialResultMessage(Action, RequestId, BackendUnavailable)`를 보내고 스냅샷을 다시 조회한다. 조회가 성공하면 `SocialSnapshotMessage`, 조회도 실패하면 `SocialResultMessage(Snapshot, Guid.Empty, BackendUnavailable)` | 결과 불명을 받으면 "결과를 확인하지 못했습니다." 뒤이어 스냅샷을 **실제로 받았을 때만** "목록을 새로 고쳤습니다."(Codex 10). 조회 실패를 받으면 "목록을 받지 못했습니다. 잠시 후 창을 다시 열어 주세요."이고 화면은 마지막 스냅샷을 유지하되 쓰기 버튼을 잠근다. 사용자는 같은 동작을 다시 눌러도 된다(3.3절 상태 멱등). 길드 생성은 같은 `RequestId`를 다시 쓴다 |
| 차단기 열림·`gateway == null` | 즉시 `BackendUnavailable` | 같은 안내, 버튼 잠금 해제 |
| 409 규칙 거절 | 코드 그대로 `ERejectReason`으로 | 사유별 문구(`SocialViewRules.Describe`) |
| 429 | `RateLimited` | "잠시 후 다시 시도해 주세요" |
| 기능 차단 | `FeatureDisabled` | "현재 사용할 수 없는 기능입니다" |
| 귓속말 상대 오프라인 | `ChatRejectMessage(WhisperTargetOffline)` | "[시스템] 상대가 접속 중이 아닙니다." |
| 귓속말 상대 이름 없음 | `ChatRejectMessage(WhisperTargetNotFound)` | "[시스템] 해당 이름의 캐릭터가 없습니다." |

`ChatRejectMessage`는 사유만 담으므로 귓속말 실패 안내에는 **대상 이름을 넣지 않는다**(Codex 11). 메시지에 요청 식별자를 추가하는 대신 기존 계약을 유지하는 쪽을 택했다. 같은 거절 사유는 `REJECT_NOTICE_INTERVAL_SECONDS` 간격으로만 오므로 연달아 보낸 귓속말 중 어느 것이 실패했는지는 입력 직후의 안내 순서로만 구분된다.
| 길드 없이 길드 채팅 | `NoGuild` | "길드에 가입한 뒤 길드 채팅을 보낼 수 있습니다." |
| 전달함 꺼내기 실패 | 다음 주기에 같은 확인 목록으로 재시도 | 표시 없음(지연) |
| 마을 연결 끊김 | 대기 요청·제한 기록 삭제(`RemoveMember`) | 재접속 후 창을 열면 새 스냅샷 |

### 5.11 캐릭터 삭제

캐릭터 삭제는 이미 **접속 중이면 거절**(`ALREADY_CONNECTED`)하므로 오프라인 상태에서만 일어난다. `PostgresCharacterRepository.DeleteEffectAsync`(계정 → 캐릭터 잠금을 이미 잡은 트랜잭션)에 다음을 추가한다.

**잠금 강도 변경(Codex 3)**: 지금 삭제는 캐릭터 행을 `FOR UPDATE`(138행 부근)로 잡는다. 서로 길드원인 두 캐릭터 A·B를 동시에 삭제하면, A의 트랜잭션이 B 앞 알림 전달함 행을 넣을 때 FK 검사가 B 행에 `FOR KEY SHARE`를 요청하고 B의 삭제가 쥔 `FOR UPDATE`에 막히며, B도 반대로 막혀 교착이 된다. 소프트 삭제는 키를 바꾸지 않으므로 이 잠금을 **`FOR NO KEY UPDATE`로 낮춘다**. 이후 `UPDATE characters SET deleted_at = ...`도 키 열을 바꾸지 않아 `FOR NO KEY UPDATE` 수준이다. `FOR KEY SHARE`(FK 검사)는 `FOR NO KEY UPDATE`와 충돌하지 않으므로 교착이 생기지 않는다. 같은 캐릭터의 동시 삭제·소셜 작업은 여전히 `FOR NO KEY UPDATE`끼리 직렬된다. 잠금 순서는 `계정 → 삭제 캐릭터 → 길드 행`이며, 알림 대상 길드원 캐릭터는 잠그지 않고(FK 검사만) `deleted_at IS NULL`인 길드원에게만 알림 행을 넣는다. 시험: 서로 길드원·친구인 두 캐릭터의 동시 삭제가 교착 없이 둘 다 끝난다.

1. 길드장이고 다른 길드원이 있으면 거절: 새 코드 `CHARACTER_IS_GUILD_LEADER`(11절 결정, 위임 뒤 삭제하게 함). 혼자인 길드장이면 길드를 해체한다.
2. 길드원이면 길드 행을 잠그고(`캐릭터 → 길드` 순서) 탈퇴 처리, 남은 살아 있는 길드원에게 `GuildMemberLeft` 알림 전달함 행.
3. `friendships` 양방향, `friend_requests` 양방향, `guild_join_requests`(본인 행, 본인이 초대자인 행의 `inviter_character_id`는 그대로 두되 표시 시 삭제 캐릭터 이름 대신 "알 수 없음"), `social_deliveries`(대상 본인) 삭제.

삭제 거절 순서는 기존 검사(접속 → 출정 → 경매 등록 → revision) 뒤에 길드장 검사를 둔다. 운영 어드민의 캐릭터 조회에 길드 이름 표시는 2차.

## 6. 코드 구조

> 아래 표는 설계 시점의 계획이다. 실제 이름·경로와 달라진 점은 13절 구현 메모를 따른다.

| 파일(신규) | 역할 |
|---|---|
| `Backend/src/Shinshi.Data/Migrations/0019_social.up.sql`·`.down.sql` | 3.2절 DDL |
| `Backend/src/Shinshi.Data/Social/PostgresSocialRepository.cs`(+ `.Friends.cs`, `.Guilds.cs`, `.Deliveries.cs` partial) | Dapper 저장소. `SessionDb.BeginAsync`·잠금 순서·23505 변환 |
| `Backend/src/Shinshi.Data/Social/SocialModels.cs` | 행·결과 레코드(`SocialSnapshotRow`, `SocialActionOutcome` 등) |
| `Backend/src/Shinshi.Api/Social/SocialController.cs` | 관리·스냅샷 경로(마을) |
| `Backend/src/Shinshi.Api/Social/SocialChatController.cs` | 귓속말·길드 채팅·전달함 꺼내기(마을·던전) |
| `Backend/src/Shinshi.Api/Social/SocialRouteTemplates.cs`, `SocialRequestLimiter.cs`, `SocialMaintenanceService.cs`(60초마다 만료 요청·전달함 정리, `GameSessionMaintenanceService` 방식) | 템플릿·API 제한·정리 |
| `Assets/01.Scripts/00.Common/Contracts/Social/SocialRoutes.cs`, `SocialLimits.cs`, `GuildNameRules.cs`, `SocialSnapshotResponse.cs`, `SocialActionResponse.cs`, `SocialWhisperRequest/Response.cs`, `SocialGuildChatRequest/Response.cs`, `SocialDeliveryPullRequest/Response.cs`, `SocialDeliveryRow.cs`, 요청 본문 DTO | Unity·백엔드 공용 계약 |
| `Assets/01.Scripts/00.Common/Constants/Constants.social.cs` | 꺼내기 주기, 스냅샷 갱신 주기, 서버 요청 창, 발신자별 채팅 대기열 3줄(`MAX_QUEUED_CHAT_PER_SENDER`), 수신 확인 묶음 주기 0.5초(`DELIVERY_ACK_INTERVAL_SECONDS`)·확인 메시지 연결당 1초 4건·64개 |
| `Assets/01.Scripts/01.Network/Social/` `ESocialAction.cs`, `ESocialNotice.cs`, `EPresence.cs`, `EGuildRole.cs`, `SocialRequestMessage.cs`, `SocialResultMessage.cs`, `SocialSnapshotMessage.cs`, `SocialNoticeMessage.cs`, `SocialDeliveryAckMessage.cs`, `DeliveryDedupe.cs`(채팅·알림 공통 중복 제거와 확인 대기), 항목 구조체 5종, `SocialMessageSerializer.cs`, `SocialClient.cs` | 메시지와 클라이언트 창구(`TownGrowthClient`처럼 정적 창구, 스냅샷 보관·이벤트) |
| `Assets/01.Scripts/01.Network/Chat/ChatWhisperRequestMessage.cs` | 귓속말 요청 |
| `Assets/01.Scripts/98.Servers/01.Network/Social/SocialRelayServer.cs` | 관리 요청 검증·API 중계·결과/스냅샷 응답(`GrowthRelayServer` 구조), 역할별 허용 |
| `Assets/01.Scripts/98.Servers/01.Network/Social/SocialChatRelay.cs` | `ChatServer`가 부르는 귓속말·길드 채팅 API 중계와 로컬 전달 |
| `Assets/01.Scripts/98.Servers/01.Network/Social/SocialDeliveryPump.cs`, `SocialDeliveryDedupe.cs`, `SocialApiRules.cs`(응답 코드 → `ERejectReason`), `SocialServerInstaller.cs`, `ICharacterConnectionLookup.cs` + `MirrorCharacterConnectionLookup.cs` | 꺼내기·중복 제거·코드 변환·설치, 캐릭터 ID → 연결 찾기 |
| `Assets/01.Scripts/06.UI/Social/Friends/FriendsView.cs`, `FriendsViewRules.cs`, `FriendsPreviewBinder.cs`, `Editor/FriendsWindowBuilder.cs` | 친구 창(EquipmentOption 구조) |
| `Assets/01.Scripts/06.UI/Social/Guild/GuildView.cs`, `GuildViewRules.cs`, `GuildPreviewBinder.cs`, `Editor/GuildWindowBuilder.cs` | 길드 창 |
| `Assets/01.Scripts/06.UI/Binding/FriendsBinder.cs`, `GuildBinder.cs`, `SocialNoticeBinder.cs` | `SocialClient` 구독, 요청 전송, 알림을 채팅·배지로 |
| `Assets/01.Scripts/06.UI/Binding/SocialViewRules.cs` | 거절 사유 → 안내 문구(`Describe`), 결과 불명·조회 실패 안내(5.10절), 친구·길드 창 공용 |
| `Assets/02.Prefab/UI/Windows/PF_FriendWindow.prefab`, `PF_GuildWindow.prefab` | 빌더 산출물, 비활성 저장 |
| `Backend/tests/Shinshi.Backend.Tests/Social/` `SocialMigrationTests.cs`, `SocialFriendsPostgresTests.cs`, `SocialGuildsPostgresTests.cs`, `SocialConcurrencyPostgresTests.cs`, `SocialDeliveriesPostgresTests.cs`, `SocialHttpTests.cs`, `SocialContractParityTests.cs` | 9절 |
| `Assets/Tests/EditMode/` `SocialRelayServerTests.cs`, `SocialChatRelayTests.cs`, `SocialDeliveryPumpTests.cs`, `SocialMessageSerializerTests.cs`, `GuildNameRulesTests.cs`, `FriendsViewRulesTests.cs`, `GuildViewRulesTests.cs`, `ChatCommandRulesTests.cs` | 9절 |

| 파일(수정) | 변경 |
|---|---|
| `00.Common/Constants/Constants.network.cs` | `PROTOCOL_VERSION` 10과 주석 |
| `00.Common/Constants/Constants.chat.cs` | `DELIVERY_DEDUPE_CAPACITY = 256`(클라이언트 전달 ID 중복 제거) |
| `00.Common/Contracts/ContractCodes.cs`, `00.Common/Types/ERejectReason.cs` | 새 거절 코드(아래 표), 두 파일 같은 번호(기존 `ContractCodeParityTests` 확장) |
| `Backend/src/Shinshi.Contracts/Admin/ServiceState/ServiceFeatureKeys.cs` | `SOCIAL = "social"`과 목록 |
| `01.Network/Chat/EChatChannel.cs`, `EChatReject.cs`, `ChatRules.cs`, `ChatClient.cs`, `ChatMessageSerializer.cs`, `ChatBroadcastMessage.cs` | 채널·거절 추가, `IsSendable`에 Guild, 수신 표시용 `IsDisplayable` 신설, `SendWhisper`와 마지막 귓속말 상대(`/r`), `ChatBroadcastMessage.DeliveryId`와 직렬화 |
| `01.Network/Chat/ChatLog.cs` | `AddMessage`가 `IsSendable` 대신 `IsDisplayable`로 거름(지금은 귓속말 줄을 버림, Codex 1), 최근 `deliveryId` 256개 중복 제거와 처리한 ID의 수신 확인 대기. 기억은 대화 기록과 수명이 같아 서버 이동 중(`OnClientDisconnect`가 `ClearHistory`를 건너뛰는 동안)에는 유지하고, 완전 종료·로그아웃 때 대화 기록을 비우는 `Clear`에서만 함께 비운다(Codex 2차 5) |
| `01.Network/ShinshiNetworkManager.cs` | `OnStartClient`(360행 부근, 다른 `*Client.RegisterHandlers` 옆)에 `SocialClient.RegisterHandlers()`. 연결이 끝날 때 `SocialClient`의 스냅샷·대기 요청·받은 요청 배지와 `ChatClient`의 마지막 귓속말 상대를 비우는 초기화를 기존 클라이언트 정리 지점에 연결(던전 이동·재접속마다 이전 연결 값이 남지 않게, Codex 8) |
| `98.Servers/01.Network/Chat/ChatServer.cs`, `ChatServerHost.cs`, `ChatServerInstaller.cs` | 귓속말 처리기 등록, 길드 채널은 `SocialChatRelay`에 넘김(응답이 비동기라 `Handle`이 곧바로 방송하지 않는 경로 추가), 로그 싱크에 Guild·Whisper 기록 |
| `98.Servers/01.Network/Bootstrap/ServerBootstrap.cs` | 두 역할 모두 `SocialServerInstaller.Install(manager, Backend, role)` |
| `98.Servers/05.Town/TownServerRuntime.cs`·`TownServerHost.cs` | 마을에서 `SocialRequestMessage` 처리기 연결, `RemoveMember`에서 소셜 대기 삭제 |
| `Backend/src/Shinshi.Api/Sessions/GameServerControllerBase.cs` | `RequireGameServerCallerAsync()`(마을 또는 던전) |
| `Backend/src/Shinshi.Api/Program.cs`(또는 서비스 등록 확장) | 저장소·제한기·정리 서비스 등록 |
| `Backend/src/Shinshi.Data/Auth/PostgresAuthRepository.TownJoin.cs`, `PostgresAccountLeaseRepository.cs` | 임대 INSERT 두 곳에 `character_id`(TownJoin은 캐릭터 값, TryAcquire는 NULL 대입, 5.6절) |
| `Backend/src/Shinshi.Data/Characters/PostgresCharacterRepository.cs` | 삭제 잠금 `FOR UPDATE` → `FOR NO KEY UPDATE`, 소셜 정리와 길드장 거절(5.11절) |
| `06.UI/Hud/HudChatView.cs`, `EChatTab.cs`, `06.UI/Binding/ChatViewRules.cs` | 길드·귓속말 탭과 색, 명령어 파싱, 채널 목록에 길드. 탭 수가 `TAB_COUNT` 고정 배열이라 HUD 프리팹(또는 `VillageUiPreviewBuilder`)의 탭 버튼 2개 추가가 필요 |
| `06.UI/Common/PlayerInteractionPanelOpener.cs` + 패널 프리팹 | 귓속말 버튼 켜기, 친구 요청·길드 초대 버튼 |
| `04.Systems/Hotkey/EHotkeyAction.cs`, `Assets/09.Data/UI/HotkeyConfig.asset` | `Friends = 12`, `Guild = 13`과 키(8절) |
| `06.UI/Binding/ReleaseMenuActions.cs`, `Assets/Editor/ReleaseMenuWiring.cs`(115행 부근 메뉴 버튼 연결) | 친구·길드 메뉴 항목과 받은 요청 배지 |
| `docs/coding-convention.md` 0장 폴더 표 | `98.Servers/01.Network/Social/` 추가 |
| `Backend/README.md`, `docs/03-technical-design.md` 5장 엔드포인트 표, `docs/04-architecture.md` 7.1, `AGENTS.md` 마이그레이션 범위, 로드맵 | 구현 후 갱신 |

**새 거절 코드(1600대 소셜)**

| 번호 | ContractCodes / ERejectReason | 뜻 |
|---|---|---|
| 1600 | `SOCIAL_TARGET_NOT_FOUND` / `SocialTargetNotFound` | 이름·ID의 살아 있는 캐릭터가 없다 |
| 1601 | `SOCIAL_TARGET_SELF` / `SocialTargetSelf` | 자기 자신 |
| 1602 | `SOCIAL_NOT_CONNECTED` / `SocialNotConnected` | 행위 캐릭터의 임대가 호출 서버에 없다 |
| 1610 | `FRIEND_ALREADY` / `FriendAlready` | 이미 친구 |
| 1611 | `FRIEND_LIMIT` / `FriendLimit` | 내 친구 50명 |
| 1612 | `FRIEND_TARGET_LIMIT` / `FriendTargetLimit` | 상대 친구 50명 |
| 1613 | `FRIEND_REQUEST_LIMIT` / `FriendRequestLimit` | 보낸 대기 요청 20건 |
| 1614 | `FRIEND_TARGET_REQUESTS_FULL` / `FriendTargetRequestsFull` | 상대 받은 요청 50건 |
| 1615 | `FRIEND_REQUEST_NOT_FOUND` / `FriendRequestNotFound` | 요청이 없거나 만료 |
| 1620 | `WHISPER_TARGET_OFFLINE` / `WhisperTargetOffline` | 상대 미접속 |
| 1630 | `GUILD_NAME_INVALID` / `GuildNameInvalid` | 길드 이름 규칙 위반 |
| 1631 | `GUILD_NAME_TAKEN` / `GuildNameTaken` | 살아 있는 길드와 이름 키 중복 |
| 1632 | `GUILD_ALREADY_MEMBER` / `GuildAlreadyMember` | 내가 이미 길드에 있다 |
| 1633 | `GUILD_TARGET_IN_GUILD` / `GuildTargetInGuild` | 상대가 이미 길드에 있다 |
| 1634 | `GUILD_NOT_FOUND` / `GuildNotFound` | 없거나 해체 |
| 1635 | `GUILD_NOT_MEMBER` / `GuildNotMember` | 길드원이 아니다 |
| 1636 | `GUILD_NOT_LEADER` / `GuildNotLeader` | 길드장이 아니다 |
| 1637 | `GUILD_FULL` / `GuildFull` | 정원 30명 |
| 1638 | `GUILD_LEADER_MUST_DELEGATE` / `GuildLeaderMustDelegate` | 다른 길드원이 있어 길드장은 탈퇴 불가 |
| 1639 | `GUILD_APPLICATION_NOT_FOUND` / `GuildApplicationNotFound` | 신청 없음·만료 |
| 1640 | `GUILD_INVITE_NOT_FOUND` / `GuildInviteNotFound` | 초대 없음·만료 |
| 1641 | `GUILD_APPLICATION_LIMIT` / `GuildApplicationLimit` | 내 대기 신청 5건 |
| 1642 | `GUILD_PENDING_FULL` / `GuildPendingFull` | 길드 대기 신청+초대 50건 |
| 1643 | `CHARACTER_IS_GUILD_LEADER` / `CharacterIsGuildLeader` | 길드원이 있는 길드장은 캐릭터 삭제 불가 |
| 1644 | `GUILD_TARGET_INVITES_FULL` / `GuildTargetInvitesFull` | 상대가 받은 대기 길드 초대 5건 |
| 1645 | `GUILD_LEADER_CHANGED` / `GuildLeaderChanged` | 요청 뒤 길드장이 바뀌어 위임 기대 버전이 맞지 않는다 |

- 코딩 규칙: Allman, 배너 주석, 매직넘버 금지(상한은 `SocialLimits`·`Constants.Social`), 서버 전용 코드는 `98.Servers/`, `NetworkBehaviour`를 새로 만들지 않는다(모두 `NetworkMessage` + 처리기). Mirror 정적 연결 이벤트에 구독하지 않고 `ShinshiNetworkManager.ServerDisconnected`를 쓴다.
- 핫 경로: 전달함 꺼내기 응답 처리와 채팅 방송은 매 프레임 경로가 아니지만, 캐릭터 ID → 연결 찾기는 `Dictionary<string, int>`를 인증·해제 때 갱신해 연결 목록 전체 순회를 피한다.
- UI는 UGUI + TextMeshPro, `EquipmentGrowthStyle` 팔레트, `UiWindowDragHandle`, 한글 글꼴 `HudQuestKorean-Regular/Bold`. 빌더를 다시 돌리면 수동 프리팹 수정은 덮어쓴다.

## 7. 소유와 경계

| 대상 | 소유 | 근거 |
|---|---|---|
| `01.Network/`, `98.Servers/`, `00.Common/`, `04.Systems/Hotkey/` | 팀장 | `.github/CODEOWNERS`(`@aurenixs`) |
| `Backend/` | 미확인(CODEOWNERS에 `/Backend/` 규칙 없음). 요청자가 팀장이라 진행 | Codex 2차 6 |
| `06.UI/` 스크립트(Friends, Guild, Hud 채팅, 상호작용 패널, Binding) | UI 담당 | `.github/CODEOWNERS` 46행 부근(`/Assets/01.Scripts/06.UI/` `@kjw1237`). CODEOWNERS는 스크립트 경로만 정한다 |
| `Assets/00.Scenes/Release/ShinshiTown.unity` 등 Release 실행 흐름 씬의 창·HUD 연결 | 팀장(통합 담당) | [씬·에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md) 30행 부근 씬 소유 표 |
| 새 창 프리팹 `PF_FriendWindow`·`PF_GuildWindow`(빌더 산출물) | UI 스크립트의 빌더가 만드는 산출물이라 UI 담당과 공유 | 구현 PR에 함께 표시 |

요청자가 팀장이므로 팀장 소유 영역(네트워크·서버·Release 씬 연결)과 소유 규칙이 없는 백엔드은 승인 질문 없이 진행한다(Codex 9 정정). `06.UI/` 스크립트 변경은 UI 담당에게 PR 리뷰로 알린다. 씬·프리팹 수정은 [씬·에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md)을 따른다.

## 8. 단축키

- 현재 상태(작업 트리, Codex 13 정정): `EHotkeyAction`에 `SkillWindow = 11`이 이미 있고, `HotkeyConfig.asset`에 `SkillWindow`=K(107), `CombatMeter`=L(108)이 이미 바인딩되어 있다. 이 작업은 그 끝에 `Friends = 12`, `Guild = 13`만 붙인다(값은 에셋에 번호로 저장되므로 끝에만 붙인다).
- 사용 중인 키: I(인벤토리), Y(경매장), Esc, P(출정 준비), Enter(채팅), L(딜미터), M(우편), U(캐릭터 정보), O(장비 옵션), K(스킬창). 전투·조작 키: Space, Q/W/E/R, Z/X/C, 1/2, F, Alt, F3, 마우스 좌우.
- **결정: 친구 `N`, 길드 `G`**(11절). `HotkeyConfigSO` 검증(중복 키 거절)과 `Constants.Player` 키 목록 비교 시험을 함께 돌린다.
- 채팅 입력 중에는 단축키가 동작하지 않는 기존 규칙을 그대로 쓴다.

## 9. 검증 계획

| 단계 | 내용 |
|---|---|
| 백엔드 실제 PostgreSQL(`[PostgresFact]`, Docker PostgreSQL 18) | 마이그레이션 적용·검증 안 된 제약 0건·인덱스·down 뒤 재적용. 친구: 요청·반대 요청 즉시 성립·수락·거절·취소·삭제·상한 50/20/50·만료 무시. 길드: 생성·이름 중복(대소문자·전각)·해체 후 이름 재사용·신청/초대 교차 즉시 가입·정원 30·탈퇴 규칙·추방·위임·`member_count` 불변식. **동시성**: `Task.WhenAll`로 같은 신청자 두 길드 동시 수락, 정원 29 동시 수락 2건, 위임 2건, 해체와 수락, 같은 이름 동시 생성, 친구 49 동시 수락, 같은 대상 받은 길드 초대 4건에서 동시 초대 2건 — 결과 하나만 성공하고 불변식 유지. 탈퇴·추방 재전송: 탈퇴 후 다른 길드에 가입한 뒤 옛 `membershipId` 탈퇴 요청이 새 가입을 지우지 않음, 추방 후 재가입한 길드원에게 옛 추방 요청이 효과 없음. 접속 표시: 임대 `character_id`(TownJoin 값, TryAcquire NULL), 만료·heartbeat 끊김·이관 후 위치. 전달함: 같은 인스턴스 대상도 행 생성과 `localDeliveries` 반환, 꺼내기가 이 인스턴스 임대 캐릭터만 반환, 확인 삭제가 남의 인스턴스 행을 못 지움, 확인하지 않은 로컬 행을 대상 이동 후 새 인스턴스가 꺼냄, 만료 정리. 캐릭터 삭제: 관계 정리·혼자 길드장 해체·길드원 있는 길드장 거절, **서로 길드원·친구인 두 캐릭터 동시 삭제가 교착 없이 끝남**. 위임: A→B 성공 뒤 B→A, 늦은 A→B 재전송이 `GUILD_LEADER_CHANGED`, 같은 요청의 즉시 재전송은 성공. 전달함 확인: 수신 캐릭터가 다르거나 임대가 다른 인스턴스인 확인은 행을 지우지 못함, 로컬 응답이 앞선 남은 행을 오름차순·저장 내용 그대로 포함, 꺼낸 행의 `next_attempt_at`이 5초 밀려 5초 안 재꺼내기에 안 나옴, 한 수신자 미확인 행 300개가 있어도 다른 수신자 행이 같은 꺼내기에 나옴(수신자별 20개) |
| 백엔드 HTTP | 서버 임대 없음 401, 플레이어·어드민 토큰 401, 던전 역할로 관리 경로 403, 경로 캐릭터 임대가 다른 인스턴스면 409 `SOCIAL_NOT_CONNECTED`, 형식 오류 400, 기능 차단, 429, 경로 상수와 템플릿 일치, 거절 코드 Unity·백엔드 번호 일치 |
| EditMode(Unity) | `SocialMessageSerializer` 왕복과 배열 상한 초과 읽기 실패, `GuildNameRules`(정규화·길이·문자), `ChatRules.IsSendable`·`IsDisplayable` 새 채널, **`ChatLog.AddMessage`가 받은 `Whisper`·`WhisperSent`·`Guild` 줄을 넣고 같은 `DeliveryId` 두 번째 줄은 버림**, 명령어 파싱(`/w`, `/귓`, `/r`, 공백·빈 본문), `SocialRelayServer`(가짜 게이트웨이: 신원 없는 연결 거절, 던전 역할 관리 요청 `WrongLocation`, 속도 제한, 동시 1건, 같은 `RequestId` 재전송 무시, 결과 불명 시 스냅샷 재조회, 재조회도 실패하면 조회 실패 결과), `SocialChatRelay`(오프라인·이름 없음·본인·로컬 전달과 확인 목록·대상 연결 없으면 확인 안 함·보낸 확인 줄·**같은 발신자 두 줄이 느린 게이트웨이에서도 순서 유지**·대기열 초과 `RateLimited`), `SocialDeliveryPump`(주기, 접속자 없으면 쉼, 차단기 열림), **수신 확인**(클라이언트 확인 전에는 API 확인 목록에 넣지 않음, 남의 연결로 보낸 ID 확인은 버림, 확인 메시지 속도·길이 상한, 연결 종료 시 대기 표 폐기, 재전송은 서버 메모리 없이 꺼내기 응답대로 보냄), **로컬 순서**(남은 m1이 있는 수신자에게 m2를 로컬 전달하면 m1, m2 순으로, 각 행의 저장 내용으로 보냄), 알림 `DeliveryId` 중복 검사와 확인, 클라이언트 표시(작은 `DeliveryId`가 늦게 와도 맨 아래에 붙임, 서버 이동 중 중복 기억 유지), 위임 `leader_version` 불일치 거절, `SocialClient` 연결 종료 시 초기화, 뷰 규칙(정렬, 파티 초대 버튼 조건, 길드장 전용 버튼, 길드 없음 모드), 단축키 중복 없음 |
| 로컬 스택(L2) | 테스트 계정 2개로 마을 친구 요청·수락·삭제, 길드 생성·초대·신청·위임·추방·해체, 귓속말 왕복, 한쪽 출정 후 마을↔던전 귓속말·길드 채팅, 오프라인 귓속말 실패, 친구 창에서 파티 초대 |
| 다인(M2) | 사람 2~4명 실제 접속. 수용 기준(5.6절 표): 오프라인 판정은 마을 정상 종료 즉시, 마을 비정상 종료 최대 15초, 던전 연결 종료는 출정 종료·임대 반환까지 "출정 중" 유지가 정상. 화면 반영은 창을 새로 열면 왕복 안, 창을 열어 둔 채면 판정 뒤 최대 30초. 다른 서버 사이 채팅 전달 지연 약 1초+왕복, 수신 확인 없이 끊긴 줄이 재접속 뒤 한 번만 보임. 실행 전까지 `구현됨·실행 미검증` |
| 문서 | 구현 후 이 문서 상태 줄, 6절 수정 표의 문서들. 실행 안 한 항목은 미검증으로 둔다 |

## 10. 구현 순서

1. **백엔드**: `0019_social` 마이그레이션과 시험 → `Contracts/Social` 계약·코드·`SocialLimits`·`GuildNameRules` → 저장소(친구 → 길드 → 전달함 → 스냅샷) → 컨트롤러·제한기·정리 서비스 → 임대 `character_id`·캐릭터 삭제 연동 → PostgreSQL·HTTP 시험 통과.
2. **서버(Unity)**: 메시지·직렬화·열거형 → `SocialRelayServer`(마을 관리 중계) → `SocialChatRelay`(채팅 서버 연결) → `SocialDeliveryPump`(두 역할) → 부트스트랩 설치 → EditMode 시험. `PROTOCOL_VERSION` 올림.
3. **클라이언트 창구**: `SocialClient`, `ChatClient.SendWhisper`, 채팅 채널·거절 문구.
4. **UI**: 뷰 규칙과 시험 → 친구·길드 창 빌더·프리팹·미리보기 바인더 → 바인더 → HUD 채팅 탭·명령어 → 상호작용 패널 → 단축키·HUD 메뉴·배지 → 마을 씬 연결.
5. 로컬 스택 검증 → 문서 갱신.

## 11. 사용자 결정(2026-10-16 결정됨)

사용자가 초안 권장값을 모두 확정했다.

| 번호 | 항목 | 결정 |
|---|---|---|
| 1 | 상한 수치 | 친구 50, 보낸 요청 20, 받은 요청 50, 길드 정원 30, 캐릭터당 가입 신청 5, 길드 대기 50, 요청·초대 기한 7일(3.1절) |
| 2 | 길드 이름 | 2~8자 완성형 한글·영문·숫자, 해체 즉시 재사용, 금칙어 1차 없음 |
| 3 | 길드 생성 비용 | 무료(경제 작업 아님, operationId·원장 없음) |
| 4 | 탈퇴·추방 뒤 재가입 대기 | 없음 |
| 5 | 서버 간 전달 | 백엔드 전달함 + 1초 꺼내기(5.5절 B) |
| 6 | 접속·종료 실시간 알림 | 없음(창 열 때, 30초마다, 소셜 알림 때 갱신) |
| 7 | 던전에서 친구·길드 창 조작 | 불가. 던전은 채팅·알림만 |
| 8 | 길드원이 있는 길드장의 캐릭터 삭제 | 거절(`CHARACTER_IS_GUILD_LEADER`), 먼저 위임 |
| 9 | `social` 기능 차단 | 관리 요청과 함께 귓속말·길드 채팅도 차단 |
| 10 | 단축키 | 친구 N(`Friends = 12`), 길드 G(`Guild = 13`) |
| 11 | 귓속말 명령어 | `/w`, `/귓`, `/r` |

## 12. 검증 이력

| 날짜 | 검증 | 결과 |
|---|---|---|
| 2026-10-16 | Codex `gpt-6.1-sol` ultra 1차 읽기 전용 정적 검증 | REJECT, 13건(높음 1, 중간 8, 낮음 4) 전부 수용 반영. 1 수신 표시 규칙 분리(5.2·6·9절), 2 받은 길드 초대 상한(3.1·5.2·5.4·6절), 3 삭제 잠금 `FOR NO KEY UPDATE`와 FK 검사 포함 잠금 순서(5.7·5.11절), 4 탈퇴·추방·위임에 기대 길드·`membershipId` 결합(3.2·3.3·5.2·5.3·5.4절), 5 로컬 대상도 전달함 기록 후 확인(5.3·5.5절), 6 `ChatBroadcastMessage.DeliveryId`와 클라이언트 중복 제거(5.2·5.5·6절), 7 발신자별 직렬 처리(5.5절), 8 `ShinshiNetworkManager.OnStartClient` 등록과 연결별 초기화(6절), 9 소유자 구분 정정(7절), 10 조회 실패 경로와 실제 조회 뒤 안내(5.10절), 11 귓속말 거절 안내에서 대상 이름 제외(5.10절), 12 판정 지연과 화면 반영 지연 분리(5.6·9절), 13 단축키 현재 상태 정정(8절). Codex가 확인한 사실(요청 시간 상한 30초, 임대 INSERT 두 곳, 트랜잭션 방식, HUD 메뉴 연결 위치)로 미확인 표시를 해소. 재검증은 하지 않음 |
| 2026-10-16 | Codex 2차 재검증 | REJECT, 1차 13건 중 8건 해소·5건 부분 해소, 신규 6건(중간 4, 낮음 2) 전부 수용 반영. 1 클라이언트 수신 확인 메시지와 확인받은 행만 삭제(5.2·5.5절), 2 로컬 전달이 앞선 대기 행을 함께 오름차순 전달·클라이언트 도착순 표시(5.5절), 3 `guilds.leader_version`과 위임 기대 버전(3.2·3.3·5.2·5.3·5.4·6절), 4 던전 연결 종료는 출정 종료까지 출정 중·수용 기준 분리(5.6·9절), 5 중복 제거 기억 수명 정정(5.5·6절), 6 백엔드 소유 미확인 표시(7절). 추가 확인: `TryAcquireAsync` 운영 호출자 없음, 30초는 전송 시도별 상한(5.5·5.6절). 재검증은 하지 않음 |
| 2026-10-16 | Codex 3차 재검증 | REJECT(높음 0, 중간 3, 낮음 1), 전부 수용 반영. 1 `SocialDeliveryRow` 공통 행 계약과 저장 내용으로 전달(5.3·5.5·6절), 2 `SocialNoticeMessage.DeliveryId`와 알림 확인, 공통 중복 기억 `DeliveryDedupe` 공유(5.2·5.5·6절), 3 `next_attempt_at`·수신자별 20개·전체 200개·DB 재전송 간격(3.2·5.5·9절), 4 동시 위임 후행 결과 `GUILD_LEADER_CHANGED`(5.7절). 재검증 횟수 상한(2회) 도달로 추가 재검증 없음 |
| 2026-10-16 | 구현 뒤 자동 검증 | Unity EditMode 6,427건 중 6,426건 통과(1건은 기존 제외 항목), 백엔드 실제 PostgreSQL(Docker PostgreSQL 18) 1,607건 통과. 게임 실행, 로컬 스택 기동, 다인 접속은 하지 않았다(9절 L2·M2 미실행). |

## 13. 구현 메모

설계(1~12절)와 달라졌거나 구현에서 정한 것만 적는다. 위 표의 이름·경로와 다르면 이 절이 맞다.

- 창 프리팹 이름은 `PF_FriendWindow`(설계의 `PF_FriendsWindow` 아님)와 `PF_GuildWindow`다. 화면 코드는 `06.UI/Social/Friends/`와 `06.UI/Social/Guild/`, 공용 행 `06.UI/Social/SocialRowView.cs`, 프리팹 빌더는 `06.UI/Social/Editor/`다.
- 마을·던전의 소셜 요청(`SocialRequestMessage`)은 `TownServerRuntime`이 아니라 `98.Servers/01.Network/Social/SocialServerHost`가 받는다. 부트스트랩이 두 역할 모두에 `SocialServerInstaller.Install`로 설치하며 역할별 허용은 중계(`SocialRelayServer`)가 판정한다.
- 설계의 `ICharacterConnectionLookup`·`MirrorCharacterConnectionLookup` 자리는 `ISocialAudience`(`MirrorSocialAudience`가 구현)가 맡는다. 서버 쪽 전달 확인 기록은 `SocialDeliveryLedger`다. 클라이언트의 중복 제거는 `01.Network/Social/DeliveryDedupe`다.
- `social_deliveries`는 설계의 `payload jsonb` 대신 명시 열이다: `payload text`(100자 이하), `channel smallint NOT NULL`(0~100), `sender_name text NOT NULL`(64자 이하), `sender_character_id uuid NULL`(FK 없음). 3.2절 DDL과 다르다. 배포 DB의 IDENTITY 시퀀스 권한은 마이그레이션 주석대로 미검증이다.
- 기능 차단 키 `social`이 꺼져도 막지 않는 요청은 스냅샷, 전달함 꺼내기, 탈퇴, 추방뿐이다. 친구·길드 관리 쓰기, 귓속말, 길드 채팅은 409 `FEATURE_DISABLED`다.
- 길드장 위임이 동시에 두 건 들어오면 나중 요청은 기대 `leader_version`이 이미 올라가 있어 `GUILD_LEADER_CHANGED`(1645)로 거절된다(5.7절 그대로, 백엔드 저장소 구현). 이 경로를 가리키는 전용 시험이 있는지는 확인하지 않았다.
- `PROTOCOL_VERSION`은 10이고 단축키는 친구 N(`Friends = 12`), 길드 G(`Guild = 13`)다. 스킬창 K와 딜미터 L은 [스킬창 설계](skill-window.md)에서 정했다.
- 실행하지 않은 것: 마이그레이션을 운영 DB에 적용, 로컬 스택(API·마을·던전)에서 친구·길드·귓속말 흐름, 마을↔던전 전달, 5.6절 접속 표시 수용 기준, 사람 2~4명 접속(M2). 모두 `구현됨·실행 미검증`이다.
