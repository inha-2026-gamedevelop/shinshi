import { ApiError, ClientFailure, RejectReason } from './errors';
import type {
  AccountCredentialsRequest,
  AccountMeResponse,
  AccountSignupRequest,
  AccountSignupResponse,
  ApiClient,
  AuthExchangeRequest,
  AuthExchangeResponse,
  AvailabilityResponse,
} from './types';

interface RequestOptions {
  method?: 'GET' | 'POST';
  body?: string;
  token?: string;
  signal?: AbortSignal;
}

// 같은 출처의 /v1 을 부른다. 개발은 Vite 프록시, 배포는 역방향 프록시가 API로 넘긴다
// 요청 본문과 토큰은 어디에도 출력하지 않는다
async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (options.token) headers.Authorization = `Bearer ${options.token}`;

  let response: Response;
  try {
    response = await fetch(path, {
      method: options.method ?? 'GET',
      body: options.body,
      signal: options.signal,
      headers,
      credentials: 'omit',
      cache: 'no-store',
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(ClientFailure.Network, 0);
  }

  const text = await response.text();
  const body = text.length > 0 ? safeParse(text) : null;

  if (!response.ok) {
    throw new ApiError(reasonCodeOf(body, response.status), response.status);
  }
  return body as T;
}

function reasonCodeOf(body: unknown, status: number): number {
  if (body && typeof body === 'object' && 'reasonCode' in body && typeof body.reasonCode === 'number') {
    return body.reasonCode;
  }
  if (status === 401) return ClientFailure.Unauthorized;
  if (status === 429) return RejectReason.RateLimited;
  if (status === 404) return ClientFailure.NotFound;
  return ClientFailure.Unknown;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

export function createHttpApi(): ApiClient {
  return {
    checkLoginId: (loginId, signal) =>
      request<AvailabilityResponse>(`/v1/accounts/login-id-availability?loginId=${encodeURIComponent(loginId)}`, {
        signal,
      }),
    signup: (body: AccountSignupRequest) =>
      request<AccountSignupResponse>('/v1/accounts', { method: 'POST', body: JSON.stringify(body) }),
    exchange: (body: AuthExchangeRequest) =>
      request<AuthExchangeResponse>('/v1/auth/exchange', { method: 'POST', body: JSON.stringify(body) }),
    me: (token, signal) => request<AccountMeResponse>('/v1/accounts/me', { token, signal }),
    setCredentials: async (body: AccountCredentialsRequest, token) => {
      await request<null>('/v1/accounts/credentials', { method: 'POST', body: JSON.stringify(body), token });
    },
    checkCharacterName: (name, token, signal) =>
      request<AvailabilityResponse>(`/v1/characters/name-availability?name=${encodeURIComponent(name)}`, {
        token,
        signal,
      }),
  };
}
