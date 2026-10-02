import { ApiError, RejectReason } from './errors';
import type { AccountMeResponse, ApiClient } from './types';
import {
  characterNameKey,
  normalizeCharacterName,
  normalizeLoginId,
  validateCharacterName,
  validateEmail,
  validateLoginId,
  validatePassword,
} from '../lib/validation';

// VITE_API_MOCK=1 일 때 쓰는 메모리 가짜 서버. 새로고침하면 처음 상태로 돌아간다
// 가짜 서버 전용 데모 계정, 복구 코드는 README의 모의 모드 절에 적어 둔다

interface MockAccount {
  me: AccountMeResponse;
  password: string | null;
  failedCount: number;
  lockedUntil: number;
}

export interface MockOptions {
  latencyMs?: number;
  takenLoginIds?: string[];
  takenCharacterNames?: string[];
}

const ACCESS_TOKEN_TTL_MS = 15 * 60 * 1000;
const LOCK_AFTER_FAILURES = 5;
const LOCK_MS = 15 * 60 * 1000;

export const MOCK_DEMO_LOGIN_ID = 'demo01';
const MOCK_DEMO_PASSWORD = 'demo1234';
// 아이디, 비밀번호가 없는 기존 계정(초대 코드로 만든 계정)을 복구하는 일회용 코드
export const MOCK_RECOVERY_CODE = 'RECOVER-0001';

export function createMockApi(options: MockOptions = {}): ApiClient {
  const latency = options.latencyMs ?? 250;
  const accounts = new Map<string, MockAccount>(); // accountId 기준
  const tokens = new Map<string, string>(); // 접근 토큰 -> accountId
  const inviteCodes = new Map<string, string>(); // 복구 코드 -> accountId
  const recoveryTokens = new Set<string>(); // 복구 코드로 받은 토큰은 현재 비밀번호 없이 자격을 정할 수 있다
  const takenNames = new Set((options.takenCharacterNames ?? ['월백', 'admin', 'shinshi']).map(characterNameKey));
  let seq = 0;

  const now = () => Date.now();
  const add = (loginId: string | null, password: string | null, email: string | null, createdAtUnixMs = now()) => {
    seq++;
    const accountId = `mock-${seq.toString().padStart(4, '0')}`;
    accounts.set(accountId, {
      me: { accountId, loginId, email, emailVerified: false, createdAtUnixMs },
      password,
      failedCount: 0,
      lockedUntil: 0,
    });
    return accountId;
  };
  const byLoginId = (loginId: string) => {
    for (const account of accounts.values()) if (account.me.loginId === loginId) return account;
    return undefined;
  };

  for (const id of options.takenLoginIds ?? ['admin', 'shinshi']) add(id, `x${randomId()}`, `${id}@example.com`);
  add(MOCK_DEMO_LOGIN_ID, MOCK_DEMO_PASSWORD, 'demo01@example.com', Date.UTC(2026, 9, 1, 3, 0));
  inviteCodes.set(MOCK_RECOVERY_CODE, add(null, null, null, Date.UTC(2026, 8, 20, 9, 0)));

  const wait = (signal?: AbortSignal) =>
    new Promise<void>((resolve, reject) => {
      if (latency <= 0) return resolve();
      const timer = setTimeout(resolve, latency);
      signal?.addEventListener('abort', () => {
        clearTimeout(timer);
        reject(new DOMException('aborted', 'AbortError'));
      });
    });

  const accountFor = (token: string) => {
    const accountId = tokens.get(token);
    const account = accountId ? accounts.get(accountId) : undefined;
    if (!account) throw new ApiError(RejectReason.TokenExpired, 401);
    return account;
  };

  const issue = (account: MockAccount) => {
    const token = `mock-${randomId()}`;
    tokens.set(token, account.me.accountId);
    const t = now();
    return {
      accountId: account.me.accountId,
      accessToken: token,
      accessTokenExpiresAtUnixMs: t + ACCESS_TOKEN_TTL_MS,
      refreshToken: `mock-refresh-${randomId()}`,
      refreshTokenExpiresAtUnixMs: t + 24 * 60 * 60 * 1000,
      serverTimeUnixMs: t,
    };
  };

  return {
    async checkLoginId(raw, signal) {
      await wait(signal);
      const id = normalizeLoginId(raw);
      if (!validateLoginId(id).ok) return { available: false, reasonCode: RejectReason.InvalidLoginId, serverTimeUnixMs: now() };
      const taken = byLoginId(id) !== undefined;
      return { available: !taken, reasonCode: taken ? RejectReason.LoginIdTaken : 0, serverTimeUnixMs: now() };
    },

    async signup({ loginId, password, email }) {
      await wait();
      const id = normalizeLoginId(loginId);
      if (!validateLoginId(id).ok) throw new ApiError(RejectReason.InvalidLoginId, 400);
      if (!validateEmail(email).ok) throw new ApiError(RejectReason.InvalidEmail, 400);
      if (!validatePassword(password, id).ok) throw new ApiError(RejectReason.WeakPassword, 400);
      if (byLoginId(id)) throw new ApiError(RejectReason.LoginIdTaken, 409);
      // 미인증 이메일은 중복을 허용한다
      const accountId = add(id, password, email.trim());
      return { accountId, loginId: id, emailVerified: false, serverTimeUnixMs: now() };
    },

    async exchange(request) {
      await wait();
      if ('inviteCode' in request) {
        const accountId = inviteCodes.get(request.inviteCode.trim());
        if (!accountId) throw new ApiError(RejectReason.InvalidInviteCode, 401);
        inviteCodes.delete(request.inviteCode.trim());
        const issued = issue(accounts.get(accountId)!);
        recoveryTokens.add(issued.accessToken);
        return issued;
      }
      const t = now();
      const account = byLoginId(normalizeLoginId(request.loginId));
      if (account && account.lockedUntil > t) throw new ApiError(RejectReason.AccountLocked, 403);
      if (!account || account.password === null || account.password !== request.password) {
        if (account && ++account.failedCount >= LOCK_AFTER_FAILURES) {
          account.failedCount = 0;
          account.lockedUntil = t + LOCK_MS;
          throw new ApiError(RejectReason.AccountLocked, 403);
        }
        throw new ApiError(RejectReason.InvalidCredentials, 401);
      }
      account.failedCount = 0;
      return issue(account);
    },

    async me(token, signal) {
      await wait(signal);
      return { ...accountFor(token).me };
    },

    async setCredentials({ currentPassword, newPassword, loginId, email }, token) {
      await wait();
      const account = accountFor(token);
      if (account.password !== null && !recoveryTokens.has(token) && currentPassword !== account.password) {
        throw new ApiError(RejectReason.InvalidCredentials, 401);
      }
      let nextLoginId = account.me.loginId;
      if (nextLoginId === null) {
        const id = normalizeLoginId(loginId ?? '');
        if (!validateLoginId(id).ok) throw new ApiError(RejectReason.InvalidLoginId, 400);
        if (byLoginId(id)) throw new ApiError(RejectReason.LoginIdTaken, 409);
        nextLoginId = id;
      }
      let nextEmail = account.me.email;
      if (nextEmail === null) {
        if (!validateEmail(email ?? '').ok) throw new ApiError(RejectReason.InvalidEmail, 400);
        nextEmail = (email ?? '').trim();
      }
      if (!validatePassword(newPassword, nextLoginId).ok) throw new ApiError(RejectReason.WeakPassword, 400);

      account.me = { ...account.me, loginId: nextLoginId, email: nextEmail };
      account.password = newPassword;
      // 자격을 바꾸면 그 계정의 모든 세션을 폐기한다
      for (const [t, id] of tokens) {
        if (id === account.me.accountId) {
          tokens.delete(t);
          recoveryTokens.delete(t);
        }
      }
    },

    async checkCharacterName(name, token, signal) {
      await wait(signal);
      accountFor(token);
      if (!validateCharacterName(name).ok) {
        return { available: false, reasonCode: RejectReason.InvalidCharacterName, serverTimeUnixMs: now() };
      }
      const taken = takenNames.has(characterNameKey(normalizeCharacterName(name)));
      return { available: !taken, reasonCode: taken ? RejectReason.CharacterNameTaken : 0, serverTimeUnixMs: now() };
    },
  };
}

function randomId(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}
