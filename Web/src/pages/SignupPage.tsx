import { useId, useRef, useState, type FormEvent, type RefObject } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useApi } from '../api/ApiContext';
import { ApiError, RejectReason, rejectMessage } from '../api/errors';
import { LoginIdField, NewPasswordFields, useLoginIdCheck } from '../components/AccountFields';
import { AuthPageShell } from '../components/Layout';
import { useToast } from '../components/Toast';
import { Alert, Button, TextField } from '../components/ui';
import { validateEmail, validatePassword, validatePasswordConfirm } from '../lib/validation';

type FieldName = 'password' | 'confirm' | 'email' | 'terms';

export function SignupPage() {
  const api = useApi();
  const navigate = useNavigate();
  const toast = useToast();
  const termsId = useId();

  const [loginId, setLoginId] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [email, setEmail] = useState('');
  const [terms, setTerms] = useState(false);
  const [touched, setTouched] = useState<Partial<Record<FieldName, boolean>>>({});
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [takenIds, setTakenIds] = useState<ReadonlySet<string>>(() => new Set());

  const loginIdRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const confirmRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const termsRef = useRef<HTMLInputElement>(null);

  const idCheck = useLoginIdCheck(loginId, takenIds);
  const passwordRule = validatePassword(password, idCheck.normalized);
  const confirmRule = validatePasswordConfirm(password, confirm);
  const emailRule = validateEmail(email);

  const touch = (name: FieldName) => setTouched((t) => ({ ...t, [name]: true }));
  const show = (name: FieldName) => touched[name] === true;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    setFormError(null);
    setTouched({ password: true, confirm: true, email: true, terms: true });

    const checks: [boolean, RefObject<HTMLInputElement | null>][] = [
      [idCheck.blocked, loginIdRef],
      [!passwordRule.ok, passwordRef],
      [!confirmRule.ok, confirmRef],
      [!emailRule.ok, emailRef],
      [!terms, termsRef],
    ];
    const firstInvalid = checks.find(([bad]) => bad);
    if (firstInvalid) {
      if (firstInvalid[1] === loginIdRef && idCheck.status.text === '') setFormError('아이디를 확인해 주세요.');
      firstInvalid[1].current?.focus();
      return;
    }

    setSubmitting(true);
    try {
      await api.signup({ loginId: idCheck.normalized, password, email: email.trim() });
      toast({
        tone: 'success',
        title: '가입이 완료되었습니다.',
        body: '이메일 인증은 나중에 제공됩니다. 지금 바로 로그인할 수 있습니다.',
      });
      navigate('/login', { state: { signedUpLoginId: idCheck.normalized } });
    } catch (error) {
      if (error instanceof ApiError && error.reasonCode === RejectReason.LoginIdTaken) {
        setTakenIds((s) => new Set(s).add(idCheck.normalized));
        loginIdRef.current?.focus();
      } else {
        setFormError(rejectMessage(error, 'signup'));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthPageShell title="회원가입" subtitle="SHINSHI 계정을 만들고 런처와 홈페이지에서 같은 아이디로 로그인하세요.">
      <form noValidate onSubmit={onSubmit} className="rounded-2xl border border-divider bg-panel p-5 sm:p-8">
        <div className="flex flex-col gap-3">
          <LoginIdField ref={loginIdRef} value={loginId} onChange={setLoginId} status={idCheck.status} />

          <NewPasswordFields
            password={password}
            confirm={confirm}
            onPassword={setPassword}
            onConfirm={setConfirm}
            passwordRule={passwordRule}
            confirmRule={confirmRule}
            showErrors={{ password: show('password'), confirm: show('confirm') }}
            onBlur={touch}
            passwordRef={passwordRef}
            confirmRef={confirmRef}
          />

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
            onBlur={() => touch('email')}
            hint="계정 복구 안내에 씁니다. 인증 메일 발송은 준비 중입니다."
            statusTone={show('email') && !emailRule.ok ? 'error' : 'neutral'}
            status={show('email') && !emailRule.ok ? emailRule.message : undefined}
          />

          <fieldset className="mt-1 rounded-lg border border-divider bg-panel-2 p-4">
            <legend className="px-1 text-sm font-medium">약관 동의</legend>
            {/* TODO(사용자): 이용약관, 개인정보 처리 문구 확정 전 자리 표시. 외부 공개 전에 교체한다 */}
            <div
              tabIndex={0}
              role="region"
              aria-label="이용약관 및 개인정보 처리 안내(초안)"
              className="max-h-28 overflow-y-auto rounded-md border border-divider/70 p-3 text-[13px] leading-relaxed text-muted"
            >
              <p className="font-semibold text-warn">[초안 · 확정 전 문구]</p>
              <p className="mt-1">
                이용약관과 개인정보 처리 방침은 아직 확정되지 않았습니다. 수집 항목은 아이디, 비밀번호(되돌릴 수 없는 형태로 저장),
                이메일이며 계정 식별과 복구에만 씁니다. 확정 문구는 외부 공개 전에 이 자리에 게시합니다.
              </p>
            </div>
            <div className="mt-3 flex items-start gap-3">
              <input
                ref={termsRef}
                id={termsId}
                type="checkbox"
                checked={terms}
                onChange={(e) => setTerms(e.target.checked)}
                aria-invalid={show('terms') && !terms ? true : undefined}
                aria-describedby={`${termsId}-status`}
                className="mt-0.5 size-5 shrink-0 cursor-pointer accent-[var(--color-accent)]"
              />
              <label htmlFor={termsId} className="cursor-pointer text-[15px]">
                이용약관과 개인정보 처리 방침에 동의합니다. <span className="text-muted">(필수)</span>
              </label>
            </div>
            <p id={`${termsId}-status`} aria-live="polite" className="mt-1 min-h-5 text-[13px] text-danger">
              {show('terms') && !terms ? '약관에 동의해야 가입할 수 있습니다.' : ''}
            </p>
          </fieldset>

          {formError && <Alert>{formError}</Alert>}

          <Button type="submit" loading={submitting} className="mt-2 w-full">
            {submitting ? '가입하는 중...' : '가입하기'}
          </Button>
        </div>
      </form>
      <p className="mt-6 text-center text-[15px] text-muted">
        이미 계정이 있나요?{' '}
        <Link to="/login" className="font-semibold text-accent underline-offset-4 hover:text-accent-hover hover:underline">
          로그인
        </Link>
      </p>
    </AuthPageShell>
  );
}
