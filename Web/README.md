# SHINSHI 계정 홈페이지

회원가입, 로그인, 내 정보(비밀번호 변경, 캐릭터 이름 확인), 계정 복구 화면을 담은 정적 사이트다. Vite + React + Tailwind. 설계는 `docs/design/account-and-website.md`를 따른다.

## 실행

```powershell
cd Web
npm install
npm run dev          # http://localhost:5173, /v1 은 http://127.0.0.1:5080 (로컬 API)으로 프록시
```

API 없이 화면만 볼 때는 모의 모드를 쓴다.

```powershell
$env:VITE_API_MOCK = '1'; npm run dev
```

또는 `Web/.env.development.local`에 `VITE_API_MOCK=1`을 적는다(.gitignore 대상).

모의 모드는 브라우저 메모리의 가짜 서버로 동작하며 새로고침하면 처음 상태로 돌아간다.

| 항목 | 값 |
|---|---|
| 이미 쓰인 아이디 | `admin`, `shinshi`, `demo01` |
| 이미 쓰인 캐릭터 이름 | `월백`, `admin`, `shinshi` (대소문자, 전각 무시) |
| 데모 로그인(모의 전용) | `demo01` / `demo1234` |
| 복구 코드(모의 전용, 일회용) | `RECOVER-0001` (아이디, 이메일이 없는 계정) |

## 검사와 빌드

```powershell
npm run lint
npm test
npm run build        # 결과: Web/dist (정적 파일)
npm run preview      # 빌드 결과 확인
```

## NAS 배치

배포 환경의 `VITE_LAUNCHER_DOWNLOAD_URL`을 `/downloads/런처파일.zip` 또는 HTTPS 주소로 설정하면 홈페이지 런처 다운로드 버튼이 활성화된다. 값이 없으면 준비 중으로 표시한다. 설정 후 다시 빌드해야 한다.

1. `npm run build` 결과인 `dist/`를 NAS 웹 서버 폴더에 올린다.
2. 역방향 프록시 한 단으로 같은 도메인의 `/v1/*`를 API로 넘긴다. 같은 출처이므로 API에 CORS를 열지 않는다.
3. 그 밖의 경로는 모두 `index.html`로 돌려보낸다(SPA 경로 `/signup`, `/login`, `/me`, `/recover`).
4. 프록시 주소를 API의 신뢰 프록시(`KnownProxies`)에 넣어 `X-Forwarded-For`로 방문자 IP가 복원되게 한다. 배치 뒤 서로 다른 외부 IP의 요청이 따로 세어지는지 확인한다.
5. HTTPS로만 연다.

nginx 예:

```nginx
location /v1/ { proxy_pass http://127.0.0.1:5080; proxy_set_header X-Forwarded-For $remote_addr; proxy_set_header X-Forwarded-Proto $scheme; }
location /    { root /volume1/web/shinshi; try_files $uri /index.html; }
```

## 보안 메모

- 접근 토큰은 React 상태(메모리)에만 둔다. localStorage, sessionStorage, 쿠키에 쓰지 않으며 갱신 토큰은 받아도 버린다. 새로고침하면 다시 로그인한다.
- 웹 로그인은 `build` 없이 `{loginId, password}`만 보낸다.
- 요청 본문, 토큰을 콘솔에 남기지 않는다(ESLint `no-console`).
