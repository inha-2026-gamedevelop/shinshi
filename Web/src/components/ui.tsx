import { forwardRef, useId, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';

const BUTTON_BASE =
  'inline-flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-lg px-5 text-[15px] font-semibold transition-colors duration-200 disabled:cursor-not-allowed disabled:opacity-50';

const BUTTON_VARIANT = {
  // 주황 바탕에는 어두운 글자를 써서 대비 4.5:1 이상을 지킨다
  primary: 'bg-accent text-bg hover:bg-accent-hover disabled:hover:bg-accent',
  secondary: 'border border-divider bg-panel text-fg hover:border-muted/60 hover:bg-[#20252f]',
  ghost: 'text-muted hover:text-fg',
} as const;

type Variant = keyof typeof BUTTON_VARIANT;

export function buttonClass(variant: Variant = 'primary', extra = ''): string {
  return `${BUTTON_BASE} ${BUTTON_VARIANT[variant]} ${extra}`;
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  loading?: boolean;
}

export function Button({ variant = 'primary', loading = false, className = '', children, disabled, ...rest }: ButtonProps) {
  return (
    <button className={buttonClass(variant, className)} disabled={disabled || loading} aria-busy={loading} {...rest}>
      {loading && <Spinner />}
      {children}
    </button>
  );
}

export function LinkButton({ variant = 'primary', className = '', ...rest }: LinkProps & { variant?: Variant }) {
  return <Link className={buttonClass(variant, className)} {...rest} />;
}

export function Spinner({ className = 'size-4' }: { className?: string }) {
  return (
    <svg className={`animate-spin ${className}`} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="3" />
      <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}

export type FieldTone = 'neutral' | 'ok' | 'error' | 'pending';

const TONE_TEXT: Record<FieldTone, string> = {
  neutral: 'text-muted',
  ok: 'text-ok',
  error: 'text-danger',
  pending: 'text-muted',
};

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  hint?: ReactNode;
  status?: ReactNode;
  statusTone?: FieldTone;
  trailing?: ReactNode;
}

/** 라벨, 규칙 안내, 상태 문구를 입력과 연결한다. 상태 문구는 aria-live로 읽힌다 */
export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, hint, status, statusTone = 'neutral', trailing, className = '', ...input },
  ref,
) {
  const id = useId();
  const hintId = `${id}-hint`;
  const statusId = `${id}-status`;
  const invalid = statusTone === 'error';
  const borderTone = invalid
    ? 'border-danger/80 focus:border-danger'
    : statusTone === 'ok'
      ? 'border-ok/50 focus:border-accent'
      : 'border-divider focus:border-accent';

  return (
    <div className={className}>
      <label htmlFor={id} className="mb-1.5 block text-sm font-medium text-fg">
        {label}
      </label>
      <div className="relative">
        <input
          ref={ref}
          id={id}
          aria-invalid={invalid || undefined}
          aria-describedby={[hint ? hintId : null, statusId].filter(Boolean).join(' ')}
          className={`block min-h-12 w-full rounded-lg border bg-panel-2 px-4 text-base text-fg placeholder:text-muted/70 transition-colors duration-200 focus:outline-none focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent ${trailing ? 'pr-12' : ''} ${borderTone}`}
          {...input}
        />
        {trailing && <div className="absolute inset-y-0 right-0 flex items-center pr-1">{trailing}</div>}
      </div>
      {hint && (
        <p id={hintId} className="mt-1.5 text-[13px] text-muted">
          {hint}
        </p>
      )}
      <p id={statusId} aria-live="polite" className={`mt-1 min-h-5 text-[13px] ${TONE_TEXT[statusTone]}`}>
        {status}
      </p>
    </div>
  );
});

export function PasswordToggle({ shown, onToggle }: { shown: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-pressed={shown}
      aria-label={shown ? '비밀번호 숨기기' : '비밀번호 보기'}
      className="inline-flex size-11 cursor-pointer items-center justify-center rounded-lg text-muted transition-colors duration-200 hover:text-fg"
    >
      <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z" strokeLinejoin="round" />
        <circle cx="12" cy="12" r="3" />
        {shown && <path d="M4 4l16 16" strokeLinecap="round" />}
      </svg>
    </button>
  );
}

export function StatusIcon({ tone }: { tone: FieldTone }) {
  if (tone === 'pending') return <Spinner className="size-3.5" />;
  const path =
    tone === 'ok' ? 'M5 12.5l4.5 4.5L19 7.5' : tone === 'error' ? 'M12 7v6M12 16.5v.5' : 'M12 8v.5M12 11v5';
  return (
    <svg viewBox="0 0 24 24" className="size-3.5" fill="none" stroke="currentColor" strokeWidth="2.4" aria-hidden="true">
      <path d={path} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`rounded-2xl border border-divider bg-panel p-6 sm:p-8 ${className}`}>{children}</section>;
}

export function Alert({ tone = 'error', children }: { tone?: 'error' | 'info' | 'ok'; children: ReactNode }) {
  const toneClass =
    tone === 'error' ? 'border-danger/60 bg-danger/10' : tone === 'ok' ? 'border-ok/50 bg-ok/10' : 'border-divider bg-panel-2';
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={`rounded-lg border px-4 py-3 text-sm ${toneClass}`}>
      {children}
    </div>
  );
}
