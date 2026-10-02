import { describe, expect, it } from 'vitest';
import { ApiError, ClientFailure, RejectReason, rejectMessage } from './errors';

describe('거절 코드 안내', () => {
  it('로그인 실패는 한 문구로 안내한다', () => {
    expect(rejectMessage(new ApiError(RejectReason.InvalidCredentials, 401), 'login')).toBe(
      '아이디 또는 비밀번호가 올바르지 않습니다.',
    );
  });

  it('잠금은 기기(IP) 단위 15분으로 안내한다', () => {
    expect(rejectMessage(new ApiError(RejectReason.AccountLocked, 403), 'login')).toBe(
      '이 기기에서 로그인 시도가 많아 15분 동안 잠겼습니다.',
    );
  });

  it('가입 거절 코드를 한국어로 바꾼다', () => {
    expect(rejectMessage(new ApiError(RejectReason.LoginIdTaken, 409), 'signup')).toContain('이미 사용 중');
    expect(rejectMessage(new ApiError(RejectReason.InvalidLoginId, 400), 'signup')).toContain('아이디 형식');
    expect(rejectMessage(new ApiError(RejectReason.InvalidEmail, 400), 'signup')).toContain('이메일 형식');
    expect(rejectMessage(new ApiError(RejectReason.WeakPassword, 400), 'signup')).toContain('비밀번호');
    expect(rejectMessage(new ApiError(RejectReason.RateLimited, 429), 'signup')).toContain('잠시 후');
  });

  it('가용성 조회 제한은 잠시 뒤 다시 확인하라고 안내한다', () => {
    expect(rejectMessage(new ApiError(RejectReason.RateLimited, 429), 'availability')).toContain('잠시 뒤 다시 확인해 주세요');
  });

  it('모르는 코드와 네트워크 실패도 문구를 준다', () => {
    expect(rejectMessage(new ApiError(ClientFailure.Network, 0))).toContain('연결할 수 없습니다');
    expect(rejectMessage(new ApiError(9999, 400))).toContain('9999');
  });
});
