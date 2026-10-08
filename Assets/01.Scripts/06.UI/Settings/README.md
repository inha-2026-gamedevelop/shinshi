# 환경설정 UI

- 개발 씬: `Assets/00.Scenes/Dev_HUD/Dev_Setting.unity`
- 재사용 프리팹: `Assets/02.Prefab/UI/Settings/PF_SettingsWindow.prefab`
- `SettingsView.Show()`로 열고, 적용은 저장, 확인은 저장 후 닫기, 취소와 ESC는 마지막 적용값으로 오디오를 복구한다.
- 화면 모드와 해상도는 Player에서 `Screen.SetResolution`으로 적용하고 Unity의 화면 설정 저장을 사용한다. 에디터에서는 게임 뷰 크기를 바꾸지 않는다.
- `Graphics.VSync`는 기본 1, `Graphics.FrameCap`은 기본 -1이다. 수직 동기화가 켜져 있으면 프레임 제한 입력은 비활성화된다.
- 마스터/BGM/SFX/음소거는 PlayerPrefs `SoundVolume`의 JSON 한 개로 저장한다. 마스터와 음소거는 `AudioListener.volume`, 각 채널은 `SettingsAudioChannel`을 통해 적용한다.
- 실제 BGM/SFX AudioSource에 `SettingsAudioChannel`을 붙이고 `_source`와 채널을 지정해야 한다. 개발 씬의 빈 오디오 소스는 수치 검증용이다.
- UI 이벤트 시스템과 입력 모듈은 통합 씬의 기존 구성을 사용한다. 개발 씬의 열기 버튼과 오디오 예제는 프리팹에 포함되지 않는다.
- 배치 재생성 메뉴는 `Shinshi/UI/설정 창 만들기`이며 `Dev_Setting` 편집 모드에서만 실행된다. 원본 JSON과 개인 렌더/검증 기록은 Git 제외 개인 기록 폴더에 보관한다.

2026-10-08 검증: Unity 6000.4.7f1 Play Mode에서 분류 전환, 드롭다운, 슬라이더 클릭/드래그, 5% 증감, 음소거, 적용/저장/재개, 취소 복원, 초기화를 확인했다. 프리팹 누락 스크립트와 표시 텍스트 넘침은 0이다. Player 빌드의 화면 모드/해상도 변경과 실제 게임 오디오 재생은 미검증이다.
