# ProjectU

## 플레이어 조작 프로토타입

`Assets/Scenes/SampleScene.unity`를 열고 Play를 누릅니다. 40×40m 평면,
경계 벽, 전방 표시가 있는 파란 캡슐에 조작 스크립트가 연결되어 있습니다.

| 입력 | 동작 |
|---|---|
| WASD | 카메라 수평 방향 기준 이동, 옆걸음·뒷걸음 |
| 마우스 | 시점 회전, 캐릭터 수평 방향 회전 |
| Space | 지상에서 점프 |
| Q / R | Ultimate / Reload 입력 로그 |
| 왼쪽·오른쪽 Shift / E | Skill1 / Skill2 입력 로그 |
| 마우스 좌클릭 / 우클릭 | PrimaryFire / SecondaryFire 입력 로그 |
| Esc | 커서 잠금 해제, 조작 입력 중단 |
| Game 뷰에서 좌클릭 | 커서 잠금 및 조작 재개 |

Inspector 조정 항목:

- **Player → Player Motor**: Move Speed(m/s), Jump Height(m), Gravity(m/s²).
- **Main Camera → Player Camera Controller**: Mouse Sensitivity(도/픽셀), Invert Y,
  Minimum/Maximum Pitch, Initial Pitch, Target Height, Distance, Shoulder Offset,
  Collision Radius, Obstruction Mask.
- **Player → Character Controller**: 경사 제한, 계단 높이, 충돌체 크기.
- **Player → Player Controller**: Log Input, Held Log Interval(홀딩 로그 간격, 기본 0.5초).

Play 중 변경한 값은 Play 종료 시 복원됩니다. 지속할 값은 Edit 모드에서 설정합니다.
기본값은 이동 속도 6m/s, 점프 높이 1.5m, 중력 25m/s²입니다.

입력은 `PlayerInputReader`의 독립 InputActionMap에서 관리합니다.
기존 템플릿 `InputSystem_Actions` 에셋은 사용하지 않습니다.
Input Action 이름과 로그의 액션명·상태명은 PascalCase(`Skill1`, `PrimaryFire`, `Pressed`)로
통일합니다. C# 비공개 필드와 지역 변수는 camelCase(`skill1`, `primaryFire`)를 사용합니다.
이동·시점은 매 프레임 Vector2로, 모든 게임플레이 버튼은 `ButtonInputState`의
`Pressed`(이번 프레임 누름), `Held`(현재 누르는 중), `Released`(이번 프레임 해제)로 제공합니다.
`Jump`, `Ultimate`, `Reload`, `Skill1`, `Skill2`, `PrimaryFire`, `SecondaryFire`에
동일한 상태 형식을 사용합니다. 이동은 WASD 홀딩 중 계속되고 점프는 `Jump.Pressed`만
사용하므로 Space를 계속 눌러도 착지 후 자동으로 다시 점프하지 않습니다.
입력 로그는 `PlayerController`에서 출력하며, Reader는 로그를 출력하지 않습니다.
`PlayerController`는 카메라 기준 입력을 월드 이동 방향으로 변환해
`PlayerMotor.SetMoveCommand()`와 `RequestJump()`를 호출합니다.
`PlayerMotor`는 입력·카메라 참조 없이 이동, 회전, 중력, 점프, 충돌을 처리합니다.
명령은 한 프레임만 유효하므로 Controller를 끄면 이전 이동·점프 명령이 남지 않으며,
Motor가 켜져 있으면 중력 처리는 계속됩니다. 실행 순서는 Reader → Camera → Controller → Motor입니다.
누름·해제는 한 번씩, 홀딩은 설정한 간격마다 출력합니다. 홀딩 상태 자체는 로그 간격과
관계없이 매 프레임 제공됩니다. 포커스 상실·커서 해제·일시정지 시 입력 상태를 초기화합니다.
커서 재잠금에 사용한 좌클릭은 버튼을 놓을 때까지 PrimaryFire에서 제외합니다.
카메라 충돌 검사는 Default 레이어를 대상으로 하며, 플레이어는 Ignore Raycast
레이어를 사용합니다. 새 장애물은 Default 레이어와 Collider를 지정합니다.

이번 단계에서 Q/E/R/Shift와 좌·우클릭은 로그만 출력합니다. 사격, 재장전, 대시,
수류탄, 궁극기와 우클릭 정밀 조준은 아직 구현하지 않았습니다.

검증: Unity 6000.3.25f1 배치 실행에서 스크립트 컴파일, 저장된 씬의
컴포넌트·참조 연결, 누락 스크립트 유무, 플레이어 아래 바닥 충돌체를 확인했습니다.
실제 마우스·키보드 플레이 감각은 Game 뷰에서 확인합니다.

입력 회귀 검증은 Unity 배치 모드에서 `-executeMethod ProjectU.Editor.PlayerInputValidation.Run`으로
실행합니다(`-quit` 제외, 테스트 완료 후 자동 종료). 합성 키보드·마우스를 사용해
누름/홀딩/해제, 이동과 스킬 동시 입력, 액션 재활성화 시 홀딩 복원을 확인합니다.
