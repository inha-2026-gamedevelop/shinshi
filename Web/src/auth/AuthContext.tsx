import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useApi } from '../api/ApiContext';
import type { AuthExchangeResponse } from '../api/types';
import { normalizeLoginId } from '../lib/validation';

// 접근 토큰은 이 상태(메모리)에만 둔다. localStorage, sessionStorage, 쿠키에 쓰지 않는다
// 갱신 토큰은 받아도 버린다. 그래서 새로고침하면 로그아웃된다
interface Session {
  accessToken: string;
  expiresAtLocalMs: number;
}

interface AuthValue {
  isAuthenticated: boolean;
  accessToken: string | null;
  login(loginId: string, password: string): Promise<void>;
  /** 복구 화면이 바로 이어서 쓰도록 접근 토큰을 돌려준다 */
  loginWithRecoveryCode(code: string): Promise<string>;
  logout(): void;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const api = useApi();
  const [session, setSession] = useState<Session | null>(null);

  const begin = useCallback((response: AuthExchangeResponse) => {
    // 만료는 서버 시각 기준이라 남은 시간만 로컬 시각에 더한다. 갱신 토큰은 버린다
    const remainingMs = response.accessTokenExpiresAtUnixMs - response.serverTimeUnixMs;
    setSession({ accessToken: response.accessToken, expiresAtLocalMs: Date.now() + remainingMs });
    return response.accessToken;
  }, []);

  // 웹은 build를 싣지 않는다. 서버는 build가 없으면 빌드 검사를 건너뛴다
  const login = useCallback(
    async (loginId: string, password: string) => {
      begin(await api.exchange({ loginId: normalizeLoginId(loginId), password }));
    },
    [api, begin],
  );

  const loginWithRecoveryCode = useCallback(
    async (code: string) => {
      return begin(await api.exchange({ inviteCode: code.trim() }));
    },
    [api, begin],
  );

  const logout = useCallback(() => setSession(null), []);

  // 접근 토큰이 만료되면 갱신하지 않고 로그아웃한다
  useEffect(() => {
    if (!session) return;
    const timer = setTimeout(() => setSession(null), Math.max(0, session.expiresAtLocalMs - Date.now()));
    return () => clearTimeout(timer);
  }, [session]);

  const value = useMemo<AuthValue>(
    () => ({
      isAuthenticated: session !== null,
      accessToken: session?.accessToken ?? null,
      login,
      loginWithRecoveryCode,
      logout,
    }),
    [session, login, loginWithRecoveryCode, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('AuthProvider가 없습니다.');
  return value;
}
