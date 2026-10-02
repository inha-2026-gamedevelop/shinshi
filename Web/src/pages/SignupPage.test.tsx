import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ApiProvider } from '../api/ApiContext';
import { ApiError, RejectReason } from '../api/errors';
import { createMockApi } from '../api/mockClient';
import type { ApiClient } from '../api/types';
import { AuthProvider } from '../auth/AuthContext';
import { ToastProvider } from '../components/Toast';
import { SignupPage } from './SignupPage';

function renderSignup(api: ApiClient) {
  return render(
    <ApiProvider client={api}>
      <AuthProvider>
        <ToastProvider>
          <MemoryRouter initialEntries={['/signup']}>
            <Routes>
              <Route path="/signup" element={<SignupPage />} />
              <Route path="/login" element={<p>로그인 화면</p>} />
            </Routes>
          </MemoryRouter>
        </ToastProvider>
      </AuthProvider>
    </ApiProvider>,
  );
}

const loginIdStatus = () => screen.findByTestId('login-id-status');

describe('회원가입 아이디 중복 확인', () => {
  it('이미 있는 아이디는 "이미 사용 중"으로 표시한다', async () => {
    renderSignup(createMockApi({ latencyMs: 0 }));
    await userEvent.type(screen.getByLabelText('아이디'), 'shinshi');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('이미 사용 중인 아이디입니다.'));
  });

  it('대문자 입력도 소문자로 바꿔 확인한다', async () => {
    renderSignup(createMockApi({ latencyMs: 0 }));
    await userEvent.type(screen.getByLabelText('아이디'), 'ADMIN');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('이미 사용 중'));
  });

  it('새 아이디는 "사용 가능"으로 표시한다', async () => {
    renderSignup(createMockApi({ latencyMs: 0 }));
    await userEvent.type(screen.getByLabelText('아이디'), 'newplayer');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('사용 가능한 아이디입니다.'));
  });

  it('형식이 틀리면 서버에 묻지 않고 "형식 오류"를 보여 준다', async () => {
    const api = createMockApi({ latencyMs: 0 });
    const spy = vi.spyOn(api, 'checkLoginId');
    renderSignup(api);
    await userEvent.type(screen.getByLabelText('아이디'), 'ab-c');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('형식 오류'));
    expect(spy).not.toHaveBeenCalled();
  });

  it('입력이 멈춘 뒤 한 번만 묻는다(디바운스)', async () => {
    const api = createMockApi({ latencyMs: 0 });
    const spy = vi.spyOn(api, 'checkLoginId');
    renderSignup(api);
    await userEvent.type(screen.getByLabelText('아이디'), 'player77');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('사용 가능한 아이디입니다.'));
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith('player77', expect.any(AbortSignal));
  });

  it('요청 제한(811)을 받으면 잠시 뒤 다시 확인하라고 안내한다', async () => {
    const api = createMockApi({ latencyMs: 0 });
    vi.spyOn(api, 'checkLoginId').mockRejectedValue(new ApiError(RejectReason.RateLimited, 429));
    renderSignup(api);
    await userEvent.type(screen.getByLabelText('아이디'), 'player88');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('잠시 뒤 다시 확인해 주세요'));
  });
});

describe('회원가입 제출', () => {
  async function fillValid(loginId: string) {
    await userEvent.type(screen.getByLabelText('아이디'), loginId);
    await userEvent.type(screen.getByLabelText('비밀번호'), 'abcd1234');
    await userEvent.type(screen.getByLabelText('비밀번호 확인'), 'abcd1234');
    await userEvent.type(screen.getByLabelText('이메일'), 'admin@example.com');
    await userEvent.click(screen.getByLabelText(/이용약관과 개인정보 처리 방침에 동의합니다/));
  }

  it('성공하면 로그인 화면으로 간다. 이미 쓰인 이메일이어도 거절하지 않는다', async () => {
    renderSignup(createMockApi({ latencyMs: 0 }));
    await fillValid('freshuser');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('사용 가능한 아이디입니다.'));
    await userEvent.click(screen.getByRole('button', { name: '가입하기' }));
    expect(await screen.findByText('로그인 화면')).toBeInTheDocument();
    expect(screen.getByText('가입이 완료되었습니다.')).toBeInTheDocument();
  });

  it('제출 때 서버가 LoginIdTaken을 주면 아이디 칸에 표시한다', async () => {
    const api = createMockApi({ latencyMs: 0 });
    vi.spyOn(api, 'checkLoginId').mockResolvedValue({ available: true, reasonCode: 0 });
    vi.spyOn(api, 'signup').mockRejectedValue(new ApiError(RejectReason.LoginIdTaken, 409));
    renderSignup(api);
    await fillValid('racer01');
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('사용 가능한 아이디입니다.'));
    await userEvent.click(screen.getByRole('button', { name: '가입하기' }));
    await waitFor(async () => expect(await loginIdStatus()).toHaveTextContent('이미 사용 중'));
    expect(screen.getByLabelText('아이디')).toHaveFocus();
  });

  it('필수 칸이 비면 제출하지 않는다', async () => {
    const api = createMockApi({ latencyMs: 0 });
    const spy = vi.spyOn(api, 'signup');
    renderSignup(api);
    await userEvent.click(screen.getByRole('button', { name: '가입하기' }));
    expect(spy).not.toHaveBeenCalled();
    expect(screen.getByText('약관에 동의해야 가입할 수 있습니다.')).toBeInTheDocument();
  });
});
