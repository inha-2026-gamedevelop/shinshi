import { describe, expect, it } from 'vitest';
import {
  characterNameKey,
  normalizeLoginId,
  passwordStrength,
  validateCharacterName,
  validateEmail,
  validateLoginId,
  validatePassword,
  validatePasswordConfirm,
} from './validation';

describe('아이디', () => {
  it('소문자로 바꾸고 앞뒤 공백을 지운다', () => {
    expect(normalizeLoginId('  Shinshi_01 ')).toBe('shinshi_01');
  });

  it.each(['abcd', 'a_b_1', 'ABCD', 'abcdefghijklmnop'])('%s 는 통과한다', (id) => {
    expect(validateLoginId(id).ok).toBe(true);
  });

  it.each(['abc', 'abcdefghijklmnopq', 'ab-cd', '한글아이디', 'ab cd', ''])('%s 는 거절한다', (id) => {
    expect(validateLoginId(id).ok).toBe(false);
  });
});

describe('비밀번호', () => {
  it('8~64자이며 영문과 숫자를 모두 포함해야 한다', () => {
    expect(validatePassword('abcd1234', 'user').ok).toBe(true);
    expect(validatePassword('abc123', 'user').ok).toBe(false);
    expect(validatePassword('abcdefgh', 'user').ok).toBe(false);
    expect(validatePassword('12345678', 'user').ok).toBe(false);
    expect(validatePassword('a1'.repeat(32), 'user').ok).toBe(true);
    expect(validatePassword('a1'.repeat(32) + 'x', 'user').ok).toBe(false);
  });

  it('아이디와 같으면 거절한다(대소문자 무시)', () => {
    expect(validatePassword('player01', 'player01').ok).toBe(false);
    expect(validatePassword('Player01', 'PLAYER01').ok).toBe(false);
  });

  it('확인 칸이 다르면 거절한다', () => {
    expect(validatePasswordConfirm('abcd1234', 'abcd1234').ok).toBe(true);
    expect(validatePasswordConfirm('abcd1234', 'abcd1235').ok).toBe(false);
  });

  it('강도는 참고용 점수를 준다', () => {
    expect(passwordStrength('').score).toBe(0);
    expect(passwordStrength('abcdefgh').score).toBeLessThanOrEqual(1);
    expect(passwordStrength('Abcdefgh1234!').score).toBe(4);
  });
});

describe('이메일', () => {
  it('형식과 길이를 검사하고 앞뒤 공백은 무시한다', () => {
    expect(validateEmail(' user@example.com ').ok).toBe(true);
    expect(validateEmail('user@example').ok).toBe(false);
    expect(validateEmail('user example.com').ok).toBe(false);
    expect(validateEmail(`${'a'.repeat(250)}@x.io`).ok).toBe(false);
  });
});

describe('캐릭터 이름', () => {
  it.each(['월백', 'Shinshi', '신시01', 'ab'])('%s 는 통과한다', (name) => {
    expect(validateCharacterName(name).ok).toBe(true);
  });

  it.each(['가', '열세글자를넘는아주긴이름이요', '월 백', '월백!', 'ㄱㄴㄷ', ''])('%s 는 거절한다', (name) => {
    expect(validateCharacterName(name).ok).toBe(false);
  });

  it('앞뒤 공백을 지우고 NFKC 정규화한 값으로 검사한다', () => {
    // 전각 영문, 숫자는 NFKC 뒤 반각이 되어 통과한다
    expect(validateCharacterName('  ＳＨＩＮ１ ').ok).toBe(true);
  });

  it('전각, 반각, 대소문자만 다른 이름은 같은 키가 된다', () => {
    expect(characterNameKey('ＳＨＩＮＳＨＩ')).toBe(characterNameKey('shinshi'));
    expect(characterNameKey(' Shinshi ')).toBe('shinshi');
  });
});
