import { useEffect, useState } from 'react';
import { ApiError, rejectMessage, type MessageScope } from '../api/errors';
import type { AvailabilityResponse } from '../api/types';
import type { RuleResult } from './validation';

export type AvailabilityState =
  | { kind: 'idle' }
  | { kind: 'invalid'; message: string }
  | { kind: 'checking' }
  | { kind: 'available' }
  | { kind: 'taken' }
  | { kind: 'error'; message: string };

// 아이디 가용성 조회는 IP당 1분 30회로 제한되므로 입력이 멈춘 뒤에만 묻는다
export const AVAILABILITY_DEBOUNCE_MS = 400;

interface Options {
  value: string;
  validate: (value: string) => RuleResult;
  check: (value: string, signal: AbortSignal) => Promise<AvailabilityResponse>;
  invalidCode: number;
  scope: MessageScope;
}

/** 입력이 멈추고 400ms 뒤 가용성을 묻는다. 형식이 틀리면 서버에 묻지 않는다. 결과는 안내용이며 확정은 생성 요청이 한다 */
export function useAvailability({ value, validate, check, invalidCode, scope }: Options): AvailabilityState {
  // 결과를 입력값과 함께 두고, 지금 입력과 다른 결과는 버린다. effect 안에서 동기 setState를 하지 않기 위함
  const [settled, setSettled] = useState<{ value: string; state: AvailabilityState } | null>(null);

  useEffect(() => {
    if (value.length === 0) return;
    const rule = validate(value);
    const controller = new AbortController();
    const settle = (state: AvailabilityState) => setSettled({ value, state });

    // 형식 오류도 입력이 멈춘 뒤에 보여 줘서 타이핑 중 깜빡임을 줄인다
    const timer = setTimeout(() => {
      if (!rule.ok) {
        settle({ kind: 'invalid', message: rule.message });
        return;
      }
      check(value, controller.signal)
        .then((result) => {
          if (controller.signal.aborted) return;
          if (result.available) settle({ kind: 'available' });
          else if (result.reasonCode === invalidCode)
            settle({ kind: 'invalid', message: rejectMessage(new ApiError(invalidCode, 200), scope) });
          else settle({ kind: 'taken' });
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) return;
          if (error instanceof ApiError && error.reasonCode === invalidCode) {
            settle({ kind: 'invalid', message: rejectMessage(error, scope) });
          } else {
            settle({ kind: 'error', message: rejectMessage(error, scope) });
          }
        });
    }, AVAILABILITY_DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [value, validate, check, invalidCode, scope]);

  if (value.length === 0) return { kind: 'idle' };
  if (settled && settled.value === value) return settled.state;
  return validate(value).ok ? { kind: 'checking' } : { kind: 'idle' };
}
