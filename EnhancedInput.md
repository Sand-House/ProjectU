# 플레이어 입력·선입력 설계

2026-11-06 분산 구독: 중앙 PlayerStageReset을 제거했다. GameManager가 OnGameplayEnabledChanged(bool)를 발행하면 Reader는 입력/커서, Controller는 예약, Motor는 이동, Combat은 요청, 각 PlayerAction은 실행 취소를 자기 안에서 처리한다. 단계 변경의 정리도 각 컴포넌트가 OnStageStateChanged를 직접 구독한다. Mortal 기반 PlayerMortal은 체력·무적만 관리한다. 구독 원본 연결과 수명 공유는 일반 C# PlayerStageSubscription이 지원하며 일괄 정리 함수는 없다. [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md) 2.1절 참고.

2026-11-06 단계 이벤트 갱신: 진행 타입은 StageState, 이벤트는 OnStageStateChanged(StageState)다. PlayerStageReset이 구독해 정상 BeforeStage/OnStage/AfterStage의 무적·입력 정책과 클리어 정리를 처리한다. 최초/재활성화 시 현재 상태를 동기화한다. 보상 선택·사망·정지·같은 단계 재시도의 게이트는 상태 전환과 별개로 유지하며, 가짜 이벤트를 발행하거나 같은 상태에 재진입해 예약/HP/쿨타임을 반복 초기화하지 않는다. [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md) 2.1절 참고.

2026-11-06 사망 재시작: 사망 창의 재시작 버튼은 캐릭터 입력이 차단되고 timeScale=0인 상태에서도 UI EventSystem으로 선택한다. 새 씬에서는 기존 선입력·전투 요청을 이어받지 않으며, Reader의 최초 기본 발사도 버튼 해제까지 억제해 재시작 선택/홀딩이 바로 사격으로 이어지지 않게 한다. 첫 스테이지 BeforeStage에서 이동하고 새 G 상호작용으로 시작한다. [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md) 8.1절 참고.

2026-11-06 3단계 리팩토링: Reader에 G(`Interact`) 스냅샷을 추가했다. StageFlowUI가 BeforeStage의 시작, AfterStage의 보상 다시 열기, 가능한 경우 생성 재시도에 사용하며 기존 전투 키는 유지한다. BeforeStage와 AfterStage는 자유 조작이고 더미 보상 창이 열린 동안만 Reader·Combatant의 게임플레이 입력을 차단한다. UI 선택은 별도 InputSystemUIInputModule이 처리하며 timeScale은 유지한다. 완료 후 UI를 닫고 입력을 복구해 BeforeStage에서 새 시작 상호작용을 기다린다. 초기화·상태·선택 취소 계약은 [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md)를 따른다. 아래 첫 진행 연결의 설명보다 이 정책을 우선한다.

2026-11-06 스테이지 진행 갱신: `PlayerStageReset.SetGameplayEnabled(false)`가 Reader 게임 입력 차단·커서 해제, Controller.ClearBufferedInput과 Combat.ClearRequests를 통해 예약·명령을 즉시 폐기한다. Controller는 사망·Combatant.IsGameplayBlocked 상태에서 이동·점프 명령도 보내지 않는다. Reader.SetGameplayBlocked는 재개 시 총을 누르고 있던 입력이 보상 선택 클릭과 겹치지 않도록 기본 발사를 해제까지 억제한다. 스테이지 종료의 이동·액션·회복·보상 대기 계약은 [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md)를 참고한다.

2026-11-01 공통 액션 갱신: PlayerAction은 ActionBase를 상속한다. 선입력·홀딩·슬롯·PlayerActionStage는 플레이어 전용으로 유지하고 공통 타이밍·조건·이벤트는 ActionBase로 옮겼다. 공통 실행 타입 이름은 ActionContext·ActionPhase·ActionResult·ActionCancelReason이다. 적의 EnemyAction은 키 입력과 선입력을 사용하지 않으며 EnemyCombat이 요청한다. 기존 입력 규칙은 유지하고 이번 회귀 로그는 `Logs/Enemy_20261101/`에 기록한다. 적 설계는 [Enemy.md](Enemy.md)를 참고한다.

기준 날짜: **2026-10-31, 공통 접지·이동 제한 반영**. PlayerAction.RequiresGrounded가 수신·시작·효과 실행 시의 접지를 검사하며 CanMove가 행동 중 일반 이동·점프 허용 여부를 정한다. 상세 필드·상속·이번 검증은 [PlayerAction 설계](PlayerAction%20설계.md) 11절을 참고한다. 아래 날짜별 검증은 해당 작업 당시의 기록이다.

## 1. 클래스의 역할과 책임

| 클래스 | 책임 | 상태 원본 |
|---|---|---|
| ButtonInputState / PlayerInputReader | 프레임별 Pressed·Held·Released, 커서·포커스·입력 가능 여부 | 현재 입력 스냅샷 |
| PlayerController | 카메라 기준 이동·점프 명령, 전투 선택·우선순위·한 개 예약·만료·반복·자동 장전 요청 | maxInputBufferTime·예약 슬롯·만료 시각 |
| PlayerCombat | 슬롯/액션 연결, 실행 가능 조회·한 프레임 명령·조준/배율·탄창·외부 이벤트 | 한 프레임 명령·탄창 |
| PlayerAction / PlayerAttackAction | 공통 접지·이동 허용·타이밍·단계·검증·취소 / 공격 실행 어댑터 | 액션별 접지 필요·이동 허용·선딜·유지·후딜·쿨타임·반복 |
| Soilder…Action | 기본·보조 발사·스킬·궁극기·대시·장전 구현 | 개별 조건·자원·효과와 장전 작업량 |
| PlayerActionBinding | PlayerActionSlot과 PlayerAction 연결 | 씬/프리팹 참조 |
| Attack / Combatant | 기본 Origin 보관·동기 공격 효과 / 같은 소유자의 액션 제한 집계·진영·생존 | 기본 Origin·공격 결과·공통 제한 조회 |
| PlayerMotor | 실제 이동·점프·중력·충돌·대시 이동 | 접지·속도·대시 이동 상태 |
| PlayerStamina / PlayerStat | 스태미나·능력치 | 자원·강화 원본 |
| PlayerAim / PlayerCamera / PlayerAnimation | 시각 조준·시점·이벤트 기반 애니메이션 | 시각 자세·시점 |

입력 선택과 예약은 Controller 한 곳에서 담당한다. 액션은 Reader를 직접 읽지 않는다. 별도 PlayerActionController와 두 번째 선입력 저장소는 없다. 기존 PlayerAction enum·AttackSlot은 PlayerActionSlot으로 통합했다.

2026-10-31 로그 변경: Controller의 Pressed·Held·Released 출력을 모두 제거했다. logInput·heldLogInterval·전용 타이머·Handle 메서드와 씬의 해당 저장값, 검증기의 옛 로그 설정 접근도 제거했다. Skill2·Ultimate의 실행 성공 로그와 버튼 스냅샷·해제 전달·선입력·홀딩 반복 동작은 유지한다.

## 2. 선입력 규칙

1. 다음 행동 한 개만 저장한다.
2. 수신 시 공격·대시 딜레이 외 자격을 만족해야 한다. 접지·스태미나·쿨타임·탄약·장전·활성·생존·공격 구성·Origin을 확인하며 검사 자체는 소모·실행을 만들지 않는다.
3. 유효하지 않은 새 입력은 예약 생성·교체·만료 연장에 참여하지 않는다.
4. 유효한 대시는 일반 예약을 교체하고, 일반 입력·Held는 예약 대시를 교체하거나 연장하지 못한다.
5. 선택한 슬롯을 막는 딜레이가 끝나는 첫 실행 기회에 조건을 다시 검사한다. 시도한 예약은 성공·실패와 관계없이 소비하며 자원 회복·착지·쿨타임 종료를 기다리지 않는다.
6. 일반 예약은 더 나중의 유효한 Pressed로 교체한다. 동일 프레임은 Dash → 비기본 공격 바인딩 순서 → Reload → PrimaryFire다.
7. 예약을 시도한 프레임에는 다른 홀딩 행동을 추가 실행하지 않는다. 반복·자동 장전은 다음 프레임부터 다시 시도할 수 있다.

CanBuffer는 현재 딜레이만 제외하고, CanStart는 현재 행동 제한도 포함한다. 장전 중 두 발사 금지는 두 검사에 모두 적용한다. RequiresGrounded=true인 액션은 PlayerAction에서 수신·시작·선딜 완료 시 모두 접지를 검사한다. 현재 대시와 Skill2가 해당하며 공중 입력은 예약하지 않는다. 예약 후 공중이 되면 첫 실행 기회에 폐기하고, 선딜 중 공중이 되면 실행에 실패한다.

예약에는 슬롯과 만료 시각만 둔다. 공격은 최신 Origin·조준점, 대시는 실행 프레임의 이동 방향 또는 카메라 Yaw 전방을 사용한다. 실제 대시 시작 이후 방향은 고정한다.

## 3. 최대 유지 시간

설정은 SampleScene의 PlayerController.maxInputBufferTime이며 현재 **1초**다. Time.timeAsDouble로 만료를 계산하며 한도 이상이면 버린다. 0은 딜레이 중 예약을 비활성화하되 실행 가능한 새 누름은 허용한다. 음수·NaN·Infinity는 0으로 정리한다. BufferedAction과 BufferRemaining은 읽기 전용 진단 값이다.

## 4. 이동·점프·반복·유지·장전

| 대상 | 정책 |
|---|---|
| WASD·시점 | 현재 입력 사용. 예약하지 않음. CanMove=false 액션 중 일반 이동은 막고 시점은 유지 |
| Jump.Pressed | CanJump·접지로 즉시 판단. 공중·실제 대시 이동·CanMove=false 액션 중 입력은 예약하지 않음 |
| 전투 Pressed | 유효한 입력만 한 개 예약하거나 즉시 한 번 시도 |
| RepeatWhileHeld | PlayerAction의 설정. 예약 다음에 실행 가능한 현재 Held를 반복 요청 |
| 유지형 액션 | 한 번 시작해 Active로 유지. 다른 행동 잠금 중에도 Release를 전달 |
| 유지형 예약의 버튼 해제 | 예약 폐기. 일반 발사·수류탄의 짧은 누름은 유지 |
| 0발 자동 장전 | 예약·비기본 반복 다음에 매 프레임 다시 요청 |
| 장전 중 기본·보조 발사 | 수신·실행 모두 거절 |
| 장전 중 대시·스킬 | 대시 시작 성공 / 스킬 수락 시 보충 없이 취소 |

장전 진행은 SoilderReloadAction, 탄창은 PlayerCombat, 스태미나 소모 정책은 SoilderDashAction이 담당한다. RepeatWhileHeld를 자동 장전 조건이나 줌 유지 여부로 사용하지 않는다.

SampleScene의 F는 지면 전용·이동 허용 Skill2, Q는 공중 사용 가능·이동 금지 Ultimate다. 두 액션은 선딜 0.5초 후 실행 로그와 ActionExecuted를 한 번 발생시키고 후딜 1초를 적용한다. AttackExecuted·피해·탄약 소모는 없으며 수락된 스킬의 장전 취소 규칙은 유지한다. 예약된 Ultimate는 실제 수락 전까지 이동을 제한하지 않는다.

## 5. 조작 중단과 실행 순서

정지·커서 해제·포커스 상실·Reader/Controller 비활성화·사망은 예약과 미처리 명령을 지운다. Reader가 Released를 합성하지 않더라도 유지형 액션은 중단 통지로 정리한다. 기존 공격·장전의 일시정지 규칙은 유지한다.

| 순서 | 실행 |
|---|---|
| Update -200 / -100 / -75 | Reader / Camera Yaw / Stamina 회복 |
| Update -50 | Controller의 입력·해제·예약·선택, PlayerCombat 경유 대시 시작 |
| Update 0 / 50 | Motor 실제 이동 / Animation 갱신 |
| LateUpdate -100 / 75 | Camera 배치 / Aim 총구·상체·IK 보정 |
| LateUpdate 100 | PlayerCombat 명령 소비·조준/배율 준비·ReloadAction 작업 |
| LateUpdate 110 | PlayerAction 선딜 완료, Attack 동기 호출·실행 결과 확정 |

PlayerCombat의 지연 명령은 전달 프레임에 한 번 소비한다. Attack에 독립 타이머·예약·LateUpdate는 없다. 이동·점프 조작과 대시 이동·후딜의 허용 범위는 기존 규칙을 유지한다.

## 6. 검증

이 절은 선입력 구현 당시의 검증 기록이다. 이후 같은 날짜에 진행한 클래스 이름 변경은 7절, 접지 중 대시 시작 조건과 이번 검증은 8절에 별도로 기록한다.

새 검증 진입점은 `ProjectU.Editor.EnhancedInputValidation.Run()` / 메뉴 **ProjectU → Validation → Validate Enhanced Input**이다. 저장된 SampleScene을 열어 합성 스냅샷으로 실제 Controller·Motor·PlayerCombat·Attack·장전을 검사하며 씬·공유 에셋을 저장하지 않는다.

검사 대상: 선딜 중 대시 예약과 홀딩 사격 우선권, 동시 입력, 다섯 공격 슬롯의 한 번 실행, 일반 예약 교체·R, 대시 이동/후딜 중 입력, 시간 만료·0초, 실행 시점 자원/쿨타임 실패 후 폐기, Held 비예약, 이동·점프 유지, 정지·커서·비활성·포커스·사망 시 폐기.

기존 PlayerActionDelayValidation·PlayerDashValidation·PlayerReloadValidation은 maxInputBufferTime을 0으로 설정해 즉시 차단·기존 실행 기능을 독립 검증한다. PlayerAttackValidation·애니메이션·스태미나 검증도 선입력 시간을 0으로 두고 기존 기능을 검사한다. 다른 캐릭터 검증에는 입력 선택을 담당하는 Controller를 연결한다. 배치 모드의 창 포커스·커서와 분리해 검사하기 위해 버튼과 IsGameplayInputEnabled 스냅샷을 함께 주입한다.

**2026-10-30 이번에 직접 실행한 결과:** Unity `6000.3.25f1`로 `Logs/EnhancedInput_20261030/Project` 복사본에서 아래 7개 검증을 실행했고 모두 종료 코드 0을 확인했다.

| 최종 로그 | 결과 |
|---|---|
| `EnhancedInputValidation.Final.log` | 새 선입력 51개 통과 |
| `PlayerActionDelayValidation.Final.log` | 기존 행동 제한·이동·점프·직접 API 110개 통과 |
| `PlayerDashValidation.Final.log` | 대시 방향·거리·충돌·자원·후딜·취소 63개 통과 |
| `PlayerReloadValidation.Final.log` | 탄약·장전·배속·취소·자동 재시도 40개 통과 |
| `PlayerAttackValidation.Final.log` | 피해 30/50/100·입력·바인딩·다른 캐릭터·반복 정책 50개 통과 |
| `PlayerAnimationValidation.Final.log` | 17발·이동·점프·상체·양팔 IK·장전·투척·정지 통과. 총구 최대 방향 오차 약 0.028도 |
| `PlayerStaminaValidation.Final.log` | 능력치·Shift·소모·회복·정지·이벤트·이동/대시 원본 통과 |

종료 코드는 같은 폴더의 `Results.Final.json`, 원본/복사본 Assets 191개 SHA256 일치는 `AssetHashes.Final.json`에 기록했다. `DotnetBuild.Final.log`에서 최종 런타임·에디터 C# 빌드 경고 0·오류 0을 확인했다. 씬 변경은 Controller의 maxInputBufferTime: 1 저장만이며 기존 공격 데이터·Muzzle·프리팹·GUID는 유지했다.

배치 모드의 포커스·커서로 인해 입력 활성 스냅샷을 함께 주입하도록 검증기를 보완했고, 개발 중 Reader의 중복 OnDisable 컴파일 오류를 수정했다. 기존 공격 검증에서 발견한 쿨타임 중 보조 홀딩의 기본 발사 차단은 CanAttack(slot)으로 실행 가능한 반복 후보를 선택하도록 수정했다. Initial~Fourth 및 `BeforeHeldEligibility/`의 중간 실패·통과는 최종 결과와 구분해 보존한다.

Unity 시작 시 SearchDatabase 인덱스 예외와 라이선스 access-token 갱신 메시지는 검증 통과·종료 코드와 구분한다. 이번에는 실제 키·마우스 수동 플레이, 이미지 렌더링, 공통 Attack·투사체·Mortal 독립 검증을 실행하지 않았다. 최대 선입력 시간의 최종 조작감, Skill2·궁극기 효과·남은 애니메이션·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다.

## 7. 2026-10-30 기능 컴포넌트 이름 변경

입력을 해석하고 행동을 선택하는 `PlayerController`만 Controller 접미사를 사용한다. 개별 플레이어의 기능 컴포넌트는 담당 기능을 나타내는 명사로 명명한다.

| 이전 클래스·파일명 | 현재 클래스·파일명 |
|---|---|
| `PlayerAimController` | `PlayerAim` / `Assets/Scripts/Player/PlayerAim.cs` |
| `PlayerAnimationController` | `PlayerAnimation` / `Assets/Scripts/Player/PlayerAnimation.cs` |
| `PlayerAttackController` | `PlayerCombat` / `Assets/Scripts/Player/PlayerCombat.cs` |
| `PlayerCameraController` | `PlayerCamera` / `Assets/Scripts/Player/PlayerCamera.cs` |

네 파일과 각 meta를 함께 이동하고 RequireComponent·GetComponent·타입/리플렉션 참조·오류 메시지·검증 도구와 SampleScene의 m_EditorClassIdentifier를 갱신했다. meta 전체·GUID, 직렬화 필드명, 씬의 컴포넌트 ID와 참조·설정, 실행 순서와 기능은 유지했다. 기존 입력 선입력·우선순위·최대 시간도 유지한다. 과거 날짜별 상세 기록에 남은 이전 이름은 당시 이름이다.

이번에 직접 실행한 검증은 `Logs/PlayerComponentRename_20261030/Project` 복사본의 EnhancedInputValidation 51개·PlayerAttackValidation 50개·PlayerAnimationValidation(17발·이동·점프·상체·조준·총구·양팔 IK·장전·투척·정지)이며 모두 종료 코드 0이다. 런타임·에디터 C# 빌드는 경고 0·오류 0이다. Assets 191개가 네 타입명/파일명 치환만 수행한 예상 결과와 일치하고, 실제 검증 복사본과도 SHA256이 모두 일치한다. 소스·씬에 이전 타입명 참조가 없고 런타임 Controller 클래스가 PlayerController 하나임을 확인했다.

로그·종료 코드는 `Logs/PlayerComponentRename_20261030/`의 각 검증 로그·Results.json·DotnetBuild.log, 변경 전후·복사본 무결성은 Assets.Before.json·RenameIntegrity.json·AssetIntegrity.Final.json에 있다. Unity Search의 기존 시작 인덱스 예외는 기능 검증 통과와 구분한다. 이번에는 수동 키·마우스 플레이·이미지 렌더링과 행동 제한·대시·장전·스태미나·공통 Attack/투사체/Mortal 독립 검증을 다시 실행하지 않았다. 6절의 이전 7종 검증과 구분한다. 조작감 조정·스킬 효과·남은 애니메이션·적 AI·사망 행동·반복 전투 흐름은 기존 후속 작업으로 유지한다.

## 8. 2026-10-30 접지 중에만 대시 시작

PlayerMotor.CanDash에 IsGrounded 조건을 추가했다. Controller가 스태미나를 소모하기 전과 직접 TryDash 호출에서 동일한 조건을 검사한다. 상승·낙하 중 입력은 대시·스태미나 소모·대시 이벤트를 발생시키지 않는다. 예약 대시도 첫 실행 기회에 공중이면 소비·폐기하고 착지 후 자동 실행하지 않는다. 대시 시작 후의 이동·중력·후딜과 이동·점프·공격 규칙은 유지한다.

이번에 원본 프로젝트에서 Unity 6000.3.25f1로 PlayerDashValidation **70개**, EnhancedInputValidation **53개**, PlayerActionDelayValidation **110개**를 직접 실행해 모두 통과했다. 상승·낙하 중 직접/입력 거절, 스태미나 미소모, 착지 시 홀딩 재실행 방지와 새 누름 성공, 공중에서 공격 잠금이 풀린 예약의 폐기를 확인했다. 기존 대시 방향·거리·충돌·후딜·취소·정지와 공격 중 이동·점프도 통과했다. 위치 초기화 검증은 CharacterController.Move로 접지를 갱신하고, 공격 후 점프 중 CanDash 기대값을 새 규칙에 맞췄다. Unity 컴파일 오류·경고와 git diff --check 오류는 없었으며 씬·프리팹·공격 데이터·meta는 변경하지 않았다. 로그는 `Logs/GroundedDash_20261030/`의 각 검증명.log다. Initial.log는 샌드박스의 라이선스 초기화 대기로 중단한 실행이며 최종 기능 검증 결과와 구분한다. 실제 키·마우스 수동 플레이와 이미지 렌더링은 이번에 수행하지 않았다. 조작감·남은 애니메이션·스킬 효과·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다.

## 9. 2026-10-30 입력 수신 시 실행 자격 검사

8절의 접지 수정은 실행 시점만 검사해, 공중에서 공격 딜레이 중 누른 대시가 예약된 뒤 딜레이 종료 전에 착지하면 실행되는 경로가 남아 있었다. 이번에는 긴 공격 후딜 중 점프·공중 대시 누름·착지·후딜 종료 순서로 재현했다. 수정 전 Reproduction.log는 buffer=Dash로 실패했고 종료 코드는 1이다.

Controller.CanAcceptAction이 입력 수신 시 자격을 검사한다. Motor·PlayerCombat·Attack의 내부 IgnoringActionLock 조회는 공격·대시 딜레이만 제외한 공통 실행 조건을 재사용한다. 대시는 접지·유효 설정·스태미나 활성/잔량, 공격은 활성·생존·데이터·쿨타임·Origin·탄약·장전·투사체 구성, 장전은 덜 찬 탄창·진행 상태를 확인한다. 조건이 맞는 새 입력만 기존 우선순위에 참여하며 실패한 입력은 예약 생성·교체·유효 시간 연장을 하지 않는다. 자원은 실행 성공 시에만 소모하고, 예약 후 조건이 바뀌면 기존 실행 재검사에서 한 번 실패한 뒤 폐기한다. 기존 한 개 예약·대시 우선·최대 유지 시간·중단 폐기·홀딩 및 자동 장전 재시도 규칙은 유지한다.

이번에 원본 프로젝트의 Unity 6000.3.25f1에서 EnhancedInputValidation 85개, AttackValidation 68개, PlayerAttackValidation 50개, PlayerDashValidation 70개, PlayerReloadValidation 40개, PlayerActionDelayValidation 110개를 직접 실행해 총 423개와 종료 코드 0을 확인했다. 공중 입력 후 딜레이 종료 전 착지, 스태미나 부족/비활성·0발·가득 찬 탄창·비활성 공격·없는 Origin·투사체 구성 실패·쿨타임 입력의 기존 예약 보호와 이후 조건 충족 시 재실행 방지, 예약 후 자원·쿨타임 변경에 대한 실행 재검사를 포함한다. Unity C# 컴파일 오류·경고는 없었으며 씬·프리팹·공격 에셋·meta·실행 순서는 유지했다. 로그·결과는 Logs/InputEligibility_20261030/의 각 검증명.log, Results.json, RegressionResults.json에 있다. 실제 키·마우스 수동 플레이·이미지 렌더링·애니메이션/스태미나/투사체/Mortal 독립 검증은 이번에 다시 수행하지 않았다. 조작감 조정·남은 애니메이션·스킬 효과·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다.

## 10. 2026-10-30 PlayerAction·Soilder 전투 액션 구현과 이번 검증

이번 원본 프로젝트의 Unity **6000.3.25f1**에서 다음 9종 검증이 모두 종료 코드 0으로 통과했다. 아래 수치형 검사 **466개**와 애니메이션·스태미나 검증은 이번 직접 실행 결과다.

| 검증 | 결과 | 로그 |
|---|---|---|
| PlayerActionValidation | 43개 | PlayerActionValidation.Final.log |
| AttackValidation | 68개 | AttackValidation.Regression.log |
| PlayerAttackValidation | 50개 | PlayerAttackValidation.Final.log |
| PlayerDashValidation | 70개 | PlayerDashValidation.Final.log |
| PlayerReloadValidation | 40개 | PlayerReloadValidation.Final.log |
| PlayerActionDelayValidation | 110개 | PlayerActionDelayValidation.Regression.log |
| EnhancedInputValidation | 85개 | EnhancedInputValidation.Final.log |
| PlayerAnimationValidation | 17발·이동·점프·사격·장전·투척·양팔 IK·벽 보정·정지 | PlayerAnimationValidation.Regression.log |
| PlayerStaminaValidation | 능력치·Shift·소모·회복·정지·이벤트·이동/대시 원본 | PlayerStaminaValidation.Regression.log |

로그는 `Logs/PlayerAction_20261030/`에 있다. 최종 결과 목록은 `Results.Final.json`, C# 런타임·에디터 빌드는 `DotnetBuild.Final.log`의 경고 0·오류 0이다. 애니메이션 검증의 총구/공격 방향 최대 오차는 약 0.028도, 비차단 총구~실제 원점 거리는 0m다.

`Integrity.Final.json`에서 원본 meta 107개(바인딩 이름 이동 포함)의 SHA256 동일, 변경 대상 외 에셋 36개 동일, 여섯 공격 에셋의 네 요청 필드 외 내용 동일, 씬의 의도된 액션 추가·바인딩·필드 이동 외 내용 동일, 새 액션 설정 28개·참조·GUID 중복 없음·공통 Motor/Combat의 Soilder 구체 타입 의존 없음·구형 런타임 API 제거를 확인했다. `git diff --check`도 통과했다.

초기 선입력 실행의 옛 Attack 쿨타임 필드 접근은 검증기를 새 액션 필드로 변경해 해결했다. 최초 회귀에서 발견한 Motor 비활성화 후 대시 후딜 잠금 잔류는 의존 기능 종료 시 액션 잠금을 정리하도록 수정했고, 대시·장전·선입력·새 액션을 다시 확인했다. 기존 직접 Attack 요청을 액션 요청으로 옮기면서 장전 중 발사는 수락 전 거절하도록 기대값을 맞췄다. Initial·Regression의 중간 실패와 최종 통과를 구분한다.

실제 키·마우스 수동 플레이·이미지 렌더링·공통 Mortal/Projectile 독립 검증은 이번에 실행하지 않았다. Skill2·Ultimate 효과, 실제 줌·버프·추가 애니메이션·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다.

## 2026-10-30: 기본 Origin의 Attack 이관

기본 origin 직렬화 필드를 PlayerAttackAction에서 Attack으로 옮겼습니다. Inspector에서는 액션에 Attack을, Attack 컴포넌트에 Origin을 연결합니다. PlayerAttackAction.Origin과 PlayerActionBinding.Origin은 Attack.Origin을 조회하며, SampleScene의 세 공격은 기존 Muzzle을 그대로 사용합니다. 요청별 Transform·고정 좌표 API, 선딜 후 최신 위치 조회·삭제 시 실패·벽 보정을 유지합니다. 입력 선택·선입력·탄약·장전·공격 시간의 책임은 유지합니다.

이번 원본 Unity 6000.3.25f1에서 공통 공격 75개·플레이어 공격 50개·장전 40개·선입력 85개, 총 250개와 애니메이션(17발·이동·점프·IK·장전·투척·벽·정지)을 직접 실행해 통과했습니다. .NET 런타임·에디터 빌드는 경고 0·오류 0이며 씬 변경 범위·meta 보존과 git diff --check도 확인했습니다. 이번 결과는 앞선 PlayerAction 리팩토링 검증과 별도이며 로그는 Logs/AttackOrigin_20261030/, 상세 결과·실행 제약은 [PlayerAction 설계](PlayerAction%20설계.md) 10절에 있습니다. 실제 키·마우스 수동 플레이·이미지 렌더링은 이번에 수행하지 않았습니다. Skill2·Ultimate 효과, 손 투척 지점·추가 애니메이션·적 AI·사망 행동·반복 전투 흐름은 후속 작업입니다.
