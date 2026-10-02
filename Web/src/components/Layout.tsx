import type { ReactNode } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { isMockMode } from '../api';
import { useAuth } from '../auth/AuthContext';

export function Logo() {
  return (
    <Link to="/" className="group inline-flex min-h-11 items-center gap-2.5 rounded-lg" aria-label="SHINSHI 신시 홈">
      <svg viewBox="0 0 32 32" className="size-7" aria-hidden="true">
        <path d="M16 3 29 16 16 29 3 16Z" fill="none" stroke="var(--color-accent)" strokeWidth="2.5" />
        <path d="M16 10 22 16 16 22 10 16Z" fill="var(--color-accent)" />
      </svg>
      <span className="text-lg font-extrabold tracking-[0.18em]">SHINSHI</span>
      <span className="text-sm text-muted">신시</span>
    </Link>
  );
}

const navClass = ({ isActive }: { isActive: boolean }) =>
  `inline-flex min-h-11 items-center rounded-lg px-3 text-[15px] font-medium transition-colors duration-200 ${
    isActive ? 'text-fg' : 'text-muted hover:text-fg'
  }`;

export function Layout() {
  const { isAuthenticated, logout } = useAuth();

  return (
    <div className="flex min-h-dvh flex-col">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-lg focus:bg-accent focus:px-4 focus:py-2 focus:text-bg"
      >
        본문으로 건너뛰기
      </a>
      {isMockMode && (
        <div className="bg-warn/15 px-4 py-1.5 text-center text-[13px] text-warn">
          모의 모드: 실제 서버 대신 브라우저 메모리의 가짜 데이터로 동작합니다
        </div>
      )}
      <header className="border-b border-divider/80 bg-bg/90 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-2 px-4 py-2 sm:px-6">
          <Logo />
          <nav aria-label="계정" className="flex items-center gap-1">
            {isAuthenticated ? (
              <>
                <NavLink to="/me" className={navClass}>
                  내 정보
                </NavLink>
                <button type="button" onClick={logout} className={navClass({ isActive: false }) + ' cursor-pointer'}>
                  로그아웃
                </button>
              </>
            ) : (
              <>
                <NavLink to="/login" className={navClass}>
                  로그인
                </NavLink>
                <NavLink
                  to="/signup"
                  className="inline-flex min-h-11 items-center rounded-lg px-3 text-[15px] font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
                >
                  회원가입
                </NavLink>
              </>
            )}
          </nav>
        </div>
      </header>
      <main id="main" className="flex-1">
        <Outlet />
      </main>
      <footer className="border-t border-divider/80">
        <div className="mx-auto flex max-w-6xl flex-col gap-1 px-4 py-6 text-[13px] text-muted sm:flex-row sm:justify-between sm:px-6">
          <p>SHINSHI 신시 계정 페이지</p>
          <p>로그인 정보는 이 탭의 메모리에만 보관됩니다.</p>
        </div>
      </footer>
    </div>
  );
}

export function AuthPageShell({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <div className="mx-auto w-full max-w-md px-4 py-10 sm:py-16">
      <h1 className="text-2xl font-bold sm:text-[28px]">{title}</h1>
      {subtitle && <p className="mt-2 text-[15px] text-muted">{subtitle}</p>}
      <div className="mt-8">{children}</div>
    </div>
  );
}
