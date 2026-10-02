// ContractCodes.RejectReason 중 웹이 다루는 값
export const RejectReason = {
  InvalidInviteCode: 800,
  InviteCodeExpired: 801,
  InviteCodeUsed: 802,
  TokenExpired: 803,
  TokenRevoked: 804,
  Maintenance: 807,
  Sanctioned: 808,
  BackendUnavailable: 810,
  RateLimited: 811,
  CharacterNameTaken: 1001,
  InvalidCharacterName: 1002,
  LoginIdTaken: 1200,
  // 이메일 인증 기능용으로 번호만 잡혀 있다. 가입에서는 돌려주지 않는다
  EmailTaken: 1201,
  InvalidLoginId: 1202,
  InvalidEmail: 1203,
  WeakPassword: 1204,
  InvalidCredentials: 1205,
  AccountLocked: 1206,
} as const;

/** 거절 코드가 없는 실패(네트워크, 본문 없는 4xx/5xx)는 음수 코드로 구분한다 */
export const ClientFailure = {
  Network: -1,
  Unknown: -2,
  Unauthorized: -401,
  NotFound: -404,
} as const;

export class ApiError extends Error {
  readonly reasonCode: number;
  readonly status: number;

  constructor(reasonCode: number, status: number) {
    super(`API 거절 ${reasonCode}`);
    this.name = 'ApiError';
    this.reasonCode = reasonCode;
    this.status = status;
  }

  get isAuthFailure(): boolean {
    return (
      this.reasonCode === ClientFailure.Unauthorized ||
      this.reasonCode === RejectReason.TokenExpired ||
      this.reasonCode === RejectReason.TokenRevoked
    );
  }
}

const COMMON_MESSAGES: Record<number, string> = {
  [RejectReason.RateLimited]: '요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.',
  [RejectReason.Maintenance]: '서버 점검 중입니다. 점검이 끝난 뒤 다시 시도해 주세요.',
  [RejectReason.BackendUnavailable]: '서버가 일시적으로 응답하지 않습니다. 잠시 후 다시 시도해 주세요.',
  [RejectReason.Sanctioned]: '이용이 제한된 계정입니다.',
  [ClientFailure.Network]: '서버에 연결할 수 없습니다. 인터넷 연결을 확인해 주세요.',
  [ClientFailure.NotFound]: '서버에 아직 이 기능이 준비되지 않았습니다.',
  [ClientFailure.Unauthorized]: '로그인이 만료되었습니다. 다시 로그인해 주세요.',
  [RejectReason.TokenExpired]: '로그인이 만료되었습니다. 다시 로그인해 주세요.',
  [RejectReason.TokenRevoked]: '로그인이 만료되었습니다. 다시 로그인해 주세요.',
};

const CREDENTIAL_RULE_MESSAGES: Record<number, string> = {
  [RejectReason.LoginIdTaken]: '이미 사용 중인 아이디입니다.',
  [RejectReason.InvalidLoginId]: '아이디 형식이 올바르지 않습니다.',
  [RejectReason.InvalidEmail]: '이메일 형식이 올바르지 않습니다.',
  [RejectReason.WeakPassword]: '비밀번호가 규칙에 맞지 않습니다. 영문과 숫자를 포함해 8자 이상으로 정해 주세요.',
};

const SIGNUP_MESSAGES: Record<number, string> = {
  ...CREDENTIAL_RULE_MESSAGES,
  [RejectReason.RateLimited]: '가입 요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.',
};

const LOGIN_MESSAGES: Record<number, string> = {
  [RejectReason.InvalidCredentials]: '아이디 또는 비밀번호가 올바르지 않습니다.',
  [RejectReason.AccountLocked]: '이 기기에서 로그인 시도가 많아 15분 동안 잠겼습니다.',
  [RejectReason.RateLimited]: '로그인 시도가 너무 많습니다. 잠시 후 다시 시도해 주세요.',
};

const RECOVER_MESSAGES: Record<number, string> = {
  [RejectReason.InvalidInviteCode]: '복구 코드가 올바르지 않습니다.',
  [RejectReason.InviteCodeExpired]: '복구 코드의 유효 기간이 지났습니다. 운영자에게 새 코드를 받아 주세요.',
  [RejectReason.InviteCodeUsed]: '이미 사용한 복구 코드입니다. 운영자에게 새 코드를 받아 주세요.',
  [RejectReason.RateLimited]: '시도가 너무 많습니다. 잠시 후 다시 시도해 주세요.',
  ...CREDENTIAL_RULE_MESSAGES,
};

const PASSWORD_CHANGE_MESSAGES: Record<number, string> = {
  [RejectReason.InvalidCredentials]: '현재 비밀번호가 올바르지 않습니다.',
  [RejectReason.AccountLocked]: '이 기기에서 시도가 많아 15분 동안 잠겼습니다.',
  ...CREDENTIAL_RULE_MESSAGES,
};

const AVAILABILITY_MESSAGES: Record<number, string> = {
  [RejectReason.RateLimited]: '확인 요청이 많습니다. 잠시 뒤 다시 확인해 주세요.',
  [RejectReason.InvalidLoginId]: '아이디 형식이 올바르지 않습니다.',
};

const CHARACTER_MESSAGES: Record<number, string> = {
  ...AVAILABILITY_MESSAGES,
  [RejectReason.CharacterNameTaken]: '이미 사용 중인 이름입니다.',
  [RejectReason.InvalidCharacterName]: '이름 형식이 올바르지 않습니다.',
};

export type MessageScope = 'signup' | 'login' | 'recover' | 'password' | 'availability' | 'character' | 'common';

const SCOPED: Record<MessageScope, Record<number, string>> = {
  signup: SIGNUP_MESSAGES,
  login: LOGIN_MESSAGES,
  recover: RECOVER_MESSAGES,
  password: PASSWORD_CHANGE_MESSAGES,
  availability: AVAILABILITY_MESSAGES,
  character: CHARACTER_MESSAGES,
  common: {},
};

export function rejectMessage(error: unknown, scope: MessageScope = 'common'): string {
  const code = error instanceof ApiError ? error.reasonCode : ClientFailure.Unknown;
  return (
    SCOPED[scope][code] ??
    COMMON_MESSAGES[code] ??
    `요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요. (코드 ${code})`
  );
}
