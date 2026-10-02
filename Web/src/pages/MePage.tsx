import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useApi } from '../api/ApiContext';
import { ApiError, RejectReason, rejectMessage } from '../api/errors';
import type { AccountMeResponse } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { NewPasswordFields } from '../components/AccountFields';
import { useToast } from '../components/Toast';
import { Alert, Button, Card, PasswordToggle, Spinner, StatusIcon, TextField, type FieldTone } from '../components/ui';
import { useAvailability, type AvailabilityState } from '../lib/useAvailability';
import { validateCharacterName, validatePassword, validatePasswordConfirm } from '../lib/validation';

const dateFormat = new Intl.DateTimeFormat('ko-KR', { dateStyle: 'long', timeStyle: 'short' });

export function MePage() {
  const api = useApi();
  const { accessToken, logout } = useAuth();
  const navigate = useNavigate();
  const [me, setMe] = useState<AccountMeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const expire = useCallback(
    (e: unknown) => {
      logout();
      navigate('/login', { replace: true, state: { from: '/me', notice: rejectMessage(e) } });
    },
    [logout, navigate],
  );

  useEffect(() => {
    if (!accessToken) return;
    const controller = new AbortController();
    api
      .me(accessToken, controller.signal)
      .then(setMe)
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        if (e instanceof ApiError && e.isAuthFailure) expire(e);
        else setError(rejectMessage(e));
      });
    return () => controller.abort();
  }, [api, accessToken, expire]);

  const onLogout = () => {
    logout();
    navigate('/', { replace: true });
  };

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-10 sm:px-6 sm:py-14">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold sm:text-[28px]">내 정보</h1>
          <p className="mt-1 text-[15px] text-muted">새로고침하거나 탭을 닫으면 로그아웃됩니다.</p>
        </div>
        <Button variant="secondary" onClick={onLogout}>
          로그아웃
        </Button>
      </div>

      <div className="mt-8 grid gap-6">
        <Card>
          <h2 className="text-lg font-semibold">계정</h2>
          {error && (
            <div className="mt-4">
              <Alert>{error}</Alert>
            </div>
          )}
          {!me && !error && (
            <div className="mt-4 flex items-center gap-2 text-muted" role="status">
              <Spinner /> 불러오는 중...
            </div>
          )}
          {me && (
            <dl className="mt-4 divide-y divide-divider">
              <InfoRow label="아이디" value={me.loginId ?? <span className="text-muted">미설정 (계정 복구에서 정할 수 있습니다)</span>} />
              <InfoRow
                label="이메일"
                value={
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="break-all">{me.email ?? <span className="text-muted">미설정</span>}</span>
                    {me.emailVerified ? (
                      <Badge tone="ok">인증됨</Badge>
                    ) : (
                      <Badge tone="warn">미인증 (인증 메일은 준비 중)</Badge>
                    )}
                  </span>
                }
              />
              <InfoRow label="가입일" value={dateFormat.format(new Date(me.createdAtUnixMs))} />
            </dl>
          )}
        </Card>

        {accessToken && me && (
          <PasswordChange
            accessToken={accessToken}
            loginId={me.loginId ?? ''}
            onAuthExpired={expire}
            onChanged={() => {
              logout();
              navigate('/login', {
                replace: true,
                state: { notice: '비밀번호를 바꿨습니다. 모든 기기에서 로그아웃되었으니 새 비밀번호로 다시 로그인해 주세요.' },
              });
            }}
          />
        )}

        {accessToken && <CharacterNameCheck accessToken={accessToken} onAuthExpired={expire} />}

        <Card>
          <h2 className="text-lg font-semibold">캐릭터</h2>
          {/* TODO(민성): 캐릭터 목록 API가 생기면 이 자리에 목록을 보여 준다 */}
          <p className="mt-2 text-[15px] text-muted">캐릭터 목록은 준비 중입니다. 캐릭터는 게임 안에서 만듭니다.</p>
        </Card>
      </div>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="grid gap-1 py-3 sm:grid-cols-[120px_1fr] sm:gap-4">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="text-[15px]">{value}</dd>
    </div>
  );
}

function Badge({ tone, children }: { tone: 'ok' | 'warn'; children: ReactNode }) {
  const cls = tone === 'ok' ? 'border-ok/50 text-ok' : 'border-warn/50 text-warn';
  return <span className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-[12px] font-medium ${cls}`}>{children}</span>;
}

function nameStatus(state: AvailabilityState): { tone: FieldTone; text: string } {
  switch (state.kind) {
    case 'idle':
      return { tone: 'neutral', text: '' };
    case 'invalid':
      return { tone: 'error', text: `형식 오류: ${state.message}` };
    case 'checking':
      return { tone: 'pending', text: '확인하는 중...' };
    case 'available':
      return { tone: 'ok', text: '지금은 사용 가능한 이름입니다.' };
    case 'taken':
      return { tone: 'error', text: '이미 사용 중인 이름입니다.' };
    case 'error':
      return { tone: 'neutral', text: state.message };
  }
}

function CharacterNameCheck({ accessToken, onAuthExpired }: { accessToken: string; onAuthExpired: (e: unknown) => void }) {
  const api = useApi();
  const [name, setName] = useState('');
  const check = useCallback(
    async (value: string, signal: AbortSignal) => {
      try {
        return await api.checkCharacterName(value, accessToken, signal);
      } catch (e) {
        if (e instanceof ApiError && e.isAuthFailure) onAuthExpired(e);
        throw e;
      }
    },
    [api, accessToken, onAuthExpired],
  );
  const state = useAvailability({
    value: name.trim(),
    validate: validateCharacterName,
    check,
    invalidCode: RejectReason.InvalidCharacterName,
    scope: 'character',
  });
  const status = nameStatus(state);

  return (
    <Card>
      <h2 className="text-lg font-semibold">캐릭터 이름 확인</h2>
      <p className="mt-1 text-[15px] text-muted">
        캐릭터 이름이 곧 닉네임이며 모든 캐릭터에서 겹칠 수 없습니다. 쓰고 싶은 이름이 비어 있는지 미리 확인해 보세요.
      </p>
      <form className="mt-4" onSubmit={(e) => e.preventDefault()} role="search" aria-label="캐릭터 이름 확인">
        <TextField
          label="캐릭터 이름"
          name="character-name"
          type="text"
          autoComplete="off"
          spellCheck={false}
          maxLength={24}
          value={name}
          onChange={(e) => setName(e.target.value)}
          hint="2~12자, 완성형 한글·영문·숫자. 공백과 기호는 쓸 수 없고 대소문자, 전각·반각만 다른 이름은 같은 이름으로 봅니다."
          statusTone={status.tone}
          status={
            status.text && (
              <span className="inline-flex items-center gap-1.5" data-testid="character-name-status">
                <StatusIcon tone={status.tone} />
                {status.text}
              </span>
            )
          }
        />
      </form>
      <p className="text-[13px] text-muted">확인 결과는 안내용입니다. 이름은 게임에서 캐릭터를 만들 때 확정되며 그 사이 다른 사람이 먼저 쓸 수 있습니다.</p>
    </Card>
  );
}

function PasswordChange({
  accessToken,
  loginId,
  onChanged,
  onAuthExpired,
}: {
  accessToken: string;
  loginId: string;
  onChanged: () => void;
  onAuthExpired: (e: unknown) => void;
}) {
  const api = useApi();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [showCurrent, setShowCurrent] = useState(false);
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [touched, setTouched] = useState({ password: false, confirm: false });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const currentRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const confirmRef = useRef<HTMLInputElement>(null);

  const passwordRule = validatePassword(password, loginId);
  const samePassword = password.length > 0 && password === current;
  const effectiveRule = samePassword ? { ok: false as const, message: '현재 비밀번호와 다른 비밀번호를 정해 주세요.' } : passwordRule;
  const confirmRule = validatePasswordConfirm(password, confirm);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    setError(null);
    setTouched({ password: true, confirm: true });
    if (current.length === 0) {
      setError('현재 비밀번호를 입력해 주세요.');
      currentRef.current?.focus();
      return;
    }
    if (!effectiveRule.ok) return passwordRef.current?.focus();
    if (!confirmRule.ok) return confirmRef.current?.focus();

    setSubmitting(true);
    try {
      await api.setCredentials({ currentPassword: current, newPassword: password }, accessToken);
      toast({ tone: 'success', title: '비밀번호를 바꿨습니다.', body: '새 비밀번호로 다시 로그인해 주세요.' });
      onChanged();
    } catch (e) {
      if (e instanceof ApiError && e.isAuthFailure) return onAuthExpired(e);
      setError(rejectMessage(e, 'password'));
      if (e instanceof ApiError && e.reasonCode === RejectReason.InvalidCredentials) {
        setCurrent('');
        currentRef.current?.focus();
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <h2 className="text-lg font-semibold">비밀번호 변경</h2>
      <p className="mt-1 text-[15px] text-muted">바꾸면 이 계정의 모든 기기에서 로그아웃됩니다.</p>
      <form noValidate onSubmit={onSubmit} className="mt-4 flex flex-col gap-3">
        {/* 브라우저 비밀번호 관리자가 계정을 알 수 있도록 숨은 아이디 칸을 둔다 */}
        <input type="text" name="username" autoComplete="username" value={loginId} readOnly hidden />
        <TextField
          ref={currentRef}
          label="현재 비밀번호"
          name="current-password"
          type={showCurrent ? 'text' : 'password'}
          autoComplete="current-password"
          maxLength={64}
          required
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          trailing={<PasswordToggle shown={showCurrent} onToggle={() => setShowCurrent((v) => !v)} />}
        />
        <NewPasswordFields
          label="새 비밀번호"
          password={password}
          confirm={confirm}
          onPassword={setPassword}
          onConfirm={setConfirm}
          passwordRule={effectiveRule}
          confirmRule={confirmRule}
          showErrors={touched}
          onBlur={(f) => setTouched((t) => ({ ...t, [f]: true }))}
          passwordRef={passwordRef}
          confirmRef={confirmRef}
        />
        <div aria-live="assertive">{error && <Alert>{error}</Alert>}</div>
        <div>
          <Button type="submit" loading={submitting}>
            {submitting ? '바꾸는 중...' : '비밀번호 바꾸기'}
          </Button>
        </div>
      </form>
    </Card>
  );
}
