-- 공개 가입과 아이디, 비밀번호 로그인(docs/design/account-and-website.md 2장, 5장).
-- 초대 코드로 만든 기존 계정은 자격 행이 없고 POST /v1/accounts/credentials로 나중에 만든다
CREATE TABLE account_credentials
(
    account_id uuid PRIMARY KEY REFERENCES accounts (account_id),
    -- API가 소문자로 바꿔 넣으므로 대소문자만 다른 아이디는 이 유일 제약에서 충돌한다
    login_id text NOT NULL UNIQUE CHECK (login_id ~ '^[a-z0-9_]{4,16}$'),
    -- pbkdf2-sha256$<회수>$<솔트 base64>$<해시 base64>. 회수를 행마다 남겨 나중에 올릴 수 있다
    password_hash text NOT NULL
        CHECK (password_hash ~ '^pbkdf2-sha256\$[1-9][0-9]*\$[A-Za-z0-9+/]+=*\$[A-Za-z0-9+/]+=*$'),
    email text NOT NULL CHECK (length(email) BETWEEN 3 AND 254),
    email_key text NOT NULL CHECK (length(email_key) BETWEEN 3 AND 254),
    email_verified boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CHECK (updated_at >= created_at)
);
-- 미인증 이메일은 중복을 허용한다(남의 이메일 선점 방지). 인증된 이메일만 하나의 계정이 갖는다
CREATE UNIQUE INDEX ux_account_credentials_verified_email ON account_credentials (email_key) WHERE email_verified;

-- 로그인 묶음의 시작 수단. 현재 비밀번호 없이 자격을 바꾸는 복구는 초대(복구 코드)로 시작한 묶음에서 30분 안에만 허용한다.
-- 0010 전 묶음은 초대 교환으로만 만들어졌으므로 'invite'로 채운다. 그 뒤에는 기본값을 지워 새 묶음이 시작 수단을 반드시 적게 한다
-- (빠뜨린 쓰기가 복구 권한을 얻지 않고 NOT NULL로 실패하게). 갱신 회전은 같은 family_id를 이어 쓰므로 시작 수단과 created_at이 이어진다
ALTER TABLE auth_token_families
    ADD COLUMN origin text NOT NULL DEFAULT 'invite',
    ADD CONSTRAINT ck_auth_token_families_origin CHECK (origin IN ('invite', 'password'));
ALTER TABLE auth_token_families ALTER COLUMN origin DROP DEFAULT;

-- 캐릭터 이름(닉네임). 기존 행에는 채울 이름이 없어 NULL로 둔다. 생성 API는 항상 채우며 NULL끼리는 유일 제약에서 충돌하지 않는다.
-- name은 NFKC 정규화한 표시 이름, name_key는 그 문화권 독립 소문자이고 전체 캐릭터에서 유일하다
ALTER TABLE characters
    ADD COLUMN name text NULL,
    ADD COLUMN name_key text NULL,
    ADD COLUMN appearance jsonb NULL,
    ADD CONSTRAINT ck_characters_name_pair CHECK ((name IS NULL) = (name_key IS NULL)),
    ADD CONSTRAINT ck_characters_name_format CHECK (name IS NULL OR name ~ '^[가-힣A-Za-z0-9]{2,12}$'),
    ADD CONSTRAINT ck_characters_name_key CHECK (name_key IS NULL OR name_key = lower(name)),
    ADD CONSTRAINT ck_characters_appearance CHECK (appearance IS NULL OR jsonb_typeof(appearance) = 'object');
CREATE UNIQUE INDEX ux_characters_name_key ON characters (name_key);
