// 와이어 형식. 키는 Assets/01.Scripts/00.Common/Contracts의 DataMember 이름(camelCase)과 같다
// 기준: docs/design/account-and-website.md 4장

export interface ApiRejection {
  reasonCode: number;
  buildId?: string | null;
  serverTimeUnixMs?: number;
}

export interface AccountSignupRequest {
  loginId: string;
  password: string;
  email: string;
}

export interface AccountSignupResponse {
  accountId: string;
  loginId: string;
  emailVerified: boolean;
  serverTimeUnixMs: number;
}

/** 웹은 build를 싣지 않는다. 서버는 build가 없으면 빌드 검사를 건너뛴다 */
export type AuthExchangeRequest = { loginId: string; password: string } | { inviteCode: string };

export interface AuthExchangeResponse {
  accountId: string;
  accessToken: string;
  accessTokenExpiresAtUnixMs: number;
  // 웹은 갱신 토큰을 받아도 저장하지 않는다
  refreshToken?: string;
  refreshTokenExpiresAtUnixMs?: number;
  serverTimeUnixMs: number;
}

/** reasonCode는 사용할 수 없는 이유(예: 형식 오류)이며 사용 가능하면 0 또는 없음 */
export interface AvailabilityResponse {
  available: boolean;
  reasonCode?: number | null;
  serverTimeUnixMs?: number;
}

/** 복구 코드로 들어온 기존 계정은 아이디, 이메일이 아직 없을 수 있다 */
export interface AccountMeResponse {
  accountId: string;
  loginId: string | null;
  email: string | null;
  emailVerified: boolean;
  createdAtUnixMs: number;
}

/**
 * 비밀번호 변경, 복구 뒤 자격 설정. 성공은 204이며 그 계정의 모든 세션이 폐기된다.
 * loginId, email은 계정에 없을 때만 싣는다
 */
export interface AccountCredentialsRequest {
  currentPassword: string | null;
  newPassword: string;
  loginId?: string;
  email?: string;
}

export interface ApiClient {
  checkLoginId(loginId: string, signal?: AbortSignal): Promise<AvailabilityResponse>;
  signup(request: AccountSignupRequest): Promise<AccountSignupResponse>;
  exchange(request: AuthExchangeRequest): Promise<AuthExchangeResponse>;
  me(accessToken: string, signal?: AbortSignal): Promise<AccountMeResponse>;
  setCredentials(request: AccountCredentialsRequest, accessToken: string): Promise<void>;
  checkCharacterName(name: string, accessToken: string, signal?: AbortSignal): Promise<AvailabilityResponse>;
}
