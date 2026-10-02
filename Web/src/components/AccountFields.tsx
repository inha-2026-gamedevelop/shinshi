import { forwardRef, useCallback, useState, type Ref } from 'react';
import { useApi } from '../api/ApiContext';
import { RejectReason } from '../api/errors';
import { useAvailability, type AvailabilityState } from '../lib/useAvailability';
import { normalizeLoginId, passwordStrength, validateLoginId, type RuleResult } from '../lib/validation';
import { PasswordToggle, StatusIcon, TextField, type FieldTone } from './ui';

// 가입, 계정 복구가 함께 쓰는 아이디 칸과 새 비밀번호 칸

function loginIdStatus(state: AvailabilityState, serverTaken: boolean): { tone: FieldTone; text: string } {
  if (serverTaken) return { tone: 'error', text: '이미 사용 중인 아이디입니다.' };
  switch (state.kind) {
    case 'idle':
      return { tone: 'neutral', text: '' };
    case 'invalid':
      return { tone: 'error', text: `형식 오류: ${state.message}` };
    case 'checking':
      return { tone: 'pending', text: '사용 가능한지 확인하는 중...' };
    case 'available':
      return { tone: 'ok', text: '사용 가능한 아이디입니다.' };
    case 'taken':
      return { tone: 'error', text: '이미 사용 중인 아이디입니다.' };
    case 'error':
      return { tone: 'neutral', text: state.message };
  }
}

/** 아이디 입력과 400ms 디바운스 중복 확인. blocked는 제출을 막아야 하는 상태(형식 오류, 이미 사용 중) */
export function useLoginIdCheck(loginId: string, takenIds: ReadonlySet<string>) {
  const api = useApi();
  const normalized = normalizeLoginId(loginId);
  const check = useCallback((value: string, signal: AbortSignal) => api.checkLoginId(value, signal), [api]);
  const availability = useAvailability({
    value: normalized,
    validate: validateLoginId,
    check,
    invalidCode: RejectReason.InvalidLoginId,
    scope: 'availability',
  });
  const status = loginIdStatus(availability, takenIds.has(normalized));
  return { normalized, status, blocked: !validateLoginId(loginId).ok || status.tone === 'error' };
}

interface LoginIdFieldProps {
  value: string;
  onChange: (value: string) => void;
  status: { tone: FieldTone; text: string };
}

export const LoginIdField = forwardRef(function LoginIdField(
  { value, onChange, status }: LoginIdFieldProps,
  ref: Ref<HTMLInputElement>,
) {
  return (
    <TextField
      ref={ref}
      label="아이디"
      name="username"
      type="text"
      autoComplete="username"
      autoCapitalize="none"
      autoCorrect="off"
      spellCheck={false}
      maxLength={32}
      required
      value={value}
      onChange={(e) => onChange(e.target.value)}
      hint="4~16자, 영문 소문자·숫자·밑줄(_). 대문자는 소문자로 바뀝니다."
      statusTone={status.tone}
      status={
        status.text && (
          <span className="inline-flex items-center gap-1.5" data-testid="login-id-status">
            <StatusIcon tone={status.tone} />
            {status.text}
          </span>
        )
      }
    />
  );
});

const STRENGTH_BAR = ['bg-divider', 'bg-danger', 'bg-warn', 'bg-ok/80', 'bg-ok'];

interface NewPasswordFieldsProps {
  password: string;
  confirm: string;
  onPassword: (value: string) => void;
  onConfirm: (value: string) => void;
  passwordRule: RuleResult;
  confirmRule: RuleResult;
  showErrors: { password: boolean; confirm: boolean };
  onBlur: (field: 'password' | 'confirm') => void;
  passwordRef?: Ref<HTMLInputElement>;
  confirmRef?: Ref<HTMLInputElement>;
  label?: string;
}

export function NewPasswordFields({
  password,
  confirm,
  onPassword,
  onConfirm,
  passwordRule,
  confirmRule,
  showErrors,
  onBlur,
  passwordRef,
  confirmRef,
  label = '비밀번호',
}: NewPasswordFieldsProps) {
  const [shown, setShown] = useState(false);
  const strength = passwordStrength(password);

  return (
    <>
      <TextField
        ref={passwordRef}
        label={label}
        name="new-password"
        type={shown ? 'text' : 'password'}
        autoComplete="new-password"
        maxLength={64}
        required
        value={password}
        onChange={(e) => onPassword(e.target.value)}
        onBlur={() => onBlur('password')}
        trailing={<PasswordToggle shown={shown} onToggle={() => setShown((v) => !v)} />}
        hint="8~64자, 영문과 숫자를 모두 포함. 아이디와 같으면 안 됩니다."
        statusTone={showErrors.password && !passwordRule.ok ? 'error' : 'neutral'}
        status={showErrors.password && !passwordRule.ok ? passwordRule.message : undefined}
      />
      {password.length > 0 && (
        <div className="-mt-2 mb-1" aria-hidden="true">
          <div className="flex gap-1">
            {[1, 2, 3, 4].map((step) => (
              <span
                key={step}
                className={`h-1.5 flex-1 rounded-full ${step <= strength.score ? STRENGTH_BAR[strength.score] : 'bg-divider'}`}
              />
            ))}
          </div>
          <p className="mt-1 text-[12px] text-muted">강도: {strength.label} (참고용)</p>
        </div>
      )}
      <TextField
        ref={confirmRef}
        label={`${label} 확인`}
        name="confirm-password"
        type={shown ? 'text' : 'password'}
        autoComplete="new-password"
        maxLength={64}
        required
        value={confirm}
        onChange={(e) => onConfirm(e.target.value)}
        onBlur={() => onBlur('confirm')}
        statusTone={showErrors.confirm && !confirmRule.ok ? 'error' : confirm.length > 0 && confirmRule.ok ? 'ok' : 'neutral'}
        status={
          showErrors.confirm && !confirmRule.ok
            ? confirmRule.message
            : confirm.length > 0 && confirmRule.ok
              ? '비밀번호가 일치합니다.'
              : undefined
        }
      />
    </>
  );
}
