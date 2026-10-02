// 계정, 캐릭터 이름 입력 규칙. docs/design/account-and-website.md 2, 3장과 같게 유지한다
// 서버가 최종 판정하므로 여기 결과는 안내용이다

export type RuleResult = { ok: true } | { ok: false; message: string };

const OK: RuleResult = { ok: true };
const fail = (message: string): RuleResult => ({ ok: false, message });

export const LOGIN_ID_MIN = 4;
export const LOGIN_ID_MAX = 16;
export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 64;
export const EMAIL_MAX = 254;
export const CHARACTER_NAME_MIN = 2;
export const CHARACTER_NAME_MAX = 12;

/** 아이디는 소문자로 바꿔 저장하고 비교한다 */
export function normalizeLoginId(raw: string): string {
  return raw.trim().toLowerCase();
}

export function validateLoginId(raw: string): RuleResult {
  const id = normalizeLoginId(raw);
  if (id.length === 0) return fail('아이디를 입력해 주세요.');
  if (id.length < LOGIN_ID_MIN || id.length > LOGIN_ID_MAX)
    return fail(`아이디는 ${LOGIN_ID_MIN}~${LOGIN_ID_MAX}자여야 합니다.`);
  if (!/^[a-z0-9_]+$/.test(id)) return fail('영문 소문자, 숫자, 밑줄(_)만 쓸 수 있습니다.');
  return OK;
}

export function validatePassword(password: string, loginId: string): RuleResult {
  if (password.length === 0) return fail('비밀번호를 입력해 주세요.');
  if (password.length < PASSWORD_MIN || password.length > PASSWORD_MAX)
    return fail(`비밀번호는 ${PASSWORD_MIN}~${PASSWORD_MAX}자여야 합니다.`);
  if (!/[A-Za-z]/.test(password) || !/[0-9]/.test(password))
    return fail('영문과 숫자를 모두 포함해야 합니다.');
  const id = normalizeLoginId(loginId);
  if (id.length > 0 && password.toLowerCase() === id) return fail('아이디와 같은 비밀번호는 쓸 수 없습니다.');
  return OK;
}

export function validatePasswordConfirm(password: string, confirm: string): RuleResult {
  if (confirm.length === 0) return fail('비밀번호를 한 번 더 입력해 주세요.');
  if (password !== confirm) return fail('비밀번호가 일치하지 않습니다.');
  return OK;
}

/** 앞뒤 공백을 지운 뒤 소문자로 비교한다 */
export function normalizeEmail(raw: string): string {
  return raw.trim().toLowerCase();
}

export function validateEmail(raw: string): RuleResult {
  const email = raw.trim();
  if (email.length === 0) return fail('이메일을 입력해 주세요.');
  if (email.length > EMAIL_MAX) return fail(`이메일은 ${EMAIL_MAX}자 이하여야 합니다.`);
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return fail('이메일 형식이 올바르지 않습니다.');
  return OK;
}

/** 캐릭터 이름 처리 순서 1, 2단계: 앞뒤 공백 제거 -> NFKC 정규화. 서버, 게임과 같은 순서다 */
export function normalizeCharacterName(raw: string): string {
  return raw.trim().normalize('NFKC');
}

/** 중복 비교 키. 정규화한 이름을 소문자로 바꾼다(서버 name_key와 같다) */
export function characterNameKey(raw: string): string {
  return normalizeCharacterName(raw).toLowerCase();
}

/** 정규화한 값으로 길이와 문자를 검사한다 */
export function validateCharacterName(raw: string): RuleResult {
  const name = normalizeCharacterName(raw);
  if (name.length === 0) return fail('이름을 입력해 주세요.');
  if (!/^[가-힣A-Za-z0-9]+$/.test(name)) return fail('완성형 한글, 영문, 숫자만 쓸 수 있습니다. 공백과 기호는 안 됩니다.');
  if (name.length < CHARACTER_NAME_MIN || name.length > CHARACTER_NAME_MAX)
    return fail(`이름은 ${CHARACTER_NAME_MIN}~${CHARACTER_NAME_MAX}자여야 합니다.`);
  return OK;
}

export type PasswordStrength = { score: 0 | 1 | 2 | 3 | 4; label: string };

/** 안내용 강도. 규칙 통과 여부와 별개다 */
export function passwordStrength(password: string): PasswordStrength {
  if (password.length === 0) return { score: 0, label: '' };
  let score = 0;
  if (password.length >= PASSWORD_MIN) score++;
  if (password.length >= 12) score++;
  if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score++;
  if (/[^A-Za-z0-9]/.test(password)) score++;
  if (!/[A-Za-z]/.test(password) || !/[0-9]/.test(password)) score = Math.min(score, 1);
  const labels = ['매우 약함', '약함', '보통', '강함', '매우 강함'];
  const clamped = Math.min(score, 4) as PasswordStrength['score'];
  return { score: clamped, label: labels[clamped] };
}
