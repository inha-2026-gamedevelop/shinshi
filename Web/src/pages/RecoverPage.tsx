import { useRef, useState, type FormEvent, type RefObject } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useApi } from '../api/ApiContext';
import { ApiError, RejectReason, rejectMessage } from '../api/errors';
import type { AccountMeResponse } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { LoginIdField, NewPasswordFields, useLoginIdCheck } from '../components/AccountFields';
import { AuthPageShell } from '../components/Layout';
import { Alert, Button, TextField } from '../components/ui';
import { validateEmail, validatePassword, validatePasswordConfirm } from '../lib/validation';

// 운영자에게 받은 복구 코드로 로그인한 뒤 아이디(없을 때), 이메일(없을 때), 새 비밀번호를 정한다
// 성공하면 서버가 그 계정의 모든 세션을 폐기하므로 메모리 토큰도 버리고 로그인 화면으로 보낸다

export function RecoverPage() {
  const api = useApi();
  const { accessToken, loginWithRecoveryCode, logout } = useAuth();
  const navigate = useNavigate();

  const [step, setStep] = useState<'code' | 'credentials'>('code');
  const [code, setCode] = useState('');
  const [account, setAccount] = useState<AccountMeResponse | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmitCode(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    if (code.trim().length === 0) {
      setError('복구 코드를 입력해 주세요.');
      return;
    }
    setError(null);
    setSubmitting(true);
    let token: string | null = null;
    try {
      token = await loginWithRecoveryCode(code);
      setCode('');
      // 계정에 아이디, 이메일이 비어 있는지 확인한다
      setAccount(await api.me(token));
      setStep('credentials');
    } catch (e) {
      if (token) logout();
      setError(rejectMessage(e, 'recover'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthPageShell
      title="계정 복구"
      subtitle="운영자에게 받은 복구 코드로 들어와 아이디와 새 비밀번호를 정합니다."
    >
      {step === 'code' && (
        <form noValidate onSubmit={onSubmitCode} className="rounded-2xl border border-divider bg-panel p-5 sm:p-8">
          <div className="flex flex-col gap-3">
            <TextField
              label="복구 코드"
              name="recovery-code"
              type="text"
              autoComplete="one-time-code"
              autoCapitalize="characters"
              spellCheck={false}
              maxLength={128}
              required
              value={code}
              onChange={(e) => setCode(e.target.value)}
              hint="코드는 한 번만 쓸 수 있습니다."
            />
            <div aria-live="assertive">{error && <Alert>{error}</Alert>}</div>
            <Button type="submit" loading={submitting} className="w-full">
              {submitting ? '확인하는 중...' : '다음'}
            </Button>
          </div>
        </form>
      )}

      {step === 'credentials' && account && accessToken && (
        <CredentialsForm
          account={account}
          accessToken={accessToken}
          onDone={() => {
            logout();
            navigate('/login', {
              replace: true,
              state: { notice: '아이디와 비밀번호를 정했습니다. 다른 기기의 로그인은 모두 해제되었습니다. 새 비밀번호로 로그인해 주세요.' },
            });
          }}
          onExpired={(e) => {
            logout();
            setStep('code');
            setAccount(null);
            setError(rejectMessage(e));
          }}
        />
      )}

      <p className="mt-6 text-center text-[15px] text-muted">
        아이디와 비밀번호를 알고 있나요?{' '}
        <Link to="/login" className="font-semibold text-accent underline-offset-4 hover:text-accent-hover hover:underline">
          로그인
        </Link>
      </p>
    </AuthPageShell>
  );
}

function CredentialsForm({
  account,
  accessToken,
  onDone,
  onExpired,
}: {
  account: AccountMeResponse;
  accessToken: string;
  onDone: () => void;
  onExpired: (e: unknown) => void;
}) {
  const api = useApi();
  const needsLoginId = account.loginId === null;
  const needsEmail = account.email === null;

  const [loginId, setLoginId] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [touched, setTouched] = useState({ password: false, confirm: false, email: false });
  const [takenIds, setTakenIds] = useState<ReadonlySet<string>>(() => new Set());
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loginIdRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const confirmRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);

  const idCheck = useLoginIdCheck(needsLoginId ? loginId : '', takenIds);
  const effectiveLoginId = needsLoginId ? idCheck.normalized : (account.loginId ?? '');
  const passwordRule = validatePassword(password, effectiveLoginId);
  const confirmRule = validatePasswordConfirm(password, confirm);
  const emailRule = validateEmail(email);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    setError(null);
    setTouched({ password: true, confirm: true, email: true });
    const checks: [boolean, RefObject<HTMLInputElement | null>][] = [
      [needsLoginId && idCheck.blocked, loginIdRef],
      [!passwordRule.ok, passwordRef],
      [!confirmRule.ok, confirmRef],
      [needsEmail && !emailRule.ok, emailRef],
    ];
    const firstInvalid = checks.find(([bad]) => bad);
    if (firstInvalid) {
      firstInvalid[1].current?.focus();
      return;
    }

    setSubmitting(true);
    try {
      await api.setCredentials(
        {
          currentPassword: null,
          newPassword: password,
          ...(needsLoginId ? { loginId: idCheck.normalized } : {}),
          ...(needsEmail ? { email: email.trim() } : {}),
        },
        accessToken,
      );
      onDone();
    } catch (e) {
      if (e instanceof ApiError && e.isAuthFailure) return onExpired(e);
      if (e instanceof ApiError && e.reasonCode === RejectReason.LoginIdTaken) {
        setTakenIds((s) => new Set(s).add(idCheck.normalized));
        loginIdRef.current?.focus();
      } else {
        setError(rejectMessage(e, 'recover'));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form noValidate onSubmit={onSubmit} className="rounded-2xl border border-divider bg-panel p-5 sm:p-8">
      <div className="flex flex-col gap-3">
        {needsLoginId ? (
          <LoginIdField ref={loginIdRef} value={loginId} onChange={setLoginId} status={idCheck.status} />
        ) : (
          <p className="mb-2 text-[15px]">
            <span className="text-muted">아이디</span> <strong className="ml-2 font-semibold">{account.loginId}</strong>
          </p>
        )}
        <NewPasswordFields
          label="새 비밀번호"
          password={password}
          confirm={confirm}
          onPassword={setPassword}
          onConfirm={setConfirm}
          passwordRule={passwordRule}
          confirmRule={confirmRule}
          showErrors={touched}
          onBlur={(f) => setTouched((t) => ({ ...t, [f]: true }))}
          passwordRef={passwordRef}
          confirmRef={confirmRef}
        />
        {needsEmail && (
          <TextField
            ref={emailRef}
            label="이메일"
            name="email"
            type="email"
            autoComplete="email"
            inputMode="email"
            autoCapitalize="none"
            spellCheck={false}
            maxLength={254}
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, email: true }))}
            hint="인증 메일 발송은 준비 중입니다."
            statusTone={touched.email && !emailRule.ok ? 'error' : 'neutral'}
            status={touched.email && !emailRule.ok ? emailRule.message : undefined}
          />
        )}
        <div aria-live="assertive">{error && <Alert>{error}</Alert>}</div>
        <Button type="submit" loading={submitting} className="w-full">
          {submitting ? '저장하는 중...' : '저장하고 다시 로그인'}
        </Button>
        <p className="text-center text-[13px] text-muted">저장하면 이 계정의 모든 로그인이 해제됩니다.</p>
      </div>
    </form>
  );
}
