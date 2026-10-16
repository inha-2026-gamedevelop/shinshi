-- 소셜(친구, 귓속말, 길드, 길드 채팅). 경제 효과가 없어 operations, 원장, outbox를 쓰지 않고 상태 기준 멱등으로 재시도를 받는다.
-- 잠금 순서는 계정(캐릭터 삭제만) -> 캐릭터 행(FOR NO KEY UPDATE, character_id 오름차순) -> 길드 행(FOR UPDATE)이다.
-- 상한 숫자는 Shinshi.Contracts.SocialLimits, 길드 이름 규칙은 GuildNameRules와 같고 시험(SocialContractParityTests)이 묶는다

-- 접속 표시: 계정 임대가 어느 캐릭터의 접속인지. 마을 입장 교환이 채우고 던전 이관(UPDATE)은 그대로 둔다.
-- 0019 전 행과 캐릭터 없이 얻은 임대(TryAcquire, 개발 경로)는 NULL이며 접속 표시에서 오프라인이다
ALTER TABLE account_leases
    ADD COLUMN character_id uuid NULL REFERENCES characters (character_id);
-- 계정당 임대 1행이고 캐릭터는 한 계정 소유라 캐릭터당 최대 1행이다. 꺼내기와 접속 판정이 이 인덱스로 캐릭터 임대를 찾는다
CREATE UNIQUE INDEX ux_account_leases_character ON account_leases (character_id) WHERE character_id IS NOT NULL;
-- 꺼내기는 호출 인스턴스에 임대가 있는 캐릭터를 찾는다
CREATE INDEX ix_account_leases_instance_character ON account_leases (instance_id, character_id)
    WHERE character_id IS NOT NULL;

-- 친구 관계는 방향마다 한 행(A->B, B->A). 두 행은 항상 같은 트랜잭션에서 넣고 지운다
CREATE TABLE friendships
(
    character_id uuid NOT NULL REFERENCES characters (character_id),
    friend_character_id uuid NOT NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (character_id, friend_character_id),
    CONSTRAINT ck_friendships_not_self CHECK (character_id <> friend_character_id)
);
CREATE INDEX ix_friendships_friend ON friendships (friend_character_id, character_id);

CREATE TABLE friend_requests
(
    requester_character_id uuid NOT NULL REFERENCES characters (character_id),
    target_character_id uuid NOT NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (requester_character_id, target_character_id),
    CONSTRAINT ck_friend_requests_not_self CHECK (requester_character_id <> target_character_id),
    CONSTRAINT ck_friend_requests_expiry CHECK (expires_at > created_at)
);
CREATE INDEX ix_friend_requests_target ON friend_requests (target_character_id, created_at);
CREATE INDEX ix_friend_requests_expiry ON friend_requests (expires_at);

CREATE TABLE guilds
(
    guild_id uuid PRIMARY KEY,
    name text NOT NULL CONSTRAINT ck_guilds_name CHECK (name ~ '^[가-힣A-Za-z0-9]{2,8}$'),
    name_key text NOT NULL CONSTRAINT ck_guilds_name_key CHECK (name_key = lower(name)),
    leader_character_id uuid NULL REFERENCES characters (character_id),
    member_count integer NOT NULL CONSTRAINT ck_guilds_member_count CHECK (member_count BETWEEN 0 AND 30),
    -- 생성 재전송 확인용. 같은 생성자가 같은 값으로 다시 보내면 처음 만든 길드를 돌려준다
    creator_character_id uuid NOT NULL REFERENCES characters (character_id),
    create_request_id uuid NOT NULL,
    revision bigint NOT NULL DEFAULT 0 CONSTRAINT ck_guilds_revision CHECK (revision >= 0),
    -- 위임마다 +1. 위임 요청이 기대 값을 함께 보내 늦게 도착한 재전송이 그 뒤의 위임을 되돌리지 못하게 한다
    leader_version bigint NOT NULL DEFAULT 0 CONSTRAINT ck_guilds_leader_version CHECK (leader_version >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    disbanded_at timestamptz NULL,
    CONSTRAINT ux_guilds_create_request UNIQUE (creator_character_id, create_request_id),
    -- 살아 있는 길드는 길드장과 1명 이상, 해체한 길드는 길드장 없음과 0명
    CONSTRAINT ck_guilds_alive_shape CHECK (
        (disbanded_at IS NULL AND leader_character_id IS NOT NULL AND member_count >= 1)
        OR (disbanded_at IS NOT NULL AND leader_character_id IS NULL AND member_count = 0))
);
-- 이름은 살아 있는 길드 사이에서만 유일하다. 해체한 길드 이름은 곧바로 다시 쓸 수 있다
CREATE UNIQUE INDEX ux_guilds_name_key_alive ON guilds (name_key) WHERE disbanded_at IS NULL;

-- 캐릭터는 길드 하나에만 든다(PK). role 0 길드원, 1 길드장.
-- membership_id는 가입할 때마다 새로 만든다. 탈퇴, 추방, 위임 요청이 이 값을 함께 보내 재전송이 그 뒤의 새 가입을 건드리지 못하게 한다
CREATE TABLE guild_members
(
    character_id uuid PRIMARY KEY REFERENCES characters (character_id),
    guild_id uuid NOT NULL REFERENCES guilds (guild_id),
    membership_id uuid NOT NULL CONSTRAINT ux_guild_members_membership UNIQUE,
    role smallint NOT NULL CONSTRAINT ck_guild_members_role CHECK (role IN (0, 1)),
    joined_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ix_guild_members_guild ON guild_members (guild_id, role, joined_at);
CREATE UNIQUE INDEX ux_guild_members_leader ON guild_members (guild_id) WHERE role = 1;

-- 가입 신청(kind 0, 캐릭터가 보냄, 길드장이 수락)과 초대(kind 1, 길드장이 보냄, 캐릭터가 수락)를 한 표에 둔다.
-- (길드, 캐릭터)당 한 행이라 반대 종류가 이미 있으면 새 요청은 곧바로 가입으로 처리한다
CREATE TABLE guild_join_requests
(
    guild_id uuid NOT NULL REFERENCES guilds (guild_id),
    character_id uuid NOT NULL REFERENCES characters (character_id),
    kind smallint NOT NULL CONSTRAINT ck_guild_join_requests_kind CHECK (kind IN (0, 1)),
    inviter_character_id uuid NULL REFERENCES characters (character_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (guild_id, character_id),
    CONSTRAINT ck_guild_join_requests_inviter CHECK ((kind = 1) = (inviter_character_id IS NOT NULL)),
    CONSTRAINT ck_guild_join_requests_expiry CHECK (expires_at > created_at)
);
CREATE INDEX ix_guild_join_requests_character ON guild_join_requests (character_id, kind, created_at);
CREATE INDEX ix_guild_join_requests_guild ON guild_join_requests (guild_id, kind, created_at);
CREATE INDEX ix_guild_join_requests_expiry ON guild_join_requests (expires_at);

-- 서버 프로세스 사이 전달함. 대상 캐릭터로 주소를 매기고, 그 캐릭터의 임대가 있는 서버가 꺼내 보낸 뒤
-- 클라이언트 수신 확인(ACK)을 받은 행만 지운다. 귓속말, 길드 채팅, 소셜 알림 모두 같다.
-- channel은 kind 0이면 귓속말(4), kind 1이면 길드(3), kind 2이면 알림 종류(ESocialNotice)다.
-- sender는 채팅이면 보낸 캐릭터, 알림이면 행위 캐릭터다. 삭제 연동 잠금을 늘리지 않게 sender에는 FK를 두지 않는다.
-- payload는 채팅이면 본문(1~100자), 알림이면 길드 이름(없으면 빈 문자열)이다
CREATE TABLE social_deliveries
(
    delivery_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    target_character_id uuid NOT NULL REFERENCES characters (character_id),
    kind smallint NOT NULL CONSTRAINT ck_social_deliveries_kind CHECK (kind IN (0, 1, 2)),
    channel smallint NOT NULL CONSTRAINT ck_social_deliveries_channel CHECK (channel BETWEEN 0 AND 100),
    sender_character_id uuid NULL,
    sender_name text NOT NULL CONSTRAINT ck_social_deliveries_sender_name CHECK (char_length(sender_name) <= 64),
    payload text NOT NULL CONSTRAINT ck_social_deliveries_payload CHECK (char_length(payload) <= 100),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    -- 다음에 꺼낼 수 있는 시각. 꺼내거나 로컬로 보낸 행은 재전송 간격(5초)만큼 미뤄 같은 행이 매 주기 다시 나가지 않게 한다
    next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_social_deliveries_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_social_deliveries_chat_text CHECK ((kind = 2) OR (char_length(payload) >= 1))
);
-- 꺼내기(수신자별 next_attempt_at <= now, delivery_id 순)와 로컬 앞선 행 조회(수신자별 delivery_id 순)
CREATE INDEX ix_social_deliveries_target_due ON social_deliveries (target_character_id, next_attempt_at, delivery_id);
CREATE INDEX ix_social_deliveries_target ON social_deliveries (target_character_id, delivery_id);
CREATE INDEX ix_social_deliveries_expiry ON social_deliveries (expires_at);

-- 배포 권한(README의 런타임 역할 예시에 추가): friendships, friend_requests, guild_members, guild_join_requests,
-- social_deliveries는 SELECT, INSERT, UPDATE, DELETE, guilds는 SELECT, INSERT, UPDATE(해체는 행을 남긴다).
-- social_deliveries의 IDENTITY 시퀀스 권한(런타임 역할의 INSERT에 별도 USAGE가 필요한지)은 배포 DB에서 확인한다(미검증).
-- account_leases의 새 열은 기존 테이블 권한을 따른다
