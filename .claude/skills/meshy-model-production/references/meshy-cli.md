# Meshy CLI 실행과 복구

2026-10-03 설치 CLI 0.3.0의 로컬 도움말에서 확인한 예시다. 실행 시 설치 버전의 도움말을 우선하며 모델 버전·요금·옵션을 과거 값으로 고정하지 않는다.

## 실행 전

```powershell
meshy --version
meshy --help
meshy make --help
meshy resources
```

인증이 필요한 실제 제작 때 `meshy auth status`, `meshy balance`로 가용 상태를 확인한다. 키 파일이나 전체 환경 변수를 출력하지 않는다. 인증이 없으면 사용자 인증이 필요한 단계만 알리고 로컬 가공을 계속한다. 임의 설치·업데이트나 결제를 하지 않는다.

## 원본 생성 예시

아래 경로와 예산 숫자는 실제 입력·승인 범위로 치환한다. 출력 폴더는 기존 결과를 덮지 않는 Assets 밖 Git 제외 폴더다.

```powershell
meshy make './reference.png' --dry-run
meshy make './reference.png' --max-credits 30 -o './work/asset/raw/'
```

`--dry-run`은 계획·예상 비용만 출력하고 API를 호출하지 않는다. `--max-credits`는 예상 비용이 한도를 넘으면 시작을 거부하며 누적 지출 관리나 실제 청구 확정값이 아니다. 예시의 30은 기본 예산이 아니다. 이미 허용된 제작 범위와 예상 비용을 비교하고, 범위를 넘는 추가 생성·재시도는 실행 전에 필요한 결정을 받는다.

문자열 프롬프트는 preview→refine의 두 단계, 단일 이미지는 textured image-to-3d 흐름이다. 비동기 `--async`는 첫 단계만 제출하고 나머지가 pending일 수 있다. 모델/리메시/다각도/리깅 등 상세 제어는 `meshy resources`로 실제 하위 명령을 찾고 해당 `--help`를 확인한다. `make`에 확인되지 않은 옵션을 붙이지 않는다.

작업을 제출하자마자 유형과 ID를 기록한다. 텍스트 preview 성공은 refine 성공이나 최종 모델 다운로드 완료가 아니다. 출력 `meta.json`과 실제 모델·텍스처 파일을 확인한다. 공유 기록에는 인증값·서명된 다운로드 URL을 복사하지 않는다.

## 중단 복구

image-to-3d 예시(실제 ID로 치환):

```powershell
meshy image-to-3d get TASK_ID
meshy image-to-3d wait TASK_ID --timeout 45
```

다른 유형은 해당 resource의 get/wait 도움말을 확인한다. `get`의 종료 코드가 0이어도 작업은 실패·진행 중일 수 있으므로 응답 status를 확인한다. 대기는 제한된 구간으로 나누고 장시간 작업에는 진행 상황을 알린다.

타임아웃·네트워크 오류는 서버 생성 실패를 뜻하지 않는다. 동일 ID 조회를 우선하고 SUCCEEDED면 기존 결과 다운로드 경로를 도움말에서 확인해 복구한다. ID를 받기 전에 응답을 잃었으면 작업 목록·제출 시각·입력으로 기존 요청을 확인한다. 중복 여부가 불명확하면 새 create를 자동 실행하지 않는다.

FAILED면 오류와 입력 보정 가능성을 확인한다. 동일 입력을 무한 재시도하지 않으며 재생성 횟수·실제 비용을 누적 기록한다. 실제 비용을 API가 주지 않으면 미확인으로 남긴다. 다른 작업도 실행되는 계정의 잔액 차이를 이 모델 비용으로 단정하지 않는다.

## 확인 근거

- 로컬 `meshy --help`, `make --help`, `image-to-3d get --help`, `image-to-3d wait --help`.
- [Meshy 공식 문서](https://docs.meshy.ai/): 설치 도움말로 해소되지 않는 API 동작을 확인할 때 사용.
- [Claude Code 스킬 문서](https://code.claude.com/docs/en/skills): `.claude/skills/{이름}/SKILL.md` 형식과 직접 호출.
