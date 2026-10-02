import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { rejectMessage } from '../api/errors';
import { useAuth } from '../auth/AuthContext';
import { AuthPageShell } from '../components/Layout';
import { Alert, Button, PasswordToggle, TextField } from '../components/ui';

interface LoginLocationState {
  from?: string;
  signedUpLoginId?: string;
  notice?: string;
}

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state ?? {}) as LoginLocationState;

  const [loginId, setLoginId] = useState(state.signedUpLoginId ?? '');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    if (loginId.trim().length === 0 || password.length === 0) {
      setError('아이디와 비밀번호를 입력해 주세요.');
      return;
    }
    setError(null);
    setSubmitting(true);
    try {
      await login(loginId, password);
      setPassword('');
      navigate(state.from && state.from.startsWith('/') ? state.from : '/me', { replace: true });
    } catch (e) {
      setError(rejectMessage(e, 'login'));
      setPassword('');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthPageShell title="로그인" subtitle="런처와 같은 아이디, 비밀번호로 로그인합니다.">
      {state.notice && (
        <div className="mb-4">
          <Alert tone="info">{state.notice}</Alert>
        </div>
      )}
      {state.signedUpLoginId && (
        <div className="mb-4">
          <Alert tone="ok">
            <strong className="font-semibold">{state.signedUpLoginId}</strong> 계정이 만들어졌습니다. 이메일 인증은 준비 중이며 나중에
            안내합니다.
          </Alert>
        </div>
      )}
      <form noValidate onSubmit={onSubmit} className="rounded-2xl border border-divider bg-panel p-5 sm:p-8">
        <div className="flex flex-col gap-3">
          <TextField
            label="아이디"
            name="username"
            type="text"
            autoComplete="username"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            maxLength={32}
            required
            value={loginId}
            onChange={(e) => setLoginId(e.target.value)}
          />
          <TextField
            label="비밀번호"
            name="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            maxLength={64}
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            trailing={<PasswordToggle shown={showPassword} onToggle={() => setShowPassword((v) => !v)} />}
          />
          <div aria-live="assertive">{error && <Alert>{error}</Alert>}</div>
          <Button type="submit" loading={submitting} className="w-full">
            {submitting ? '로그인하는 중...' : '로그인'}
          </Button>
          <p className="text-center text-[13px] text-muted">
            보안을 위해 로그인 상태는 이 탭에만 유지되며 새로고침하면 다시 로그인해야 합니다.
          </p>
          <p className="text-center text-[13px]">
            <Link to="/recover" className="inline-flex min-h-11 items-center text-muted underline underline-offset-4 hover:text-fg">
              운영자에게 복구 코드를 받았나요? 계정 복구
            </Link>
          </p>
        </div>
      </form>
      <p className="mt-6 text-center text-[15px] text-muted">
        계정이 없나요?{' '}
        <Link to="/signup" className="font-semibold text-accent underline-offset-4 hover:text-accent-hover hover:underline">
          회원가입
        </Link>
      </p>
    </AuthPageShell>
  );
}
