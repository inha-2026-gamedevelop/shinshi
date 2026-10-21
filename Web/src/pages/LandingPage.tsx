import { useAuth } from '../auth/AuthContext';
import { Button, LinkButton, buttonClass } from '../components/ui';

const DOWNLOAD_URL = (import.meta.env.VITE_LAUNCHER_DOWNLOAD_URL ?? '').trim();
const HAS_DOWNLOAD = DOWNLOAD_URL.startsWith('/downloads/') || DOWNLOAD_URL.startsWith('https://');

const FEATURES = [
  { title: '하나의 계정', body: '홈페이지에서 만든 아이디로 런처에 바로 로그인합니다.' },
  { title: '나만의 이름', body: '캐릭터 이름이 곧 닉네임입니다. 게임 안에서 정하고, 겹치지 않습니다.' },
  { title: '런처로 시작', body: HAS_DOWNLOAD ? '런처에서 게임을 설치하고 홈페이지 계정으로 접속합니다.' : '런처가 업데이트와 접속을 맡습니다. 내려받기는 준비 중입니다.' },
];

export function LandingPage() {
  const { isAuthenticated } = useAuth();

  return (
    <>
      <section className="relative isolate overflow-hidden border-b border-divider/80">
        {/* TODO(아트): 키 아트 이미지가 나오면 이 그라데이션 자리를 교체한다 */}
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10"
          style={{
            background:
              'radial-gradient(60% 70% at 78% 30%, rgba(255,106,19,0.30), transparent 70%), radial-gradient(50% 60% at 15% 85%, rgba(255,130,54,0.12), transparent 70%), linear-gradient(180deg, #12151b 0%, #0e1014 100%)',
          }}
        />
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10 opacity-[0.07]"
          style={{
            backgroundImage:
              'linear-gradient(#edeff3 1px, transparent 1px), linear-gradient(90deg, #edeff3 1px, transparent 1px)',
            backgroundSize: '48px 48px',
            maskImage: 'linear-gradient(180deg, black, transparent 85%)',
          }}
        />
        <div className="mx-auto flex min-h-[68vh] max-w-6xl flex-col justify-center px-4 py-16 sm:px-6 sm:py-24">
          <p className="text-sm font-semibold tracking-[0.3em] text-accent">SHINSHI · 신시</p>
          <h1 className="mt-4 max-w-2xl text-4xl leading-tight font-extrabold sm:text-6xl">
            신시의 문이
            <br />
            열립니다
          </h1>
          <p className="mt-5 max-w-xl text-base text-muted sm:text-lg">
            <span className="mr-2 rounded border border-warn/50 px-1.5 py-0.5 align-middle text-[11px] font-semibold text-warn">
              문구 임시
            </span>
            게임 소개 문구 자리입니다. 확정된 태그라인으로 바꿔 주세요.
          </p>
          <div className="mt-9 flex flex-col gap-3 sm:flex-row">
            {isAuthenticated ? (
              <LinkButton to="/me" className="sm:min-w-40">
                내 정보
              </LinkButton>
            ) : (
              <>
                <LinkButton to="/signup" className="sm:min-w-40">
                  회원가입
                </LinkButton>
                <LinkButton to="/login" variant="secondary" className="sm:min-w-40">
                  로그인
                </LinkButton>
              </>
            )}
            {HAS_DOWNLOAD ? (
              <a href={DOWNLOAD_URL} download className={buttonClass('secondary', 'sm:min-w-40')} aria-describedby="launcher-status">
                런처 내려받기
              </a>
            ) : <Button variant="secondary" disabled aria-describedby="launcher-status" className="sm:min-w-40">
              <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                <path d="M12 4v11m0 0-4-4m4 4 4-4M5 19h14" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
              런처 내려받기
            </Button>}
          </div>
          <p id="launcher-status" className="mt-3 text-[13px] text-muted">
            {HAS_DOWNLOAD ? 'Windows 64비트 · 설치 EXE 실행 → 바탕화면의 신시 런처 실행 → 게임 설치 → 로그인' : '런처 내려받기는 준비 중입니다.'}
          </p>
        </div>
      </section>

      <section aria-labelledby="features-title" className="mx-auto max-w-6xl px-4 py-14 sm:px-6">
        <h2 id="features-title" className="sr-only">
          계정 안내
        </h2>
        <ul className="grid gap-4 sm:grid-cols-3">
          {FEATURES.map((f, i) => (
            <li key={f.title} className="rounded-2xl border border-divider bg-panel p-6">
              <span className="text-sm font-bold text-accent tabular-nums">0{i + 1}</span>
              <h3 className="mt-2 text-lg font-semibold">{f.title}</h3>
              <p className="mt-1.5 text-[15px] text-muted">{f.body}</p>
            </li>
          ))}
        </ul>
      </section>
    </>
  );
}
