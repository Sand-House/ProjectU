# ProjectU 프로젝트 지침

최신 플레이어 시작 HP 기준: **2026-11-02, 적 능력치 작업 후속**. PlayerStat은 Mortal을 필수 컴포넌트로 요구하고 Awake(-150)에서 `Mortal.TryInitializeHealth(MaxHealth)`를 한 번 호출한다. 시작 HP 설정은 PlayerStat.MaxHealth, 현재 HP·피해·사망은 Mortal이 담당한다. Edit 모드에서 Max Health를 편집하면 다음 생성 시 적용하며 SampleScene 기본값은 100이다. 재활성화·실행 중 MaxHealth 변경은 현재 HP를 회복하거나 부활시키지 않는다. 런타임 최대 HP 변경 시 현재 HP 보정·회복·부활 정책은 후속 작업이다. 아래 이전 기록의 플레이어 최대 체력 미연결은 변경 전 상태다.

플레이어 시작 HP의 이번 직접 검증: **2026-11-02**, Unity 6000.3.25f1에서 PlayerAttackValidation **58개**(기존 50개 + HP 연결 8개), PlayerStaminaValidation, MortalValidation을 직접 실행해 세 종류 모두 종료 코드 0을 확인했다. Mortal 자동 요구, 서로 다른 저장값에서 MaxHealth 250 우선 적용, 피해/무적/이벤트 유지, 개체·컴포넌트 재활성화 시 회복 방지, 실행 중 MaxHealth 변경 시 HP 유지, 사망 후 부활·중복 사망 방지를 검사했다. .NET 런타임·에디터 빌드 경고·오류 0과 git diff --check를 확인했다. 로그는 `Logs/PlayerHealth_20261102/`이며 앞선 적 능력치의 220개 검증과 별도다. 실제 키·마우스 수동 플레이·이미지 렌더링과 다른 독립 검증은 재실행하지 않았다.

최신 적 능력치 기준: **2026-11-02**. `EnemyStatData` ScriptableObject에 최대 HP·이동속도·공격력 배율·공격속도 배율을 정의하고 `EnemyStat` 컴포넌트의 Data에 연결한다. `Assets/Data/Enemies/TempEnemyStat.asset`을 TempEnemy 프리팹과 SampleScene의 연결된 5기가 공유한다. 기본값은 HP 100·3.5m/s·공격 배율 1·공격속도 1이다. EnemyStat은 Awake(-150)에서 데이터를 인스턴스에 복사하고 Mortal.TryInitializeHealth로 현재 HP를 한 번 초기화하며 NavMeshAgent.speed에 속도를 적용한다. EnemyAttackAction은 캐릭터 공격 배율과 액션 피해 배율을 곱하고 공격속도로 선딜·후딜·쿨타임을 나눈다. 공유 에셋에 현재 HP를 저장하지 않으며 재활성화 시 회복·부활이나 실행 중 데이터 재적용은 없다. PlayerStat·플레이어 HP는 기존 상태를 유지한다. 적의 HP·이동속도 편집 위치는 이제 능력치 에셋이며 자세한 사용법은 [Enemy.md](Enemy.md) 2절을 참고한다. 런타임 능력치 변경·웨이브 배율·풀 재생성은 후속 작업이다.

적 능력치의 이번 직접 검증: **2026-11-02**, Unity 6000.3.25f1에서 EnemyValidation 95개·AttackValidation 75개·PlayerAttackValidation 50개, 총 **220개**와 MortalValidation을 실행해 네 종류 모두 종료 코드 0을 확인했다. 별도 에셋의 HP·실제 이동속도·피해/타이밍 배율 적용, 공유 에셋과 개체별 HP 분리, 재활성화 HP 유지, 생성 시 복사·후속 생성 반영, 기존 사망·공격을 검증했다. 기존 플레이어 검증의 짧은 후딜/다음 입력 프레임 경계 문제는 고정 시간·후딜 초반 검사로 보완했다. 최종 .NET 빌드 경고·오류 0, 씬·NavMesh·기존 공격 데이터/투사체/애니메이션/meta 변경 0, GUID 중복 0과 git diff --check를 확인했다. 로그는 `Logs/EnemyStats_20261102/`이며 초기 실패와 최종 결과는 Enemy.md 11절에 구분했다. 실제 키·마우스 수동 플레이·이미지 렌더링과 다른 독립 검증은 이번에 재실행하지 않았다.

최신 적·공통 액션 기준: **2026-11-01**. `ActionBase`를 공통 부모로 추가하고 PlayerAction·EnemyAction이 직접 상속한다. 공통 타이밍·조건·제한·취소·이벤트는 ActionBase, 슬롯·홀딩·선입력 자격은 PlayerAction이 담당한다. 공통 타입은 ActionContext·ActionPhase·ActionResult·ActionCancelReason이며 PlayerActionSlot·PlayerActionStage는 유지한다. Attack.TryExecute는 ActionBase를 받고 Combatant는 모든 ActionBase를 집계한다. 모든 MeleeAttack은 원/부채꼴·플레이어/적 구분 없이 월드 벽 가림을 검사한다.

`TempEnemy.prefab`과 SampleScene의 연결된 인스턴스 **5기**, 베이크된 NavMesh를 추가했다. 적은 EnemyStateBase를 상속하는 Idle·Chase·Combat·Dead 네 상태, Inspector에서 편집하는 EnemySensor의 시야각·거리·눈 위치·간격·장애물 마스크, 피격 인식, NavMesh 추격, EnemyAttackAction의 근접 공격을 사용한다. 초기 HP 100, 피해 15, 선딜 0.5초·후딜 0.8초·쿨타임 1.2초다. Mortal.OnDead에서 Dead 정리 후 Destroy한다. 적 메시·애니메이션·지연 비활성화·스테이지 일괄 제거·Gunner 거리 유지·보스·웨이브 흐름은 후속 작업이다. 상세 구조와 새 적 생성 방법은 [Enemy.md](Enemy.md), 이번 검증 로그는 `Logs/Enemy_20261101/`을 참고한다. 아래 이전 날짜의 적 AI 미구현·PlayerAction 공통 부모 기록은 당시 상태다.

적·ActionBase의 이번 직접 검증: **2026-11-01**, Unity 6000.3.25f1에서 적 70개·공통 조건 39개·플레이어 액션 43개·공통 공격 75개·플레이어 공격 50개·대시 70개·장전 40개·행동 제한 110개·선입력 85개, 총 **582개**와 애니메이션 17발·이동·점프·IK·장전·투척·벽·정지, 스태미나·Mortal·Projectile까지 **13종 모두 종료 코드 0**을 확인했다. .NET 빌드 경고·오류 0, 기존 meta/공격 데이터/투사체/애니메이션 변경 0, 기존 씬 블록 115개 보존, 새 GUID 유일성과 git diff --check를 확인했다. 씬 전체·캡슐 근접 렌더 2장을 열어 확인했으며 실제 키·마우스 수동 플레이는 수행하지 않았다. 상세 결과는 Enemy.md 11절과 `Logs/Enemy_20261101/`에 있다. 이전 검증 기록과 구분한다.

최신 입력 로그 기준: **2026-10-31, 홀딩 로그까지 제거**. PlayerController의 버튼 누름(Pressed)·홀딩(Held)·해제(Released) 로그를 모두 제거했다. logInput·heldLogInterval·전용 타이머·Handle 메서드와 씬의 저장값, 검증 코드의 옛 설정 접근도 제거했다. Skill2/Ultimate 실행 로그와 실제 입력 스냅샷·행동 선택·선입력·해제 전달·홀딩 반복은 유지한다. 이번에는 Unity 6000.3.25f1에서 PlayerActionRestrictionValidation **39개**를 직접 실행해 종료 코드 0으로 통과했고, 입력 로그 0건·두 스킬 실행 로그 각 1건을 확인했다. .NET 빌드 경고·오류 0, 런타임·씬·검증 코드의 옛 로그 참조 0건 및 git diff --check를 확인했다. 로그는 `Logs/HeldInputLogRemoval_20261031/`이며 앞선 누름·해제만 제거한 빌드는 `Logs/InputButtonLogs_20261031/`다. 아래 Unity 437개 검증은 앞선 공통 조건 작업의 결과이며 이번에 전체 회귀·수동 플레이·이미지 렌더링은 재실행하지 않았다. 남은 스킬 효과·애니메이션·적 AI·사망 행동·반복 전투 흐름은 유지한다.

최신 공통 액션 조건 기준: **2026-10-31**. PlayerAction에 직렬화 설정 `requiresGrounded`(기본 false)·`canMove`(기본 true)와 읽기 전용 RequiresGrounded·CanMove를 추가했다. 접지 필요 액션은 부모가 수신 자격·시작·선딜 완료 시 Motor.IsGrounded를 검사하며 PlayerMotor의 대시 전용 접지 자격 검사는 제거했다. Combatant가 BlocksMovement를 집계하고 Motor가 CanMove=false 액션의 선딜·Active·후딜 동안 일반 이동·새 점프를 막는다. 중력·충돌·시점은 유지한다. SampleScene의 대시는 지면 전용을 유지하며 Skill2(F)는 지면 전용·이동 허용, Ultimate(Q)는 공중 사용 가능·이동 금지다. 두 스킬은 PlayerAction을 직접 상속해 Attack 없이 선딜 0.5초 후 실행 로그·ActionExecuted를 한 번 알리고 후딜 1초를 사용한다. 수락 시 장전을 취소하지만 피해·AttackExecuted·탄약 소모·최종 스킬 효과·전용 애니메이션은 추가하지 않았다. 상세 설정과 이번 직접 검증은 [PlayerAction 설계](PlayerAction%20설계.md) 11절과 `Logs/ActionRestrictions_20261031/`을 참고한다. 아래 2026-10-30의 미구성 Skill2·Ultimate 및 Motor 접지 검사 기록은 변경 전 상태다.

공통 조건의 이번 직접 검증: Unity 6000.3.25f1에서 새 조건 39개·공통 액션 43개·대시 70개·선입력 85개·행동 제한 110개·장전 40개·플레이어 공격 50개, 총 **437개**와 애니메이션 17발·이동·점프·IK·장전·투척·벽·정지를 통과했다. .NET 빌드는 경고·오류 0이며 씬 변경 범위·기존 meta/공격/투사체/애니메이션 보존·새 GUID 유일성과 git diff --check를 확인했다. 로그는 `Logs/ActionRestrictions_20261031/`이다. 이전 날짜 결과와 별도이며 실제 키·마우스 수동 플레이·이미지 렌더링·공통 Attack/Mortal/Projectile·스태미나 독립 검증은 이번에 재실행하지 않았다. 실제 스킬 효과·애니메이션·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다.

최신 공격 Origin 책임 기준: **2026-10-30**. 기본 `origin` 직렬화 필드는 PlayerAttackAction에서 Attack으로 옮겼다. Inspector에서는 공격 액션에 Attack을, Attack 컴포넌트에 Origin을 연결한다. PlayerAttackAction.Origin과 PlayerActionBinding.Origin은 Attack.Origin을 조회하며 별도로 저장하지 않는다. SampleScene의 세 Attack은 기존 Muzzle을 그대로 참조한다. 요청별 Transform·고정 월드 위치 API와 선딜 완료 시 최신 위치 조회·총구 삭제 시 실행 실패·벽 보정 규칙은 유지한다. 이번 검증은 [PlayerAction 설계](PlayerAction%20설계.md)의 Origin 이관 기록을 참고한다.

Origin 이관의 이번 직접 검증: Unity 6000.3.25f1에서 공통 공격 75개·플레이어 공격 50개·장전 40개·선입력 85개, 총 250개와 애니메이션 17발·이동·점프·IK·장전·투척·벽·정지를 통과했다. .NET 빌드 경고·오류 0, 씬 변경 범위·meta 보존과 git diff --check를 확인했다. 로그는 `Logs/AttackOrigin_20261030/`이며 아래의 앞선 PlayerAction 검증과 별도다. 수동 플레이·이미지 렌더링과 다른 독립 검증은 재실행하지 않았다. 남은 스킬 효과·손 투척 지점·추가 애니메이션·적 AI·사망 행동·반복 전투 흐름은 유지한다.

최신 전투 액션 리팩토링 기준: **2026-10-30, 구현**. 공통 부모 `PlayerAction`과 사용자 지정 **Soilder** 접두사의 기본·보조 발사·Skill1·Skill2·Ultimate·Dash·Reload 액션을 구현했다. 기존 PlayerAction enum·AttackSlot은 PlayerActionSlot으로 통합했다. AttackData 계열에는 공격 속성만 두고 선딜·후딜·쿨타임·repeatWhileHeld와 실행 상태는 PlayerAction으로 이관했다. Attack은 액션이 호출하는 동기 효과 실행기이며 독립 타이머·TryAttack·LateUpdate는 제거했다. 현재 세 공격·피해·Muzzle·GUID와 선입력 규칙은 유지한다. Skill2·Ultimate 클래스는 미구성 상태여서 효과를 실행하지 않는다. 상세 책임·API·저장값·이번 검증은 [PlayerAction 설계](PlayerAction%20설계.md)를 참고한다.

최신 대시 조건 기준: **2026-10-31, 검사 책임 이관**. 대시는 지면에 접지 중일 때만 시작한다. 대시 액션의 RequiresGrounded=true 설정을 PlayerAction이 검사하며 Controller는 수신 자격을 확인하고 실제 실행 직전에도 부모의 접지 검사와 SoilderDashAction의 자원 검사를 거친다. 공중의 Dash.Pressed는 공격·대시 딜레이 중에도 예약하지 않으며 직접 TryDash 호출도 거절한다. 공격·대시 딜레이 중 예약된 대시는 첫 실행 기회에 접지를 다시 검사하고 공중이면 소비·폐기하며 착지를 기다리지 않는다. 이전 날짜 기록의 공중 대시 허용은 당시 상태다.

최신 클래스명 변경 기준: **2026-10-30**. PlayerAimController → PlayerAim, PlayerAnimationController → PlayerAnimation, PlayerAttackController → PlayerCombat, PlayerCameraController → PlayerCamera로 클래스·파일·참조를 변경했다. PlayerController 이름은 유지한다. 네 스크립트의 meta GUID와 직렬화 필드명·씬 참조·실행 순서·기능은 유지한다. 과거 날짜 기록의 이전 이름은 당시 이름이다.

최신 입력 변경 기준: **2026-10-30**. PlayerController 한 곳에서 이동·점프와 전투 행동 선택을 처리한다. 공격·대시 딜레이 중 다음 행동 한 개를 예약하며 maxInputBufferTime(코드·SampleScene 기본 1초)이 지나면 버린다. 일반 예약은 새 일반 누름으로 교체하고 대시는 모든 예약보다 우선하며 다른 입력으로 교체·유효 시간 연장되지 않는다. 입력 수신 시 딜레이 외 접지·스태미나·쿨타임·탄약·장전·구성 조건을 만족한 행동만 예약한다. 유효하지 않은 입력은 기존 예약을 교체하거나 연장하지 않는다. 실행 시점에도 같은 조건을 다시 검사하고 한 번 시도한 예약은 성공·실패와 관계없이 소비한다. 정지·커서 해제·포커스 상실·Reader/Controller 비활성화·사망은 예약을 지운다. 이동·점프는 예약하지 않으며 홀딩 반복·빈 탄창 자동 장전은 예약 다음에 재시도한다. 별도 PlayerActionController는 추가하지 않는다. 2026-10-30 명명 규칙에 따라 PlayerController만 Controller 접미사를 사용하고 나머지는 PlayerAim·PlayerAnimation·PlayerCombat·PlayerCamera다. 최신 상세 책임·규칙은 [EnhancedInput.md](EnhancedInput.md)를 기준으로 한다. 아래 2026-10-29 이전 기록의 ‘단발 입력 예약 없음’은 당시 상태다.

진행 현황 기준: **2026-10-30**. Soldier 기본 애니메이션과 Skill1(E)의 Toss Grenade를 연결했으며 남은 애니메이션은 **Dash·Skill2·Ultimate 3개**다. 이동 임포트·플레이어 forward 기준 상체 보정·양팔 IK를 유지한다. SampleScene의 좌클릭은 TempPrimaryFire 히트스캔 피해 30, 우클릭은 기존 Bullet 피해 50, E는 기존 Grenade 폭발 피해 100이다. 기본 발사에만 30발 탄창과 수동·빈 탄창 자동 재장전을 연결했다. 자동 재장전은 가능한 상태가 될 때까지 매 프레임 재시도한다. 장전 중 기본·보조 발사는 금지하고 대시 성공·스킬 수락 시 탄환 보충 없이 취소한다. PlayerStat의 reloadDuration(3.3초) / reloadSpeedMultiplier(1)로 장전 시간을 계산하며, Animation은 OnReloadStarted 이벤트와 재장전 상태 전용 배속 파라미터를 사용한다. OnReloadCompleted는 탄창을 채운 뒤 한 번 발생한다. dashDistance(5m)는 Motor에서 PlayerStat으로 옮겼으며 시간·후딜·스태미나는 유지했다. 바인딩은 PlayerAction 연결로 이관했고 반복 설정은 액션별로 관리한다. 공격 원점 보정과 기존 세 공격·Muzzle 참조를 보존했다. Skill2·궁극기 효과, 손 투척 지점·적 AI·사망 행동과 반복 전투 흐름은 후속 작업이다.

공격 선딜·후딜의 공통 행동 제한을 2026-10-29 연결했다. 같은 캐릭터의 다른 공격·대시·재장전을 거절하며 이동·점프·시점은 유지한다. 점프는 CanJump로 분리해 공격 선딜·후딜과 대시 후딜에 허용하며 실제 대시 이동 중에는 거절한다. TempGrenade의 선딜 0초·후딜 1초·쿨타임 6초를 유지했다. 이전 날짜 기록의 점프 차단·공격 선딜 취소·수류탄 시간 0 설명은 당시 상태다.

## 1. 작업할 때의 기준

- 이 문서는 저장소 전체에 적용하는 프로젝트 설명, 구현 목표, 진행 현황이다. 작업 시작 시 읽고 관련 코드와 상세 가이드를 확인한다.
- 사용자의 최신 요청을 우선한다. 아래의 목표와 현재 상태를 구분하고, 새로운 결정이 내려지면 해당 항목을 갱신한다.
- 구현 여부는 실제 실행 코드와 씬·프리팹 연결을 기준으로 판단한다. 입력 로그, TODO, 주석, 설치된 패키지만으로 기능이 완성됐다고 기록하지 않는다.
- 기능을 변경한 작업을 마칠 때 구현 현황, 남은 작업, 확인한 검증 결과와 기준 날짜를 갱신한다. 관련 상세 가이드도 동기화한다.
- 기존 작업을 보존하며 요청 범위에 맞게 수정한다. 조작 감각과 전투 흐름을 작은 단위로 구현하고 플레이로 확인한다.
- 사용자에게 변경 내용과 확인 결과를 한국어로 설명한다. 이전 검증 기록과 이번에 직접 실행한 검증을 구분한다.

상세 구현 참고:

- [플레이어 조작 가이드](플레이어%20조작%20가이드.md): 입력, 이동, 카메라, 클래스별 책임과 메서드.
- [공격 구현 가이드](공격%20구현%20가이드.md): 공격 데이터·입력·실행·진영·피해와 투사체 생명주기·풀·프리팹·검증을 합친 통합 문서.
- [EnhancedInput.md](EnhancedInput.md): 클래스별 입력·실행 책임, 한 개 선입력·최대 유지 시간·대시 우선순위·실행 실패와 조작 중단 규칙.
- [PlayerAction 설계](PlayerAction%20설계.md): 구현된 전투 액션의 책임·타이밍·상속·바인딩·저장값 이관과 이번 검증.
- [Enemy.md](Enemy.md): 적 FSM·감지·이동·액션·공격·사망, TempEnemy 구성과 새로운 적 생성 방법.

## 2. 프로젝트 설명

**ProjectU는 웨이브 전투와 강화 선택을 반복하며 최종 보스를 공략하는 3D TPS 아레나 슈터 로그라이트다.** 게임 완성과 Unity 게임 클라이언트 개발 포트폴리오 제작이 목적이다.

| 항목 | 기준 |
|---|---|
| 개발 환경 | Unity 6, C#; 저장소 에디터 버전 `6000.3.25f1` |
| 주요 패키지 | URP `17.3.0`, Input System `1.20.0`, AI Navigation `2.0.14`, Test Framework `1.6.0` |
| 플랫폼·플레이 방식 | Windows PC, 싱글플레이 |
| 개발 규모·기간 | 1인 개발, 목표 8~12주 |
| 한 판 목표 시간 | 15~20분 |
| 초기 완성 범위 | 고정 아레나 1개, 총 10웨이브, 최종 보스 1종, Soldier 1종 |

근미래의 자율 기계 제어 네트워크 **U-NET**이 인간의 명령을 거부하고 시설을 점거한다. 플레이어는 특수 작전 요원으로 투입되어 방어 기계들을 격파하고 중앙 제어 시설을 무력화한다.

스타일라이즈드 SF 분위기, 산업·군사 시설 형태의 아레나, 기계형 적을 사용한다. 적의 공격 예고와 투사체를 쉽게 식별할 수 있게 한다. 구체적인 아트와 연출은 확보 가능한 에셋에 맞춰 조정한다.

## 3. 전체 구현 목표

### 한 판의 흐름

**전투 시작 → 적 처치 → 웨이브 클리어 → 강화 3개 중 1개 선택 → 다음 웨이브 → 최종 보스 → 결과**

- 1~9웨이브는 일반 전투, 10웨이브는 최종 보스전이다.
- 1~9웨이브를 클리어할 때마다 강화 보상을 선택한다. 선택 중에는 전투 진행을 멈춘다.
- 웨이브 사이에 플레이어 체력을 유지한다.
- 최종 보스를 처치하면 성공, 플레이어 체력이 소진되면 실패한다.
- 한 판이 끝나면 획득한 강화와 진행 상태를 초기화하고 재시작할 수 있게 한다.

### 플레이어와 전투

- 초기 클래스는 돌격소총을 사용하는 **Soldier**다. 이동 사격, 대시 회피, 수류탄 범위 공격을 중심으로 구성한다.
- 일반·전투 모드를 나누지 않고 항상 전투에 대응하는 조작 방식을 사용한다.
- 캐릭터의 수평 방향은 카메라의 조준 방향을 따른다. 카메라 기준으로 전후좌우 이동하고 옆걸음·뒷걸음에서도 조준 방향을 유지한다.
- 돌격소총의 목표는 **히트스캔 즉시 명중 판정**, 탄창, 재장전, 무제한 예비 탄약이다.
- 전술 대시는 Shift로 짧은 거리를 빠르게 이동한다. 역동적이고 캐주얼한 플레이를 위해 이동기는 별도 자원인 **스태미나(Stamina)**를 소모하며 **쿨타임 없이** 사용할 수 있게 한다. 지면에 접지 중이고 스태미나가 충분해야 사용할 수 있으며 최대량·소모량·초당 회복량·회복 지연 시간, 대시 무적 여부는 미정이며 대시 이동 중에는 일반 이동·점프를 금지하고 후딜레이에는 이동·점프를 허용한다. 두 구간에서 공격·장전·재대시는 금지한다.
- 전술 수류탄은 물리 투사체로 날아가 일정 시간 후 폭발해 범위 피해를 준다. 기본 사격과 별도인 **Skill1(E)**로 제공한다.
- **Skill2(F)**는 두 번째 스킬이며 효과는 아직 정하지 않았다. 대시 입력과 스태미나 자원은 Skill1·Skill2와 구분한다.
- 궁극기 입력은 Q로 예정되어 있으나 효과와 발동 조건은 아직 정하지 않았다.

| 입력 | 최종 목표 | 현재 프로토타입 |
|---|---|---|
| WASD | 카메라 기준 스트레이프 이동 | 구현 |
| 마우스 이동 | 카메라 회전·조준, 캐릭터 수평 회전 | 구현 |
| 좌클릭 | 기본 사격 | TempPrimaryFire 히트스캔 피해 30, 짧은 누름·홀딩 연사, 성공당 탄창 1발 소모; 시작 30발 |
| 우클릭 | 정밀 조준 | TempSecondaryFire의 기존 Bullet, 기본 피해 50, 짧은 누름·홀딩 연사에 임시 연결 |
| R | 재장전 | 탄창이 덜 찼으면 수동 장전, 0발이면 지속 자동 시도; 기본 3.3초 후 가득 채움, 대시·스킬 취소 가능 |
| Space | 점프 | 지상 점프와 Jump Up/Loop/Down 애니메이션 구현; 공격 선딜·후딜·대시 후딜에도 허용, 대시 이동 중 거절 |
| 왼쪽·오른쪽 Shift | 스태미나를 소모하는 대시, 쿨타임 없음 | 접지 중에만 이동 입력 방향 또는 무입력 시 전방으로 대시, 5m/0.2초·후딜레이 0.3초, 스태미나 25; 대시 이동 중 일반 이동·점프 차단, 후딜 중 이동·점프 허용; 두 구간에서 공격·장전·재대시 금지 |
| E | Skill1: 수류탄 투척 | TempGrenade의 기존 Grenade, 2.5초 후 기본 폭발 피해 100, 누름당 한 번 |
| F | Skill2: 효과 미정 | 지면 전용·이동 허용 검증용 액션; 선딜 0.5초 뒤 실행 로그·후딜 1초 |
| Q | 궁극기 | 공중 사용 가능·이동 금지 검증용 액션; 선딜 0.5초 뒤 실행 로그·후딜 1초 |
| Esc / 커서 해제 후 좌클릭 | 커서 해제 / 조작 재개 | 구현; 재잠금 클릭은 사격에서 제외 |

### 적·보스와 강화

| 대상 | 구현 목표 |
|---|---|
| Striker | 플레이어 추적, 공격 예고 후 근접 공격 |
| Gunner | 거리 유지, 눈으로 확인하고 피할 수 있는 투사체 발사 |
| U-NET Guardian | 10웨이브 최종 보스; 전투 단계에 따라 공격 패턴 변화 |
| 웨이브 난도 | 진행에 따라 적의 수와 조합으로 난도를 높임 |
| 무기 강화 | 공격력·탄창 증가, 재장전 단축, 탄환 관통 등의 효과 |
| 이동기·자원 강화 | 대시 스태미나 소모량 감소, 최대 스태미나 증가 등의 효과 |
| 스킬 강화 | 수류탄 범위 증가, 추가 폭발 등의 효과 |
| 특수 강화 | 처치 시 체력 회복, 조건부 추가 피해 등의 효과 |

강화 효과는 설계 방향을 보여주는 예시다. 단순한 능력치 강화부터 구현하고 동작을 바꾸는 효과를 점진적으로 추가한다. 보스의 패턴과 단계 전환 조건은 보스 개발 시 구체화한다.

프로토타입 완성 후 **Assassin과 Tank** 추가 클래스를 구현하는 방향이다. **Charger** 돌진형 적과 강화 개체는 기본 적 2종의 전투를 검증한 뒤 추가를 검토한다. 초기 범위에서는 추가 클래스를 제외하며, 일정이 부족하면 확장 기능을 줄이고 완결된 전투·강화·보스 흐름을 우선한다.

## 4. 현재 구현 현황

**현재는 플레이어와 캡슐 근접 적이 서로 피해를 주는 전투 프로토타입 단계다. 적의 감지·추격·공격·사망 제거를 연결했으며, 웨이브·강화·보스·결과를 잇는 한 판의 반복 흐름은 아직 구현하지 않았다.**

| 시스템 | 상태 | 실제 구현과 남은 범위 |
|---|---|---|
| 테스트 아레나 | 프로토타입 구현 | `SampleScene`에 40×40m 바닥, 경계 벽, X Bot 플레이어와 임시 소총; 기존 캡슐 렌더러·전방 표시는 숨기고 CharacterController는 유지 |
| Soldier 메시·애니메이션 | 기본 애니메이션 적용 완료 | Humanoid 클립 11개를 2D 이동 Blend Tree·점프 3상태·상체 소총·투척 레이어에 연결. 대기·전후좌우 이동·점프·사격·재장전과 이동 문제 해결을 2026-10-15 사용자 확인으로 완료 처리. 루트·반복 보정과 Rotation Offset 0, 플레이어 forward 기준 상체 보정·총구 조준·양팔 IK 사용. 남은 애니메이션은 Dash·Skill2·Ultimate; 최종 무기 모델·손가락 정렬은 별도 시각 완성도 작업 |
| 입력 | 선입력·행동 선택 구현 | WASD·마우스와 8개 버튼 스냅샷 유지. Controller가 최대 1초의 다음 행동 한 개·대시 최우선·최신 일반 입력 교체·실패/중단 폐기와 반복 요청을 담당. 이동·점프는 비예약 |
| 이동·점프 | 구현 | `CharacterController` 이동, 스트레이프, 카메라 Yaw 방향 회전, 중력·지상 점프·충돌; 실제 수평 속도와 접지·수직 속도로 애니메이션 구동 |
| TPS 카메라 | 구현 | 어깨 뒤 추적, Yaw·pitch, 수직 각도 제한, SphereCast 장애물 회피 |
| 발사·조준 | 프로토타입 구현 | 화면 중앙 조준점과 소총 자식 Muzzle을 상체·총구·실제 공격이 공유. 카메라 LateUpdate 이후 상체·총구 정렬 후 발사. 선딜이 끝나는 프레임의 총구 위치를 읽고 캐릭터~총구의 벽 검사·원점 보정·방향 재계산을 실행. 가까운 조준점의 총 후퇴와 수류탄 발사각 보정 유지 |
| 투사체·풀 | 구현 | 공통 생명주기, Bullet, Grenade, 프리팹별 사전 생성·확장·대여·반환·용량 제한 |
| 공통 공격 | 플레이어·근접 적 연결 | ActionBase 기반의 공통 타이밍·조건·제한과 3종 Attack을 사용. Player의 기존 세 공격을 유지하고 TempEnemy에 MeleeAttack을 연결. 모든 근접 공격에 벽 가림 적용 |
| 수류탄 스킬 | 임시 기능 연결 | E에서 TempGrenade 투척, 기존 물리 튕김·2.5초 지연 폭발·기본 범위 피해 100·연출·풀 반환. AttackExecuted(Skill1) 성공으로 상체 투척 애니메이션 재생. 실제 발사 시점·Origin·오프셋·발사각은 유지. 현재 저장값은 후딜 1초·쿨타임 6초이며 최종 밸런스·엄폐물·넉백은 후속 작업 |
| 소총·탄약·재장전 | 기본 발사 탄창·실제 장전 구현 | PrimaryFire 성공당 1발 차감, MaxMagazine까지 장전. R 수동·0발 지속 자동 시도, 장전 중 기본·보조 발사 차단, 대시·스킬로 취소. 무제한 예비 탄약이며 HUD는 후속 작업 |
| 플레이어 능력치 | 기본 능력치 연결 | 이동속도·대시 거리→Motor, 최대 스태미나→PlayerStamina, 공격력·공격속도→PlayerAttackAction, 최대 탄창·장전 시간/속도→PlayerCombat, 최대 체력→생성 시 Mortal 초기화를 연결했다. 런타임 최대 HP 변경에 따른 현재 HP 보정은 후속 작업 |
| 대시·스태미나 | 이동·행동 제한·씬 연결 | PlayerStamina의 소모·지연 자동 회복 유지. Shift.Pressed에서 접지·실행 가능한 상태를 확인 후 25 소모, Motor가 입력 방향 또는 전방으로 대시한다. 거리/실제 이동/액션 후딜은 각 Inspector 설정이며 현재 5m/0.2초/0.3초. 대시·후딜 중 공격·장전·재대시 금지, 후딜 중 이동·점프 허용, 대기 공격·재장전 취소. 홀딩 반복·별도 쿨타임·무적·전용 애니메이션 없음 |
| 정밀 조준·Skill2·궁극기 | 스킬 조건 검증용 액션 구현 | F는 지면 전용·이동 허용, Q는 공중 사용 가능·이동 금지. 각각 선딜 0.5초·후딜 1초와 실행 로그·장전 취소를 연결; 실제 정밀 조준·스킬 효과·궁극기 자원은 미구현 |
| 적 능력치 | ScriptableObject·프리팹 연결 | EnemyStatData의 최대 HP·이동속도·공격력/공격속도 배율을 EnemyStat이 생성 시 복사해 실제 체력·이동·공격에 적용. TempEnemyStat 공유; 런타임 강화·웨이브 배율은 후속 작업 |
| 체력·피해·사망 | 플레이어·적 피해 연결 | 현재 HP는 Mortal, 시작 HP는 PlayerStat.MaxHealth / EnemyStatData.MaxHealth에서 공급. Player·TempEnemy 기본 HP 100. 적은 OnDead에서 액션·이동·충돌 정리 후 Destroy. 플레이어 사망 이동 중단·연출·복귀와 적 지연 제거는 후속 작업 |
| 적 AI·적 공격 | 근접형 TempEnemy 구현 | EnemyStateBase의 Idle·Chase·Combat·Dead, 시야/피격 인식, NavMesh 추격, 실제 근접 피해, 캡슐 프리팹 5기 배치. 공격 예고 시각 연출·애니메이션·Gunner 거리 유지·추가 패턴은 후속 작업 |
| 웨이브·강화 선택 | 미구현 | 적 생성, 클리어 판정, 전투 정지, 강화 제시·적용 필요 |
| 보스·결과·재시작 | 미구현 | 10웨이브 보스, 성공·실패, 한 판 상태 초기화 필요 |
| 게임 UI·사운드·완성도 | 시각 프로토타입, 나머지 미구현 | X Bot 이동·점프·상체 동작, 임시 소총과 폭발 구체 제공; HUD·보상·결과 UI, 사운드·파티클·추가 애니메이션·밸런스·성능 개선 필요 |

### Soldier 애니메이션 후속 작업

기본 애니메이션과 이동 관련 문제 해결은 완료했다. Skill1(E)의 Toss Grenade 애니메이션을 연결했으며 남은 작업은 Dash·Skill2·Ultimate 3개다.
애니메이션 작업 현황과 실제 행동·자원·피해 기능의 구현 현황을 구분한다.

| 애니메이션 | 입력 | 현재 기능과 남은 범위 |
|---|---|---|
| Dash | 좌·우 Shift | 스태미나·실제 대시 이동·후딜레이·행동 제한 구현; 전용 애니메이션·무적은 후속 작업 |
| Skill1 | E | Toss Grenade 상체 애니메이션 연결 완료. 실제 투척 시점·Muzzle Origin·오프셋·발사각은 기존 값 유지; 손 투척 지점은 후속 작업 |
| Skill2 | F | 애니메이션 추가 예정; 지면 전용 검증용 액션·딜레이·실행 로그 구현, 실제 효과 설계·실행 기능 필요 |
| Ultimate | Q | 애니메이션 추가 예정; 이동 금지 검증용 액션·딜레이·실행 로그 구현, 실제 효과·자원·발동 조건 설계 필요 |

### 코드와 에셋의 위치

런타임 네임스페이스는 `ProjectU`, 에디터 검증 도구는 `ProjectU.Editor`다.

| 경로 | 책임 |
|---|---|
| `Assets/Scripts/Mortal.cs` | 플레이어·적 공통 체력, 무적, 피해·사망 처리 및 이벤트 기반 클래스 |
| `Assets/Scripts/Combat/` | Combatant·Hitbox, Attack과 3종 실행, DamageContext·DamageResolver·ProjectileLaunchData |
| `Assets/Scripts/Combat/Data/` | 공통 AttackData와 HitscanAttackData·ProjectileAttackData·MeleeAttackData |
| `Assets/Data/Attacks/` | 씬에 연결된 TempPrimaryFire·TempSecondaryFire·TempGrenade와 이전 독립 예제 에셋 |
| `Assets/Editor/AttackValidation.cs` | 씬을 저장하지 않는 진영·공격 타이밍·명중·관통·범위·풀 재사용 자동 플레이 검증 |
| `Assets/Editor/PlayerAttackValidation.cs` | 저장된 씬의 세 입력·에셋·실제 피해 30/50/100·이벤트·배율·정지·사망 검증 |
| `Assets/Scripts/Player/ButtonInputState.cs` | 읽어도 소비되지 않는 버튼별 프레임 입력 스냅샷 |
| `Assets/Scripts/Player/PlayerInputReader.cs` | 독립 `PlayerControls` InputActionMap 생성, 상태 수집, 커서 관리 |
| `Assets/Scripts/Player/PlayerStat.cs` | 9개 능력치의 Inspector 설정; Awake(-150)에서 Mortal 시작 HP 초기화, 이동속도·대시 거리·탄창·장전 시간/속도·최대 스태미나 원본. ReloadDuration은 외부 읽기 전용 |
| `Assets/Scripts/Player/PlayerStamina.cs` | 현재 스태미나, 소모 검증·차감·회복, 지연 자동 회복과 값 변경 이벤트 |
| `Assets/Scripts/Player/PlayerController.cs` | 이동·점프 명령, 선입력 한 개·만료·교체·대시 우선순위, 전투 액션 선택·요청, 공격/장전 한 프레임 명령과 홀딩/빈 탄창 재시도·로그 |
| `Assets/Scripts/Player/PlayerMotor.cs` | 한 프레임 명령을 소비해 이동·회전·중력·점프·충돌, 대시 이동·후딜·행동 가능 상태 처리 |
| `Assets/Scripts/Player/PlayerAnimation.cs` | Motor로 이동·점프 갱신, 공격·대시·장전 이벤트로 상체 동작 실행, 장전 상태 전용 배속·일시정지 처리 |
| `Assets/Scripts/Player/PlayerAim.cs` | 카메라 갱신 후 척추·가슴 회전, 소총·총구 조준, 양팔 IK·가까운 조준점에서 소총 후퇴 |
| `Assets/Scripts/Player/PlayerCamera.cs` | 시점 회전, 플레이어 추적, 카메라 장애물 검사 |
| `Assets/Scripts/Player/PlayerActionBinding.cs` | PlayerActionSlot·PlayerAction 연결. 공격 참조는 공격 액션에서 조회 |
| `Assets/Scripts/Combat/ActionBase.cs`, `ActionTypes.cs` | 플레이어·적 공통 행동 생명주기·시간·조건·제한·취소·실행 정보 |
| `Assets/Scripts/Player/PlayerAction.cs`, `PlayerAttackAction.cs` | 플레이어 슬롯·홀딩·선입력 자격·실행 단계 / Attack 실행 어댑터 |
| `Assets/Scripts/Enemy/`, `States/` | 적 AI·감지·NavMesh 이동·액션 선택·실행과 EnemyStateBase를 상속하는 네 상태 |
| `Assets/Scripts/Enemy/EnemyStatData.cs`, `EnemyStat.cs` | 적 기본 능력치 ScriptableObject / 생성 시 복사·현재 HP 초기화·Agent 속도 적용 |
| `Assets/Data/Enemies/TempEnemyStat.asset` | TempEnemy 공통 최대 HP·이동속도·공격력/공격속도 배율 에셋 |
| `Assets/Prefabs/Enemies/`, `Assets/Scenes/Navigation/` | TempEnemy 캡슐 프리팹·머티리얼과 저장된 NavMesh |
| `Assets/Editor/EnemyValidation.cs`, `TempEnemySetup.cs` | 적 자동 플레이 검증 / 데모 프리팹·이동 영역·씬 배치 도구 |
| `Assets/Scripts/Player/Actions/Soilder…Action.cs` | 일곱 캐릭터별 전투 액션. Skill2·Ultimate는 조건 검증용 딜레이·실행 로그, 실제 효과는 미구현 |
| `Assets/Editor/PlayerActionValidation.cs` | 새 부모·일곱 연결·공격 없는 유지형 행동·사격 공존·해제·슬롯별 제한·대시 확정/정리 검증 |
| `Assets/Editor/PlayerActionRestrictionValidation.cs` | 공통 접지 수신/실행 재검사·예약 폐기·두 스킬 딜레이/로그·이동/점프 제한·중력·정지·해제·집계·장전 취소 검증 |
| `Assets/Scripts/Player/PlayerCombat.cs` | Controller의 한 프레임 명령 소비·최종 검사, 바인딩별 최신 조준·능력치·공격 요청·실행 이벤트, 기본 발사 탄창·장전·취소·시작/완료 이벤트. 버튼·선입력 직접 관리 없음. 장전 상태/작업은 SoilderReloadAction에 위임 |
| `Assets/Scripts/Projectiles/Projectile.cs` | 추상 기반 클래스; Rigidbody 초기화, 수명, 충돌 전달, 발사자 충돌 제외·복구, 반환 |
| `Assets/Scripts/Projectiles/ProjectileBullet.cs` | 중력 없는 직선 물리 총알; 충돌 시 반환 |
| `Assets/Scripts/Projectiles/ProjectileGrenade.cs` | 중력·튕김, 수명 만료 시 물리 정지·폭발 연출·반환 |
| `Assets/Scripts/Projectiles/ProjectilePool.cs` | 프리팹별 Queue·Dictionary·HashSet으로 생성·대여·반환 관리 |
| `Assets/Scenes/SampleScene.unity` | 현재 테스트 씬과 직렬화된 컴포넌트 참조 |
| `Assets/Prefabs/Projectiles/` | `Bullet.prefab`, `Grenade.prefab`, 물리 머티리얼과 시각 머티리얼 |
| `Assets/Characters/Soldier/Models/` | 임시 Soldier의 `X Bot.fbx`; Humanoid Avatar와 전신 스킨드 메시 |
| `Assets/Characters/Soldier/Animations/` | Mixamo의 사격·재장전·소총 대기·전후좌우 달리기·Jump Up/Loop/Down·Toss Grenade 클립 11개; Soldier Animator에서 사용 |
| `Assets/Characters/Soldier/Animator/` | `Soldier.controller`와 `SoldierUpperBody.mask` |
| `Assets/Characters/Soldier/Materials/` | 임시 소총의 URP/Lit 머티리얼 2개 |
| `Assets/Editor/ProjectileValidation.cs` | 기존 씬·프리팹을 사용하는 투사체 배치 검증 |
| `Assets/Editor/MortalValidation.cs` | 씬을 저장하지 않는 공통 체력·피해·사망 검증; 에디터 메뉴 또는 배치 실행 |
| `Assets/Editor/PlayerAnimationValidation.cs` | 씬을 저장하지 않는 이동·점프·상체 동작·조준·총구 발사·일시정지 검증과 선택적 렌더링 |
| `Assets/Editor/PlayerStaminaValidation.cs` | 씬을 저장하지 않는 능력치·자원 API·Shift 소모·자동 회복·일시정지 검증 |
| `Assets/Editor/PlayerDashValidation.cs` | 저장된 씬의 방향·거리·시간·벽 충돌·스태미나·후딜 행동 제한·공격/재장전 취소·일시정지 검증 |
| `Assets/Editor/PlayerReloadValidation.cs` | 탄창·장전 시간/배속·두 발사 차단·대시/세 스킬 취소·빈 탄창 지속 재시도·일시정지·실패 발사·사망 검증 |
| `Assets/Editor/PlayerActionDelayValidation.cs` | 다섯 공격 슬롯의 선딜·후딜 상호 차단, 이동·지상 점프 허용·공중 재점프 방지·대시/장전 거절·동시 입력·쿨타임·정지·실패/취소 검증 |
| `Assets/Editor/EnhancedInputValidation.cs` | 실제 Controller·씬 실행기의 한 개 예약·대시 우선·다섯 슬롯·교체·R·만료/0초·자원/쿨타임 실패·중단 폐기 검증 |
| `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` | 패키지·Unity 버전의 기준 |

### 유지해야 할 현재 구조

- `Update` 순서는 Reader **-200** → Camera **-100** → Stamina **-75** → Controller **-50** → Motor **0** → Animation **50**이다. Animator 평가 후 `LateUpdate`는 Camera **-100** → Aim **75** → PlayerCombat **100** → PlayerAction **110** 순서다. Controller는 같은 프레임의 Yaw를 사용하고 PlayerCombat는 조준·배율 갱신과 요청, PlayerAction은 타이밍과 실제 Attack 호출을 담당한다.
- 이동은 Motor, 애니메이션은 실제 Motor 상태, Aim은 시각 자세를 담당한다. GetAimPoint를 시각 조준과 공격이 공유한다. AttackExecuted(PlayerActionSlot)은 연결된 Attack의 실제 실행 성공 후 발생한다. Soldier Animation은 PrimaryFire·SecondaryFire 성공으로 사격, Skill1 성공으로 투척 애니메이션을 실행하며, 소총 시각 Muzzle은 Aim에서, 공격별 기본 Origin은 Attack에서 관리한다. PlayerAttackAction은 요청별 실행 위치와 선딜을 관리한다.
- Reader는 입력 수집, Controller는 행동 명령, Motor는 실제 이동을 담당한다. Motor는 입력과 카메라를 참조하지 않으며 이동·점프 명령은 한 업데이트에 소비한다.
- SoilderDashAction이 접지·이동 구성·스태미나·슬롯 제한을 검사하고 이동·자원 상태를 확정한 뒤 Motor.OnDashExecuted를 한 번 알린다. 공개 Motor.TryDash도 이 액션을 거친다. 실제 이동 시간은 Motor, 후딜은 액션의 recoveryTime, 거리는 PlayerStat이 관리한다. Motor/대시 액션 비활성화는 이동·후딜 잠금을 정리하고 별도 쿨타임·전용 클립·무적은 추가하지 않았다.
- 탄창의 원본은 PlayerCombat, 장전 상태·작업의 원본은 SoilderReloadAction이다. 기존 CurrentAmmo·MaxAmmo·IsReloading·ReloadRemaining·CanReload·TryReload/CancelReload API는 액션에 위임한다. 기본 발사 성공만 1발 소모한다. 장전 중 두 발사는 거절하고 대시 성공·스킬 수락은 보충 없이 취소한다. 공격 선딜·후딜 중 R·대시는 공격을 취소하지 못한다. 현재 수류탄 후딜 1초·쿨타임 6초와 애니메이션 비권위 규칙을 유지한다.
- ActionBase.Blocks(action)와 Combatant의 전체 액션 집계가 같은 소유자의 전투 제한을 판단한다. PlayerAction.Blocks(slot)은 플레이어 슬롯별 정책을 유지한다. 바인딩 없는 직접 액션 요청도 동일한 자격 검사를 받는다. Controller는 선택한 슬롯을 실제로 막는 딜레이만 기다린다. 실행 프레임 잠금은 효과 호출 전에 확정해 0초 설정·이벤트 재진입을 막는다. EnemyCombat에는 별도 타이머를 두지 않는다.
- OnReloadStarted·OnReloadCompleted는 상태 갱신 후 각각 한 번 발생한다. 취소는 보충·완료 이벤트를 발생시키지 않는다. Animation은 장전 입력이나 독립 타이머 대신 시작 이벤트를 구독하며 ReloadSpeedMultiplier float를 Reloading 상태에만 적용한다. 진행 중 배율 강화는 남은 기본 작업량과 애니메이션에 함께 반영하고 일시정지 시 둘 다 멈춘다. 장전 중에는 사격 홀딩 여부와 관계없이 Aim 보정을 푼다.
- 입력은 코드에서 만든 액션 맵을 사용한다. 템플릿 `Assets/InputSystem_Actions.inputactions`는 현재 플레이어 조작에 사용하지 않는다.
- Dash=좌·우 Shift, Skill1=E, Skill2=F, Ultimate=Q, Reload=R을 유지한다. Controller는 입력·예약·반복·자동 장전을 선택하고 PlayerCombat/PlayerAction이 실행 조건을 최종 검사한다. 대시·Skill2는 RequiresGrounded=true이며 수신·시작·효과 실행에 접지가 필요하다. 일반 이동·점프는 예약하지 않는다. Skill2·Ultimate는 검증용 딜레이와 실행 로그·ActionExecuted를 사용하며 실제 피해·자원 효과는 없다. CanMove=false 액션은 선딜·Active·후딜 동안 일반 이동·점프를 막고 예약 중에는 막지 않는다. 버튼 해제와 조작 중단은 다른 행동 잠금 중에도 유지형 액션을 정리한다.
- 액션·공개 멤버·로그 상태명은 PascalCase, 비공개 필드·지역 변수는 camelCase를 따른다. Inspector 설정은 `[SerializeField]` 필드로 관리한다.
- `Mortal.isDead`는 사용자가 요청한 공개 이름을 그대로 유지하는 예외다. `HP`와 `isDead`는 읽기 전용, `IsInvincible`은 읽기·쓰기가 가능하다. `TakeDamage(int damage, GameObject attacker)`는 실제 HP 감소량과 공격자를 `OnDamaged`에 전달하고, 치명 피해에서는 이후 `Dead(attacker)`로 `OnDead`를 한 번 발생시킨다. 파생 클래스는 재정의 시 기본 구현을 호출한다.
- 투사체 이동은 Rigidbody가 담당한다. `ProjectilePool.Spawn()`이 `Launch()`까지 호출하며 반환은 `Despawn()`을 사용한다. 외부의 직접 비활성화·삭제는 풀의 대여 상태를 어긋나게 할 수 있다.
- 재사용 시 발사자, 수명, 물리 상태, 충돌 제외, 추가 연출 상태를 초기화한다. 수명과 폭발 연출은 게임 시간에 따라 진행한다.
- PlayerAction이 LateUpdate 110에서 Attack을 동기 호출한다. 공격 속성은 공유 ScriptableObject, 타이밍은 액션, 연속 발사 상태는 HitscanAttack 인스턴스가 관리한다. 쿨타임·후딜은 실행 성공부터 동시에 시작하며 Time.timeScale 0에서 멈춘다. 히트스캔 탄퍼짐은 SpreadAngle이 양수이고 SpreadStartShot에 도달했을 때 적용하며 0이면 비활성화한다. 별도 탄퍼짐 활성화 필드는 없다. 현재 TempPrimaryFire와 SoldierRifleAttack 예제는 3도다. TempPrimaryFire의 후딜은 0.25초, TempSecondaryFire의 후딜은 0.5초이며 기존 설정을 보존한다. HitscanAttack은 기본 노란색·1초의 Debug.DrawLine을 실제 시작점에서 벽·관통 한도 대상의 표면 또는 최대 사거리까지 표시하며, 컴포넌트의 Debug 설정과 각 뷰의 Gizmos로 확인한다. LastShotEndPoint는 실제 궤적 끝점이다. 진영은 Combatant, 치명타 부위는 Hitbox로 판별한다. 투사체에 발사 시점 피해·진영을 값으로 복사하며 풀 재사용 시 배율·피격 목록·충돌 제외를 복구한다. 기존 Launcher와 새 Attack이 같은 입력을 중복 실행하지 않게 한다.
- 기본 총구 Transform은 Attack.Origin에 저장하며 AttackData에는 총구·공통 공격 위치를 저장하지 않는다. 호출자가 PlayerAttackAction.TryStart(Transform origin, Vector3 worldAimPoint)으로 Muzzle을 넘기면 선딜 완료 시 최신 위치를 읽는다. PlayerAttackAction.TryStart(Vector3 worldAttackPosition, Vector3 worldAimPoint)은 고정 월드 좌표를 받고, 기존 편의 호출은 캐릭터 Transform을 사용한다. PlayerAttackAction.WorldAttackPosition은 요청의 현재 위치, Attack.WorldAttackPosition은 최근 효과 호출의 보정 전 위치, LastAttackPosition은 벽을 보정한 실제 위치다. 캐릭터 중심축의 총구 높이에서 실제 총구까지 검사하고 가장 가까운 고체 앞에서 최대 0.2m 캐릭터 쪽으로 원점을 당긴 뒤 조준 방향을 다시 계산한다. 선딜 중 총구가 삭제되면 발사 실패로 처리한다. Melee.OriginOffset은 별도 범위 중심 보정이다. Projectile.LaunchAngle은 조준 기준 0~45도이며 TempGrenade는 12도다.
- 카메라·총구 장애물 검사의 기본 대상은 Default 레이어이며 플레이어는 Ignore Raycast 레이어를 사용한다. 새 장애물과 마스크 설정을 함께 확인한다.
- 코드 초기값과 씬·프리팹의 저장값은 별개다. 지속할 설정은 Edit 모드에서 저장하고 `.meta` GUID와 직렬화된 참조를 보존한다.

### 스태미나·대시 설계 결정

2026-10-09 자원 분리 설계를 바탕으로 2026-10-15 PlayerStat·PlayerStamina를 구현했고 2026-10-26 Shift 대시 이동·후딜레이·행동 제한을 연결했다.
스태미나는 별도 `PlayerStamina` 컴포넌트로 관리한다. 체력과 스태미나의 소모·회복 규칙을 각각 관리해 강화와 밸런스 조정을 쉽게 한다.

| 구성 | 책임 |
|---|---|
| `Mortal` | 공통 HP, 무적, 피해·사망 처리 |
| `PlayerStat` | 9개 능력치 보관·변경; 최대 스태미나·이동속도·대시 거리·탄창·장전 설정의 원본 |
| `PlayerStamina` | 현재 스태미나와 PlayerStat을 참조하는 최대량, 소모 검증·소모·회복과 값 변경 알림 |
| `PlayerController` | Dash.Pressed의 수신 자격·예약·선택. 소모·실행은 SoilderDashAction에 위임 |
| `PlayerMotor` | 고정 방향·거리·시간의 대시와 충돌. 후딜은 SoilderDashAction에 위임 |

- `PlayerStamina`는 읽기 전용 `Current`, `Max`, `RecoveryDelayRemaining`, 읽기·쓰기 `RecoveryDelay`, `RecoveryRate`와 `CanConsume(float amount)`, `TryConsume(float amount)`, `Recover(float amount)`를 제공한다. `Current`는 Inspector의 current 필드로 편집한다. 검증 함수는 값을 바꾸지 않으며, 소모는 양수·유한한 양과 충분한 잔량·활성 컴포넌트일 때만 성공한다.
- 최대 스태미나는 PlayerStat, 소모량은 SoilderDashAction, 현재량·회복 대기·초당 회복량은 PlayerStamina의 Inspector에서 조절한다. 시험 기본값은 최대·시작 100, 1회 소모 25, 회복 대기 1초, 초당 20이다. 액션의 StaminaCost 0은 자원 차감 없이 대시하며 PlayerStamina.TryConsume 자체는 양수만 받는다. 최종 밸런스는 미정이다.
- 성공한 소모마다 회복 대기를 다시 시작하고, 부족하거나 유효하지 않은 요청은 잔량·대기를 바꾸지 않는다. `Time.deltaTime`으로 대기를 줄이고 대기를 초과한 프레임 시간에만 자동 회복한다. 일시정지 중 대기·자동 회복은 함께 멈춘다. 비활성화·재활성화는 잔량·대기를 초기화하지 않으며 비활성 중 자동 회복하지 않는다. 회복 지연 시간은 사용 쿨타임이 아니다.
- `Recover`는 실제 회복량을 반환하고 최대량을 넘지 않는다. 명시적 회복은 대기를 무시하되 타이머를 바꾸지 않는다. 최대량 증가 시 잔량을 유지하고 감소 시 잔량을 제한한다. 런타임 최대량·Inspector 현재량 변경은 다음 Stamina.Update에서 동기화하고 `OnChanged(current, max)`를 알린다. HUD는 이벤트를 구독하고 초기값은 직접 읽으며 자원을 직접 변경하지 않는다.
- 대시 실행 흐름은 **입력 → 접지·대시 가능한 상태 확인 → 스태미나 소모 성공 → Motor에 이동 요청**이다. 행동 제한으로 실행할 수 없는 대시에 자원만 소모하지 않도록 상태를 먼저 확인한다. 대시와 dashPostDelay가 끝나면 자원이 충분할 때 새 누름 또는 유효한 대시 선입력으로 다시 사용할 수 있다. 별도 쿨타임은 없으며 선입력은 PlayerController의 한 개 예약·최대 시간·실행 시점 조건 검사를 따른다. 대시 중 이동 방향은 고정하고 후딜 중 WASD 이동은 허용한다.
- `PlayerMortal`은 플레이어 고유의 피해 판정이나 사망 처리가 필요할 때 도입한다. 스태미나 추가를 위해 만들지는 않는다. 사망 시 입력 중단 같은 반응은 우선 `Mortal.OnDead`를 구독하는 컴포넌트로 연결할 수 있다.

### 현재 설정과 제약

- 이동 속도 6m/s, 점프 높이 1.5m, Motor 중력 25m/s². 수류탄은 별도로 프로젝트 Physics 중력을 사용한다.
- 스태미나 소모·부족 시 거절·지연 자동 회복은 구현했다. Shift는 PlayerStat의 거리 5m를 Motor의 0.2초 동안 이동하고 후딜레이 0.3초 동안 이동·점프 외 행동을 차단한다. 대시 방향·속력은 시작 시 고정하며 CharacterController 충돌·중력을 유지하며 지면에 접지 중일 때만 대시를 시작한다. 전용 애니메이션과 무적은 추가하지 않았다. 공격력·공격속도 배율은 세 Attack에 연결했으며 최대 체력은 PlayerStat.Awake에서 Mortal 시작 HP로 적용한다. 최대 탄창은 기본 발사에 연결했다.
- Bullet: 현재 저장된 프리팹은 속력 10m/s, 수명 5초, 중력 없음, 사전 생성 24개, 최대 생성 128개.
- Grenade: 속력 14m/s, 수명 2.5초, 중력 있음, 폭발 구체 연출 약 0.2초, 연출 반경 설정 4m, 사전 생성 8개, 최대 생성 32개.
- PlayerCombat는 조준 거리 100m와 PlayerActionBinding 배열을 사용하고 슬롯별 액션에 명령을 전달한다. 슬롯·액션은 PlayerCombat에, Attack·반복 여부는 공격 액션에, 기본 Origin은 Attack에 설정하며 같은 슬롯·같은 Attack 중복과 누락 참조는 활성화 시 거절한다. 실제 원점은 실행 시 현재 Muzzle.position과 장애물 보정이다. 기존 Prototype Rifle/Muzzle의 로컬 위치 `(0, 0, 0.665)`와 씬 참조를 재사용한다. 현재 임시 수류탄도 같은 총구를 쓰며 손의 별도 투척 지점은 후속 작업이다. 투척 애니메이션만 연결했으며 실제 발사 타이밍·Origin·오프셋·발사각은 유지한다. 좌·우클릭은 Pressed 또는 Held, E는 Pressed를 사용한다. TempGrenade 발사각은 12도이며 선딜 0초·후딜 1초·쿨타임 6초다. 현재 소총 후딜은 좌클릭 0.25초·우클릭 0.5초이고 좌클릭 탄퍼짐은 3도다. Controller는 PlayerAction.RepeatWhileHeld가 true이면 실행 가능한 Held를 반복 요청하고 false이면 Pressed를 사용한다. 새 액션의 반복 기본값은 false이며 Data를 공유해도 정책은 액션별로 설정한다. 요청은 해당 Attack의 시간을 지키며 발사 간격의 최종 밸런스는 후속 작업이다.
- 좌클릭은 실제 히트스캔이며 우클릭 물리 Bullet은 임시 별도 공격이다. 둘 다 Attack의 시간을 지키며 홀딩 반복 발사한다. 기본 발사에만 탄창을 사용한다. 덜 찬 탄창의 R은 기본 발사 홀딩보다 우선하고 장전 중 두 발사를 막는다. 보조 발사·스킬 사용 중 R은 예약하지 않지만 0발 자동 장전은 매 프레임 계속 시도한다.
- 수류탄은 E에 연결했다. 우클릭은 임시 Bullet 발사이며 정밀 조준은 후속 작업이다.
- `IDamageable`은 존재하지 않는다. 새 공격은 Combatant·Hitbox·DamageResolver를 거쳐 Mortal에 피해를 준다. 기존 인자 4개짜리 Pool.Spawn은 피해 정보 없는 발사이고 새 ProjectileLaunchData 전달 경로만 실제 투사체 피해를 실행한다. 프리팹 YAML의 옛 `damage/damageMask` 값은 사용하지 않는다.
- 공통 `Mortal`의 HP 기본값은 100이며 Inspector에서 시작값을 설정한다. 공격자는 `GameObject`이며 환경 피해는 null을 허용한다. 무적·사망 상태와 0 이하 피해는 무시하고 HP는 0 아래로 감소하지 않는다. `Dead()` 직접 호출은 무적과 무관하게 HP를 0으로 만들며, 이동·사격 중단·애니메이션·객체 삭제·부활은 처리하지 않는다. 현재 SampleScene의 Player에는 Mortal·Combatant를 연결했다. 적 AI·프리팹의 체력·사망 행동 연결은 후속 작업이다.
- X Bot은 임시 시각 에셋이며 현재 씬에 적용했다. 기존 `Run Backwards`는 삭제하고 `Run Backward`로 교체했다. 이전 원본 루트 검사에서는 수평 누적 변위가 한 주기 약 0.0034mm, 5주기 약 0.0175mm, 주기 중 최대 편차가 약 2.43cm였다. 현재는 Animator의 Apply Root Motion을 끄고 Motor가 이동한다. 상체 마스크·양팔 IK·점프 연결과 기본 애니메이션 작업은 완료했으며 소총은 기본 도형으로 만든 임시 메시다. 최종 무기 에셋·손가락 정렬은 별도 시각 완성도 작업이고, 남은 Soldier 애니메이션은 Dash·Skill2·Ultimate다.
- 2026-10-12 대기·전후좌우 이동 5개 클립에 적용한 Rotation·Position(Y/XZ)의 Bake Into Pose와 Loop Pose를 유지한다. Rotation·XZ는 Original 기준이며 2026-10-14 후속 요청으로 Rotation Offset만 모두 0으로 되돌렸다. 평균 골반 방향을 전방에 맞추던 보정은 제거했으며, 이전 정렬값과 측정은 플레이어 조작 가이드 14절의 과거 기록이고 현재 설정은 16절이다. FBX·Avatar·클립 GUID와 프레임 범위를 유지했고 Soldier Visual의 Transform은 변경하지 않았다. 점프·사격·재장전의 임포트 설정은 기존 값이다. 상체 보정은 플레이어의 `transform.forward` 기준이며 소총 자세의 몸통 비틀림을 유지한다. 몸통 보정은 기존 80도 제한·Spine Weight를 사용하고 총구와 양손 IK가 실제 조준점을 맞춘다. 루트 누적과 반복 이음새를 다루는 임포트 보정은 유지하지만 클립별 기본 골반 방향이 서로 같다고 가정하지 않는다.
- 점프 3종은 X Bot Avatar를 공유하며 `Jump Up`은 약 0.533초, `Jump Loop`는 1초, `Jump Down`은 약 0.667초다. 모두 60 FPS·압축 Off이며 Loop만 Loop Time을 켰다. 원본 루트 이동은 에셋에 남아 있지만 씬 Animator에서는 적용하지 않는다. 실제 접지·수직 속도로 상승·공중·착지 상태를 전환한다. 재생 속도는 Up 1.5배, Down 1.4배이며 기본 점프 애니메이션 작업은 2026-10-15 사용자 확인으로 완료 처리했다.
- 장전 시간은 PlayerStat.reloadDuration / reloadSpeedMultiplier초다. 기본 3.3초·1배이며 완료할 때 현재 MaxMagazine까지 채운다. 최대 탄창 증가 시 현재 탄환은 유지하고 감소 시 제한한다. 시작 이벤트로 재생하는 장전 애니메이션은 reloadSpeedMultiplier만 배속에 사용한다. 완료·취소 시 상체를 복귀시키고 Time.timeScale=0이면 타이머·애니메이션·조준 평가를 멈춘다.
- E의 TempGrenade는 2.5초 후 기본 범위 피해 100과 폭발 구체 연출을 실행한다. 근접·폭발의 넉백·엄폐물 차단·거리별 감쇠는 미구현이다. 수류탄 조준은 초기 발사각만 보정하며 정확한 착탄 탄도 계산은 없다.
- 풀은 프리팹별 고정 등록 방식으로 동적 등록·자동 축소를 지원하지 않는다. 투사체끼리의 충돌 제외도 별도로 설정하지 않았다. 조준은 32개 버퍼의 `RaycastNonAlloc`을 사용하며 버퍼가 가득 찬 경우 정확한 최근접 판정을 위해 `RaycastAll`로 재검사한다.
- 임시 `OnGUI` 크로스헤어와 `PlayerPrototypeSetup`, `PlayerInputValidation`, `ProjectilePrototypeSetup`은 제거된 상태다. 삭제된 도구의 실행을 전제로 작업하지 않는다.

## 5. 앞으로의 구현 순서

사용자가 지정한 작업을 먼저 진행하되, 전체 개발은 다음 흐름을 기준으로 한다.

1. **조작 확인:** 입력·이동·카메라 기반과 Soldier 기본 애니메이션 작업·이동 문제 해결은 완료했다. 후속 Soldier 애니메이션은 Dash·Skill2·Ultimate이며 새 행동·전투 기능을 연결하면서 실제 조작 감각을 확인한다.
2. **최소 전투:** 체력·피해·사망, 소총 사격·탄창·재장전, 근접형·원거리형 적을 작은 테스트 아레나에서 연결하고 검증한다.
3. **반복 플레이:** 웨이브·클리어 판정·강화 3개 중 선택·강화 적용을 만들고 구현된 Shift 대시의 조작 감각과 밸런스를 확인한다. E의 임시 수류탄을 바탕으로 최종 밸런스와 행동 제한을 확정한다. F의 Skill2 효과, 정밀 조준과 행동 제한도 구체화한다.
4. **한 판 완성:** 10웨이브, 보스 패턴과 단계 변화, 성공·실패·결과·재시작 및 상태 초기화를 완성한다.
5. **완성도 개선:** HUD·보상·결과 UI, 사운드·효과·애니메이션, 밸런스, 테스트와 성능 개선을 진행한다.
6. **확장:** 초기 흐름 완성 후 Assassin·Tank를 진행하고 Charger·강화 개체 등 추가 콘텐츠를 검토한다.

아직 확정하지 않은 항목은 해당 기능 개발 시 구체화하고 결정 내용을 이 문서 또는 관련 상세 가이드에 남긴다.

- 피해량, 체력, 이동속도, 탄창·재장전·발사 간격, 스킬 쿨타임.
- 스태미나 최대량·대시 소모량·초당 회복량·회복 지연 시간, 대시 거리·속도·무적 여부. 별도 PlayerStamina 컴포넌트, 게임 시간 기준 자동 회복, 쿨타임 없이 스태미나를 소모하는 이동기 구조는 확정했다.
- Skill2 효과, 정밀 조준 감각, 대시 외 재장전·스킬 간 행동 제한, 궁극기 효과·발동 조건.
- 웨이브별 적 수·배치·등장 간격, 보스 세부 패턴·단계 전환 조건.
- 강화 목록·등급·등장 확률·중첩·선행 조건.
- UI 구성, 에셋, 애니메이션과 연출.
- 새 기능의 코드 구조, 데이터 관리, 테스트와 최적화 방법.

## 6. 검증 방법과 기록

변경한 기능에 맞게 컴파일, 씬·프리팹 참조, 실제 동작을 확인한다. 문서만 변경한 경우 내용·경로·링크 검사를 수행한다. 플레이 감각은 `SampleScene`의 Game 뷰에서 확인한다.

자동 검증은 `ProjectU.Editor.ProjectileValidation.Run()`, `ProjectU.Editor.MortalValidation.Run()`, `ProjectU.Editor.PlayerAnimationValidation.Run()`, `ProjectU.Editor.PlayerStaminaValidation.Run()`, `ProjectU.Editor.AttackValidation.Run()`, `ProjectU.Editor.PlayerAttackValidation.Run()`, `ProjectU.Editor.PlayerDashValidation.Run()`이다. 공격 검증 범위와 실행 방법은 공격 구현 가이드를 참고한다. 투사체 검증은 동일 프로젝트를 연 Unity 에디터가 닫힌 상태에서 다음 PowerShell 명령을 사용한다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe' -batchmode -nographics -projectPath 'C:/UnityProjects/ProjectU' -executeMethod ProjectU.Editor.ProjectileValidation.Run -logFile 'C:/UnityProjects/ProjectU/Logs/ProjectileValidation.log'
```

검증기는 플레이 모드 진입 후 자체적으로 성공 코드 0 또는 실패 코드 1로 Unity를 종료한다. 이 흐름에는 `-quit`을 추가하지 않는다. 수량·타이밍을 바꾸면 검증 기대값도 함께 수정한다.

- 검사 범위: 사전 생성 32개, 총알 직선 비행, 수류탄 중력, 발사자 충돌 제외·복구, 총알 바닥 충돌 반환, 수류탄 지연 폭발·반환, 총알 128개 용량 제한, 전체 반환·재사용·풀 비활성화 반환.
- 기존 `Logs/ProjectileValidation.log`에서 `PROJECTILE VALIDATION PASSED` 기록을 확인했다. 플레이어 조작 가이드에도 이전 컴파일·씬 참조·바닥 충돌체 검증 기록이 있다.
- 2026-10-07 문서 작성에서는 소스·씬·프리팹·기존 기록만 대조했으며 Unity 배치 검증이나 새 플레이 테스트를 실행하지 않았다.
- 투사체 자동 검증은 피해 처리, 실제 클릭, 시각 효과 외형, 정확한 탄도 착탄, 총알의 무충돌 수명 만료를 각각 독립적으로 확인하지 않는다.
- 체력 검증은 에디터 메뉴 `ProjectU > Validation > Validate Mortal` 또는 `-executeMethod ProjectU.Editor.MortalValidation.Run`으로 실행한다. 시작 상태, 피해 감소·공격자 전달, 무적·유효하지 않은 피해 무시, 초과 피해·이벤트 순서, 중복 사망 방지, null 공격자, 치명 피해 이벤트 재진입·구독자 예외를 검사한다. 씬을 저장하거나 캐릭터 조작을 변경하지 않으며 배치 실행 시 성공 코드 0 또는 실패 코드 1로 종료한다.
- 2026-10-08 작업에서 Unity 6000.3.25f1의 임시 별도 프로젝트(`Temp/MortalValidationProject`)로 새 두 스크립트를 컴파일하고 `MortalValidation.Run()`을 직접 실행했다. 종료 코드 0과 `Logs/MortalValidation.log`의 `MORTAL VALIDATION PASSED`를 확인했다. 당시 기존 투사체 검증과 SampleScene 수동 플레이는 다시 실행하지 않았다.
- 이전 수동 확인: WASD·마우스·점프, 좌·우클릭 단발, 벽 충돌, 수류탄 튕김·폭발 연출, Esc 입력 중단, 재잠금 클릭의 사격 억제, 크로스헤어 미표시.
- 2026-10-09 입력 변경 작업에서는 Unity 6000.3.25f1로 원본 프로젝트를 배치 컴파일했다. 종료 코드 0과 `Logs/PlayerInputCompile.log`의 컴파일 완료·정상 종료를 확인했으며 C# 컴파일 오류는 없었다. Shift=Dash·E=Skill1·F=Skill2 바인딩, 입력 수집·초기화, 8개 버튼 로그 인덱스를 소스로 대조했고 씬의 Log Input 활성 설정, 이전 매핑 제거, 문서 경로·링크와 `git diff --check`를 확인했다. 실제 키 입력·SampleScene 수동 플레이와 기존 투사체·체력 검증은 당시 실행하지 않았다.
- 2026-10-09 작업의 후속 수동 확인 항목: 좌·우 Shift의 Dash, E의 Skill1, F의 Skill2가 각각 Pressed·Held·Released로 출력되는지, 동시 입력의 로그가 독립적인지, Esc·포커스 상실·일시정지 시 입력이 초기화되는지 Game 뷰에서 확인한다. 당시 세 버튼은 실제 이동·스킬·스태미나 처리를 실행하지 않았다. 2026-10-15에는 Shift 자원 소모 연결을 추가했다.
- 2026-10-09 스태미나 설계 문서 반영에서는 AGENTS.md만 수정했다. 별도 PlayerStamina 컴포넌트와 클래스별 책임, 소모·회복 API 및 대시 실행 흐름을 미구현 설계로 기록했다. 문서 내용·참조 경로·링크와 `git diff --check`를 확인했으며 Unity 컴파일·자동 검증·수동 플레이는 이번 문서 작업에서 실행하지 않았다.
- 2026-10-10 에셋 임포트에서는 사용자 제공 FBX 8개의 복사 전후 SHA256 일치와 Unity 6000.3.25f1 배치 임포트 종료 코드 0을 확인했다. X Bot의 유효한 Humanoid Avatar, 스킨드 메시 2개와 URP/Lit 머티리얼 2개, 모든 애니메이션의 동일한 65개 Mixamo 본 구조, 7개 클립의 60 FPS·양수 재생 길이·Humanoid 변환을 확인했다. 임시 Preview Scene에서 각 클립의 한 주기를 60회 샘플링해 실제 Humanoid 포즈 변화와 유한한 값을 확인했고, `Logs/SoldierAssetImport.log`의 `SOLDIER ASSET IMPORT PASSED` 및 `Logs/SoldierAssetImportReport.json`을 기록했다. 대기·달리기는 Loop Time을 켜고 사격·재장전은 끈 상태로 임포트했으며 원본 루트 이동은 유지했다. `Run Backwards`의 큰 루트 이동을 후속 제약으로 기록했다. 임시 에디터 검사 스크립트는 검증 후 제거했다. SampleScene의 SHA256과 기존 씬·프리팹·런타임 코드·패키지·프로젝트 설정의 변경 없음, 문서 참조와 `git diff --check`를 확인했다. Animator Controller·상체 마스크·플레이어 연결과 수동 시각 검증, 기존 투사체·체력 검증은 이번 작업에서 실행하지 않았다.
- 2026-10-10 후속 후진 교체에서는 이전 `Run Backwards` FBX와 meta 경로를 제거하고 새 `Run Backward`를 임포트했다. 기존 GUID와 클립 내부 ID, X Bot Avatar 참조를 유지하고 새 파일의 프레임 범위 0~30, Loop Time 켬, 압축 Off를 적용했다. 새 FBX의 원본·복사본 SHA256 일치, 동일한 65개 본과 Humanoid 재생, Unity 배치 종료 코드 0을 확인했다. 루트 이동 보정을 적용하지 않고 `Apply Root Motion`을 켠 임시 Preview Scene에서 5주기를 주기당 60회 샘플링했다. 수평 변위는 한 주기 약 0.0034mm, 5주기 약 0.0175mm이고, 한 주기 중 최대 수평 편차는 약 2.43cm다. 검사 결과는 `Logs/SoldierBackwardImport.log`의 `SOLDIER BACKWARD IMPORT PASSED`와 `Logs/SoldierBackwardImportReport.json`에 기록했다. 기존 모델·다른 클립·씬·코드·설정 등 81개 파일의 작업 전후 SHA256 일치를 확인했으며 임시 검증 스크립트를 제거하고 문서·경로와 `git diff --check`를 확인했다. 씬 적용·수동 시각 검증과 기존 투사체·체력 검증은 수행하지 않았다.
- 2026-10-10 점프 추가 임포트에서는 사용자 제공 `X Bot@Jump Up.fbx`·`X Bot@Jump Loop.fbx`·`X Bot@Jump Down.fbx`의 원본·복사본 SHA256 일치, X Bot Avatar 참조와 동일한 65개 본 구조, 세 클립의 60 FPS·Humanoid 변환·저장된 반복 설정을 확인했다. Unity 6000.3.25f1 배치 컴파일·임포트 종료 코드 0과 `Logs/SoldierJumpImport.log`의 `SOLDIER JUMP IMPORT PASSED`, `Logs/SoldierJumpImportReport.json`을 기록했다. 루트 Bake를 끄고 `Apply Root Motion`을 켠 임시 Preview Scene에서 Up·Down은 한 주기당 120회, Loop는 5주기 총 600회 샘플링해 포즈 변화·유한한 근육 및 본 Transform 값을 확인했다. 기존 Assets·Packages·ProjectSettings의 131개 파일은 작업 전후 SHA256이 일치했다. 임시 검사 스크립트와 meta를 제거하고 문서 경로·링크와 `git diff --check`를 확인했다. 씬·Animator 연결, 수동 시각 검증과 기존 투사체·체력 검증은 이번 작업에서 수행하지 않았다.
- 2026-10-10 애니메이션 씬 적용에서는 Unity 6000.3.25f1로 Soldier Animator·상체 마스크·임시 소총과 씬 참조를 생성하고 컴파일했다. `PlayerAnimationValidation.Run()`의 플레이 모드 검사에서 전후좌우·대각 이동과 카메라 기준 회전, 점프 3상태, 12개 Yaw/pitch 조합의 발사, 점프 중 발사, 재장전의 실제 사격 상태 전환, 일시정지·재장전 재개, 총구 앞 장애물, 가까운 경계 벽, 같은 프레임의 카메라 급회전·이동 사격을 확인했다. 총 17발의 발사, 총구·투사체 방향 최대 오차 약 0.028도, 왼손 목표 최대 거리 약 0.415mm를 기록했으며 종료 코드 0과 `Logs/PlayerAnimationValidation.log`의 `PLAYER ANIMATION VALIDATION PASSED`, `Logs/PlayerAnimationValidationReport.json`을 확인했다. Game 카메라와 관찰 카메라의 PNG 12개를 렌더링하고 상하 조준·점프·재장전·벽 근처 자세를 이미지로 확인했다. 기존 투사체 검증도 다시 실행해 종료 코드 0과 `Logs/ProjectileAnimationRegression.log`의 `PROJECTILE VALIDATION PASSED`를 확인했다. 기존 FBX·임포트 설정·투사체 프리팹·패키지·프로젝트 설정을 유지했고 임시 생성 스크립트와 불필요하게 생성된 SceneTemplateSettings를 제거했다. 문서·참조 경로·링크와 `git diff --check`를 확인했다. 자동 입력 주입으로 검사했으며 실제 키보드·마우스 수동 플레이와 기존 Mortal 검증은 이번 작업에서 실행하지 않았다.

- 2026-10-12 방향 정렬에서는 열린 원본 에디터를 유지하고 별도 프로젝트(`Logs/SoldierAlignment_20261012/Project`)에서 Unity 6000.3.25f1로 컴파일·임포트·자동 플레이를 직접 실행했다. 대기·이동 5개 클립의 평균 골반 Yaw 오차는 최대 약 0.00015도이며, Apply Root Motion을 켠 연속 60주기 평가에서 위치·회전 누적은 모두 0이었다. `PlayerAnimationValidation.Run()`에 먼 전방 조준점의 실제 몸통 방향 검사를 추가했으며 이동·점프·17발 사격·재장전 중단·일시정지·장애물·급회전을 통과했다. 몸통·총구 최대 오차는 각각 약 0.028도, 왼손 최대 오차는 약 0.416mm였다. Game·관찰 PNG 12개를 렌더링했고 상하 조준·점프·재장전·가까운 벽 이미지를 확인했다. 전진·우측 이동은 조준 On/Off 각각 60주기를 추가 검사해 플레이어·Visual 회전, Visual·메시 Transform 로컬 위치의 누적 변화 0을 확인했다. `Logs/SoldierAlignment_20261012/`에 임포트·플레이·반복 검사 로그와 JSON·PNG를 보존했다. 원본에 반영한 5개 meta의 설정과 검증 복사본의 설정을 대조했고 씬·FBX·Animator 참조를 유지했다. 실제 키 입력 수동 플레이와 기존 투사체·Mortal 독립 검증은 이번 작업에서 실행하지 않았다.

- 2026-10-14 상체 보정 복원에서는 PlayerAimController의 기준을 `transform.forward`로 되돌리고 어깨·목 방향 계산·Neck 본 의존성을 제거했다. 실제 몸통 정면을 요구하던 검증과 `maximumTorsoAimAngle` 기록을 제거했으며, 총구·투사체·양손 IK 검증과 기존 CS0108 필드 이름 수정은 유지했다. 원본 C# 프로젝트를 직접 컴파일해 경고 0·오류 0을 확인했고, 별도 복사본에서 Unity 6000.3.25f1의 `PlayerAnimationValidation.Run()`을 직접 실행해 종료 코드 0과 17발 사격·이동·점프·재장전 중단·일시정지·장애물·급회전 통과를 확인했다. 총구 최대 오차는 약 0.028도, 투사체 오차는 0도, 왼손 목표 최대 거리는 약 0.262mm였다. 기록은 `Logs/PlayerRootAim_20261014/`에 보존했다. 5개 이동 클립 meta·씬·Controller·기존 블렌딩 로그를 포함한 다른 프로젝트 파일은 작업 전후 SHA256으로 대조했다. 임포트 보정의 이전 60주기 기록은 유지하며 이번에는 해당 반복 검사·이미지 렌더링·실제 키 입력 수동 플레이·투사체·Mortal 독립 검증을 다시 실행하지 않았다.

- 2026-10-14 후속 Rotation Offset 롤백에서는 5개 이동 클립 meta의 `orientationOffsetY`만 각각 0으로 변경했다. 원본과 작업 전 meta를 대조해 Loop Pose·Bake Into Pose·Original 기준·GUID·클립 ID·프레임 범위 등 다른 값이 동일함을 확인했다. 나머지 프로젝트 파일 148개는 SHA256이 일치하며 런타임 코드·씬·Controller도 유지했다. 별도 복사본에서 Unity 6000.3.25f1 임포트·컴파일과 `PlayerAnimationValidation.Run()`을 직접 실행해 종료 코드 0, 이동·점프·17발 사격·재장전 중단·일시정지·장애물·급회전 통과를 확인했다. 총구 최대 오차는 약 0.028도, 투사체 약 0.020도, 왼손 목표 최대 거리는 약 0.408mm였다. 기록은 `Logs/SoldierOffsetRollback_20261014/`에 보존했다. 평균 골반 방향의 정면 정렬은 현재 검증 목표가 아니며 이전 정렬 후 0도 기록은 과거 설정의 결과다. 루트 60주기 반복 검사·이미지 렌더링·실제 키 입력 수동 플레이·투사체·Mortal 독립 검증은 이번에 다시 실행하지 않았다.

- 2026-10-15 문서 갱신에서는 사용자의 Soldier 이동 문제 해결·작업 완료 확인을 반영해 기본 애니메이션 작업을 완료로 기록하고, 남은 Soldier 애니메이션을 Dash·Skill1·Skill2·Ultimate 4개로 정리했다. 이는 사용자 확인 기록이며 에이전트가 새 수동 플레이를 수행한 결과가 아니다. Dash·스킬·궁극기의 실제 기능은 코드상 입력·로그 단계임을 대조해 기존 미구현 현황을 유지했다. AGENTS.md와 플레이어 조작 가이드의 날짜·현재 상태·후속 작업을 동기화하고 문서 링크·경로와 `git diff --check`, 코드·씬·에셋의 작업 전후 SHA256을 확인했다. 이번에는 문서만 수정했으며 Unity 컴파일·자동 검증·새 수동 플레이를 실행하지 않았다.

- 2026-10-15 PlayerStat·PlayerStamina 작업에서는 능력치 7개와 자원 API·지연 자동 회복·값 변경 이벤트를 구현하고 SampleScene의 Player에 연결했다. Controller의 Dash.Pressed는 1회 25를 소모하며 실제 대시 이동은 추가하지 않았다. 이동속도 6m/s를 Motor에서 PlayerStat으로 이전했다. 열린 원본 에디터는 유지하고 `Logs/PlayerStamina_20261015/Project`의 복사본에서 Unity 6000.3.25f1로 컴파일·자동 플레이를 직접 실행했다. `PlayerStaminaValidation.Run()`의 종료 코드 0과 `PlayerStaminaValidation.log`의 `PLAYER STAMINA VALIDATION PASSED`를 확인했다. 7개 능력치·씬 참조·Shift 두 바인딩, 합성 입력의 누름당 소모·홀딩 반복 방지·대시 변위 없음, 부족·유효하지 않은 소모·정확한 소진, Inspector 시작량, 직접 회복·최대량 제한·최대량 변경, 회복 대기·재사용 시 초기화·초당 회복·회복률 0, 일시정지·재개·이벤트를 검사했다. 기존 `PlayerAnimationValidation.Run()`도 실행해 종료 코드 0과 `PlayerAnimationRegression.log`의 `PLAYER ANIMATION VALIDATION PASSED`를 확인했다. 이동·점프 3상태·17발 사격·재장전 중단·일시정지·장애물·급회전을 통과했으며 총구·투사체 최대 오차는 약 0.028도였다. 씬 ID·GUID·참조, 문서 링크·날짜, 검증 복사본과 원본의 관련 소스 SHA256 일치 및 `git diff --check`를 확인했다. 이번 검증은 합성 입력을 사용한 자동 플레이이며 실제 키보드·마우스 수동 플레이·이미지 렌더링과 투사체·Mortal 독립 검증은 다시 실행하지 않았다. 실제 대시·HUD·전투 능력치의 피해·HP·탄약 적용은 남아 있다.

- 2026-10-15 애니메이션 블렌드 로그 제거에서는 PlayerAnimationController의 클립·레이어 가중치 로그 출력, 전용 LateUpdate·문자열 버퍼·클립 목록·타이머와 Inspector 설정을 삭제했다. 이동·점프·사격·재장전 애니메이션 실행 코드는 유지했다. 원본 Assembly-CSharp.csproj를 직접 컴파일해 경고 0·오류 0과 종료 코드 0을 확인했으며 Assets에서 관련 로그 코드·설정의 남은 참조가 없고 `git diff --check`가 통과함을 확인했다. 상세 가이드도 동기화했다. 이번에는 Unity 자동 플레이·수동 플레이를 다시 실행하지 않았으며 앞선 자동 플레이 기록과 구분한다.

- 2026-10-15 PlayerStat 후속 변경에서는 방어력 필드·프로퍼티를 제거하고 정수 AttackPower를 float AttackMultiplier(기본 1, 최소 0)로 변경했다. SampleScene 저장값을 attackMultiplier: 1로 바꾸고 기존 공격력·방어력 저장값을 제거했으며 PlayerStaminaValidation의 기대값·표기를 6개 능력치에 맞췄다. ProjectU.sln의 런타임·에디터 코드를 직접 컴파일해 경고 0·오류 0을 확인했다. 기존 별도 복사본에 최신 Assets를 동기화해 Unity 6000.3.25f1의 PlayerStaminaValidation.Run을 다시 실행했고, 종료 코드 0과 `Logs/PlayerStamina_20261015/PlayerStatMultiplierValidation.log`의 `PLAYER STAMINA VALIDATION PASSED: six stats`를 확인했다. 이전 능력치의 Assets 참조 없음과 `git diff --check`도 확인했다. 앞선 7개 능력치 검증 기록은 변경 전 결과다. 이번에는 실제 키보드·마우스 수동 플레이와 이동·애니메이션·투사체·Mortal 독립 검증을 다시 실행하지 않았다. 공격력 배율은 값 제공 단계이며 실제 피해 계산 연결은 남아 있다.

- 2026-10-15 공격력 배율 이름 정리에서는 공개 프로퍼티를 AttackMultiplier, 비공개 직렬화 필드를 attackMultiplier로 통일했다. SampleScene·기존 검증 코드·상세 가이드의 참조를 함께 변경했고 기존 직렬화 필드 이름을 읽을 수 있도록 FormerlySerializedAs를 추가했다. float 형식·기본값 1·최소값 0과 값 제공 단계라는 구현 범위는 유지했다. ProjectU.sln의 런타임·에디터 코드를 직접 컴파일해 경고 0·오류 0을 확인했으며 이전 이름은 직렬화 이관 속성 한 곳에만 남아 있고 `git diff --check`가 통과했다. 이번 이름 변경에서는 Unity 자동·수동 플레이를 다시 실행하지 않았다.

- 2026-10-16 공통 공격 작업에서는 Combatant·Faction·Hitbox, AttackData와 3종 ScriptableObject 데이터, Attack과 HitscanAttack·ProjectileAttack·MeleeAttack, DamageContext·DamageResolver·ProjectileLaunchData 및 시험 데이터 에셋 3개를 구현했다. 선딜·후딜·쿨타임, 진영·헤드샷·정수 피해, 탄퍼짐·적 관통수, 투사체 관통·크기·폭발 배율·풀 재사용, 원·부채꼴 근접 판정을 지원한다. 기존 투사체 API를 유지하고 새 공격 정보를 전달할 때만 피해를 실행한다. 새 공격은 SampleScene 입력에 연결하지 않았고 기존 씬·프리팹·조준·애니메이션·입력 코드를 보존했다. 열린 원본 에디터를 유지한 별도 검증 복사본에서 Unity 6000.3.25f1의 AttackValidation 43개 검사, 기존 ProjectileValidation과 PlayerAnimationValidation을 직접 실행해 모두 종료 코드 0을 확인했다. 로그는 `Logs/Attack_20261016/`에 보관했다. 같은 복사본의 런타임·에디터 C# 직접 빌드는 경고 0·오류 0이었다. 배치 시작의 Unity Search 인덱스 내부 예외는 공격 코드 외부의 문제로 로그에 기록했으며, 이후 3종 검증은 모두 완료했다. 공격 구현 가이드와 기존 상세 가이드를 동기화했다. 실제 수동 플레이·새 공격 시각 연출 검수는 수행하지 않았으며 입력·AI·캐릭터 씬 연결, 탄약·재장전·공격 간 행동 제한은 후속 작업이다.

- 2026-10-16 공격 위치·발사각 후속 작업에서는 공통 AttackData.AttackPosition(Vector3)을 추가하고 Attack.WorldAttackPosition을 실제 3종 공격 원점에 연결했다. AttackOrigin이 지정되면 그 Transform, 없으면 캐릭터 Transform 기준 로컬 좌표이며 실행 시 위치·회전·스케일을 반영한다. 예제 에셋 3개에는 0을 저장해 기존 위치를 보존했다. 기존 근접 OriginOffset은 추가 중심 보정으로 유지했다. 투사체 LaunchAngle은 기존 구현을 유지하고 Inspector 설명과 실제 적용 검사를 보강했다. 별도 복사본에서 AttackValidation을 직접 다시 실행해 49개 검사·종료 코드 0, C# 런타임·에디터 빌드 경고 0·오류 0을 확인했다. 로그는 `Logs/Attack_20261016/AttackPositionValidation.log`다. 기존 투사체·애니메이션 독립 검증은 앞선 기록이며 이번 후속 변경에서는 재실행하지 않았다. 씬·프리팹·입력 코드는 유지했고 수동 플레이는 수행하지 않았다. 공격 가이드와 관련 문서를 동기화했다.

- 2026-10-16 총구 오프셋 정정에서는 사용자의 설명에 따라 새 Attack의 AttackOrigin Transform 참조를 제거하고 AttackPosition을 항상 캐릭터 로컬 오프셋으로 사용하도록 통일했다. 월드 위치는 transform.TransformPoint(Data.AttackPosition)이며 조준점이 없을 때의 방향도 캐릭터 forward를 사용한다. 소총·수류탄 예제에는 기존 Launcher의 저장된 muzzleOffset `(0.35, 0.55, 0.65)`를 입력했다. 별도 검증 복사본의 AttackValidation은 50개 검사·종료 코드 0, 직접 C# 런타임·에디터 빌드는 경고 0·오류 0이었다. `Logs/Attack_20261016/MuzzleOffsetValidation.log`에 기록했다. 기존 씬 Launcher의 실제 총구 참조·조준·입력 연결은 유지했고 관련 가이드를 현재 기준으로 수정했다. 앞선 49개 검사는 선택한 기준 Transform을 허용하던 정정 전 결과다. 이번에는 수동 플레이와 기존 투사체·애니메이션 독립 검증을 재실행하지 않았다.

- 2026-10-16 임시 공격 입력 연결에서는 TempPrimaryFire(히트스캔 30), TempSecondaryFire(기존 Bullet 50), TempGrenade(기존 Grenade 폭발 100) ScriptableObject를 만들고 SampleScene의 좌클릭·우클릭·E에 연결했다. Player에 Mortal·Combatant(Player)·HitscanAttack 1개·ProjectileAttack 2개를 저장하고 기존 Launcher는 조준·능력치·입력 어댑터로 바꿨다. 기존 직접 Fire 경로를 제거하고 Attack 성공 이벤트로 좌·우클릭 사격·재장전 중단을 연결했다. 선딜·후딜·쿨타임 0, 누름당 한 번, 캐릭터 로컬 오프셋을 사용한다. 원점 앞 고체 차단·보정 방향을 공통 Attack에 연결하고 PlayerStat 공격력·공격속도 배율을 전달했다. 두 가이드를 공격 구현 가이드로 합치고 중복·과거 프로토타입 설명을 삭제했으며 별도 투사체 가이드 파일을 제거했다. 별도 복사본에서 PlayerAttackValidation 22개·AttackValidation 50개·수정한 PlayerAnimationValidation의 히트스캔 17회·기존 ProjectileValidation·PlayerStaminaValidation을 직접 실행해 모두 종료 코드 0을 확인했다. 로그는 `Logs/TempAttacks_20261016/`에 보관했다. 직접 C# 빌드는 경고 0·오류 0이었다. 배치의 Unity Search 인덱스 내부 예외는 기능 검증 이후 결과와 구분해 통합 가이드에 기록했다. 신규 적 AI·영구 피격 타깃·투척 애니메이션·탄약·사망 이동 중단은 추가하지 않았고 실제 수동 플레이는 수행하지 않았다.

- 2026-10-16 홀딩 발사 후속 변경에서는 Launcher가 PrimaryFire·SecondaryFire의 Pressed 또는 Held에서 TryAttack을 요청하도록 수정했다. E의 Skill1은 Pressed만 유지했다. 조준 자세와 새 시각 재장전의 사격 우선순위에도 Held를 반영했다. 공격 시간·상태 검사는 공통 Attack이 담당하며 현재 Temp 시간 0은 각 공격이 프레임마다 최대 한 번 실행됨을 뜻한다. 버튼 해제는 새 요청을 중단하고 수락한 선딜은 완료할 수 있다. 열린 원본 에디터를 유지한 별도 복사본에서 Unity 6000.3.25f1로 PlayerAttackValidation 31개, AttackValidation 50개, PlayerAnimationValidation의 이동·점프·17회 히트스캔·재장전·조준 검사를 직접 실행해 모두 종료 코드 0을 확인했다. 짧은 누름, 두 공격의 Held 단독 반복, 시간 제한, 해제·정지·재개·사망, E 홀딩 반복 방지를 검사했다. 런타임·에디터 C# 빌드는 경고 0·오류 0이었다. 로그는 `Logs/HeldFire_20261016/`에 보관했다. 배치 시작 시 Unity Search 내부 인덱스 예외는 계속 기록되지만 기능 검증은 통과했다. 공격·플레이어 가이드를 갱신했다. 실제 키보드·마우스 수동 플레이, ProjectileValidation·PlayerStaminaValidation 독립 검증은 이번에는 재실행하지 않았다. 발사 간격 밸런스·탄약·실제 재장전은 후속 작업이다.

- 2026-10-16 탄퍼짐 데이터 정리에서는 HitscanAttackData의 UseSpread 필드·프로퍼티와 에셋 저장값을 제거했다. SpreadAngle > 0이고 연속 발사 임계값에 도달했을 때만 탄퍼짐을 적용한다. 기존 비활성 TempPrimaryFire는 SpreadAngle을 3에서 0으로 옮겼고 SoldierRifleAttack 예제는 3도를 유지했다. 기존 검증의 탄퍼짐 비활성 설정도 각도 0으로 바꾸고, 임계값 이후에도 0도이면 직진하는 검사와 저장된 에셋 각도 검사를 추가했다. 현재 Bullet 프리팹의 속력 10m/s·수명 5초는 보존했으며, 이 설정에서 기존 고정 대기 검사가 실패해 검증의 비행 대기를 거리/속력 기준으로 보완했다. 최초 실패 기록은 `Logs/SpreadAngle_20261016/AttackValidation.Initial.log`다. 열린 원본 에디터를 유지한 별도 복사본에서 Unity 6000.3.25f1로 최종 AttackValidation 52개·PlayerAttackValidation 31개를 직접 실행해 모두 종료 코드 0을 확인했고 런타임·에디터 C# 빌드는 경고 0·오류 0이었다. 최종 로그는 같은 폴더의 `AttackValidation.log`·`PlayerAttackRegression.log`다. 관련 소스·에셋 6개의 검증 복사본 SHA256 일치와 참조 제거를 확인했다. Unity Search 내부 인덱스 예외는 배치 시작에 계속 기록되지만 기능 검증은 통과했다. 공격 가이드를 갱신했다. 수동 플레이·애니메이션·투사체·스태미나 독립 검증은 이번에는 재실행하지 않았으며 발사 간격 밸런스·탄약·실제 재장전은 남아 있다.

- 2026-10-16 히트스캔 궤적 디버그 표시에서는 HitscanAttack에 표시 여부·색상·유지 시간 설정을 추가했다. 기본 켬·노란색·1초이며 실제 Raycast 시작점에서 탄퍼짐 적용 방향으로 Debug.DrawLine을 그린다. 실제 판정 결과에서 벽 또는 허용 대상 수를 소진한 적의 표면을 끝점으로 사용하고, 차단되지 않으면 최대 사거리까지 표시한다. LastShotEndPoint로 마지막 끝점을 제공한다. 기존 진영·중복 부위·일반 Trigger·Projectile 통과 규칙과 피해·관통 수는 유지했다. 각 뷰의 Gizmos를 켜서 확인하며 게임용 탄환·트레이서는 추가하지 않았다. 열린 원본 에디터를 유지한 별도 복사본에서 Unity 6000.3.25f1의 AttackValidation을 직접 실행해 61개 검사·종료 코드 0을 확인했다. 첫 대상·추가 관통 대상·Trigger 피격 부위·벽 표면, 한도보다 적은 대상, 빗나감·실제 탄퍼짐 방향·오프셋 원점의 끝점을 검사했다. 런타임·에디터 C# 빌드는 경고 0·오류 0이었다. 로그는 `Logs/HitscanDebugLine_20261016/AttackValidation.log`다. 변경 소스 2개의 검증 복사본 SHA256 일치·문서 링크·diff 검사를 확인했고 공격 가이드를 갱신했다. Unity Search 내부 인덱스 예외는 배치 시작에 기록됐지만 이후 기능 검증은 통과했다. 실제 디버그 선의 시각 확인·수동 플레이·다른 독립 검증은 이번에는 수행하지 않았다. 탄약·실제 재장전·발사 간격 밸런스는 후속 작업이다.

- 2026-10-16 실행 시 총구 전달 리팩토링에서는 AttackData.AttackPosition과 6개 에셋의 위치 필드를 제거했다. TryAttack에 총구 Transform 또는 월드 좌표를 전달하며, Launcher는 기존 소총 자식 Muzzle을 세 공격에 넘긴다. 씬의 Muzzle 로컬 위치 `(0, 0, 0.665)`·참조와 에셋 GUID를 재사용했다. Transform 요청은 선딜 동안 따라가고 실행 시 최신 위치를 읽는다. 캐릭터 중심축에서 실제 총구 높이로 벽을 검사한 뒤 원점·조준 방향을 보정하며 총구 삭제는 실패로 끝낸다. 근접 OriginOffset·발사각·입력·피해 배율·디버그 선을 유지했다. 열린 원본 에디터를 유지한 별도 복사본에서 Unity 6000.3.25f1로 AttackValidation 67개·PlayerAttackValidation 35개·PlayerAnimationValidation의 이동·점프·IK·17회 히트스캔·재장전·벽·정지 검사를 직접 실행해 모두 종료 코드 0을 확인했다. 비차단 발사의 총구~실제 원점 거리 최대 0m, 시각 총구·공격 방향 최대 오차 약 0.028도였고 런타임·에디터 C# 빌드는 경고 0·오류 0이었다. 로그는 `Logs/RuntimeAttackOrigin_20261016/`에 보관했다. 관련 소스·에셋·씬 13개의 SHA256 일치·삭제 필드 참조 제거·diff 검사를 확인하고 공격·플레이어 가이드를 갱신했다. Unity Search 내부 인덱스 예외는 배치 시작에 기록됐지만 기능 검증은 통과했다. 실제 수동 플레이·이미지 렌더링과 투사체·스태미나 독립 검증은 이번에는 재실행하지 않았다. 손 투척 지점·애니메이션, 탄약·실제 재장전·발사 간격 밸런스는 후속 작업이다.

- 2026-10-18 공격 바인딩 구성에서는 PlayerAttackBinding 직렬화 클래스와 AttackSlot을 추가하고 Launcher의 세 고정 공격·공유 총구 필드를 바인딩 배열로 교체했다. 프리팹별 슬롯·공통 Attack·Origin·홀딩 반복을 설정하고 AttackExecuted(slot)으로 성공을 알린다. 기존 Launcher 스크립트 이름·GUID와 SampleScene의 세 Attack·Muzzle 원점을 유지했으며 소총 시각 총구 참조는 Aim으로 옮겼다. Soldier 애니메이션은 PrimaryFire·SecondaryFire 성공을 구독한다. 현재 저장된 좌클릭 후딜 0.25초·탄퍼짐 3도와 사용자 수정 중인 우클릭 후딜 0.5초를 보존했다. 원본 프로젝트에서 Unity 6000.3.25f1로 PlayerAttackValidation 45개와 PlayerAnimationValidation의 이동·점프·양팔 IK·17회 발사·재장전·벽·정지 검증을 직접 실행해 모두 종료 코드 0을 확인했다. 별도 Soldier 컴포넌트 없는 임시 캐릭터의 PrimaryFire 근접·Skill2 히트스캔 홀딩·Ultimate 투사체와 개별 Origin, 재활성화 이벤트 중복 방지·대기 취소도 검사했다. 애니메이션 검증은 현재 저장된 탄퍼짐·발사 간격과 자세 정렬을 구분하기 위해 런타임 데이터 복사본에서만 탄퍼짐·공격 시간을 0으로 설정한다. 최초 검증용 표적 간섭 실패와 기존 무탄퍼짐 가정의 실패를 보존하고 검증 코드를 보완했다. 로그는 `Logs/AttackBindings_20261018/`다. 런타임·에디터 C# 직접 빌드는 경고 0·오류 0이었다. 손의 실제 투척 지점 배치·투척 애니메이션, SampleScene의 Skill2·Ultimate 효과, 탄약·실제 재장전은 후속 작업이다. 수동 플레이·이미지 렌더링과 공통 공격·투사체·스태미나 독립 검증은 이번에는 다시 실행하지 않았다.

- 2026-10-23 반복 발사 설정 통합에서는 RepeatWhileHeld 필드·읽기 전용 프로퍼티를 PlayerAttackBinding에서 공통 AttackData SO로 옮겼다. 바인딩에는 Slot·Attack·Origin만 남기고 Launcher는 Attack.Data.RepeatWhileHeld를 읽는다. 같은 SO를 사용하는 공격 연결은 반복 정책을 공유하며 새 데이터는 기본 false다. TempPrimaryFire·TempSecondaryFire·SoldierRifleAttack은 true, 두 수류탄·StrikerMeleeAttack 예제는 false로 저장했다. SampleScene의 옛 바인딩 필드를 제거하고 기존 공격·원점·GUID·피해·후딜·탄퍼짐 설정을 보존했다. Unity 6000.3.25f1 원본 프로젝트에서 PlayerAttackValidation 48개와 PlayerAnimationValidation의 이동·점프·양팔 IK·17회 발사·벽·재장전·정지 검증을 직접 실행해 모두 종료 코드 0을 확인했다. 같은 바인딩에서 런타임 데이터 복사본의 반복 정책만 변경한 뒤 Held 반복 중단·Pressed 단발 유지와 원본 SO 값 보존을 추가 검사했다. 런타임·에디터 C# 직접 빌드는 경고 0·오류 0이었다. 로그는 `Logs/AttackRepeatPolicy_20261023/`다. 두 상세 가이드를 동기화했으며 2026-10-18의 바인딩 반복 필드 설명은 당시 기록이다. 손 투척 지점·추가 애니메이션·스킬 효과·탄약·실제 재장전은 후속 작업이다. 수동 플레이·이미지 렌더링과 공통 Attack·투사체·스태미나 독립 검증은 이번에는 다시 수행하지 않았다.

- 2026-10-23 공격 컨트롤러 이름 정리에서는 PlayerProjectileLauncher 클래스·파일을 PlayerAttackController로 변경했다. meta 전체와 GUID `810bdef62c39a4b4c94901ea221cd967`를 보존하고 Aim·Animation·검증 코드의 타입 및 SampleScene의 클래스 식별자를 갱신했다. 입력·SO·Origin·이벤트·실행 순서·조준 동작은 유지했다. aimDistance는 카메라 중앙 조준점 탐색의 최대 거리이며 실제 히트스캔 사거리는 HitscanAttackData.MaxDistance임을 Tooltip·공격 가이드에 명시했다. 열린 원본 에디터는 유지하고 `Logs/AttackControllerRename_20261023/Project` 복사본에서 Unity 6000.3.25f1의 PlayerAttackValidation 48개와 PlayerAnimationValidation의 이동·점프·양팔 IK·17회 발사·재장전·벽·정지 검증을 직접 실행해 모두 종료 코드 0을 확인했다. 같은 복사본의 런타임·에디터 C# 직접 빌드는 경고 0·오류 0이었고, 관련 소스·씬·meta 8개의 SHA256 일치를 확인했다. 배치 시작의 Unity Search 내부 인덱스 예외는 최종 기능 검증 통과와 구분해 로그에 보존했다. 로그는 `Logs/AttackControllerRename_20261023/`다. 수동 플레이·이미지 렌더링과 공통 Attack·투사체·스태미나 독립 검증은 재실행하지 않았다. 손 투척 지점·추가 애니메이션·스킬·탄약·재장전의 남은 범위는 유지한다.

- 2026-10-24 Skill1 투척 애니메이션 연결에서는 제공된 X Bot@Toss Grenade.fbx를 기존 X Bot Avatar 공유 Humanoid·60 FPS·0~67프레임(약 1.117초)·반복/Mirror Off로 임포트했다. 기존 Upper Body 레이어·마스크에 Toss Grenade 상태와 Skill1 트리거를 추가하고 AttackExecuted(Skill1) 성공으로 재생한다. 상태·전환 중 상체 보정·양팔 IK만 풀고 끝나면 기존 조준으로 복귀한다. 실제 공격 입력·실행 코드, 선딜·후딜·쿨타임 0, Muzzle Origin·로컬 오프셋 `(0, 0, 0.665)`·발사각 12도, 피해·수명과 씬·프리팹은 보존했다. 발사를 애니메이션 프레임으로 옮기거나 손 투척 지점을 추가하지 않았다. 이번에 원본 Unity 6000.3.25f1에서 PlayerAttackValidation 48개와 PlayerAnimationValidation의 기존 이동·점프·양팔 IK·17회 사격·재장전·벽·정지 검사, 추가한 E 입력 프레임 발사·상체 투척·이동·홀딩 반복 방지·일시정지/재개·조준 복귀 검사를 직접 실행해 통과했다. 투척 Game·관찰 화면을 렌더링해 확인했으며 총구·공격 방향 최대 오차는 약 0.028도였다. 원본 FBX 일치와 기존 씬·공격 에셋·프리팹·임포트 설정 SHA256 보존 및 diff 검사를 확인했다. 로그·검증 조건 보완 전 실패 기록·보고서·이미지는 `Logs/GrenadeSkillLink_20261024/`에 있다. 실제 수동 플레이와 다른 독립 검증은 다시 실행하지 않았다. 남은 Soldier 애니메이션은 Dash·Skill2·Ultimate이며 손 투척 지점·발사 동기화는 별도 후속 작업이다. 두 상세 가이드를 동기화했다.

- 2026-10-26 대시 이동 작업에서는 PlayerMotor에 Inspector 필드 dashDistance(5m)·dashDuration(0.2초)·dashPostDelay(0.3초), IsDashing·IsDashPostDelay·CanPerformActions·CanDash·TryDash를 추가했다. 입력 방향 또는 무입력 전방으로 고정 방향 대시를 실행하고 CharacterController 충돌·중력을 유지한다. Controller는 상태 확인 후 기존 스태미나 25를 소모한다. 대시·후딜 중 점프·재대시·모든 바인딩 공격·재장전을 차단하고 대기 선딜·시각 재장전을 취소한다. 후딜 중 WASD 이동을 허용하며 홀딩 재대시·입력 예약·별도 쿨타임·무적·전용 대시 애니메이션은 추가하지 않았다. 새 PlayerDashValidation과 기존 스태미나 검증의 변위 기대값을 갱신했다. 열린 원본 에디터를 유지하고 Logs/Dash_20261026/Project 복사본에서 Unity 6000.3.25f1의 대시 38개·공격 48개·스태미나·기존 이동/점프/IK/17발 사격/재장전/벽/일시정지 자동 검증을 직접 실행해 모두 종료 코드 0을 확인했다. 최종 로그는 같은 상위 폴더의 PlayerDashValidation.Final.log, PlayerStaminaRegression.log, PlayerAttackRegression.log, PlayerAnimationRegression.log다. 직접 런타임·에디터 C# 빌드도 오류·경고 없이 통과했고, 검증 복사본과 변경 소스·씬·meta 8개의 SHA256 일치 및 git diff --check를 확인했다. 배치 시작의 Unity Search 내부 인덱스 예외는 최종 기능 검증 통과와 구분한다. 실제 키보드·마우스 수동 플레이·이미지 렌더링과 공통 Attack·투사체·Mortal 독립 검증은 이번에 다시 실행하지 않았다. 남은 애니메이션 4개, 무적 여부·최종 밸런스와 다른 미구현 전투 기능은 후속 작업이다.

- 2026-10-27 대시 시각 이벤트 분리에서는 PlayerMotor.TryDash가 상태 초기화 후 성공당 한 번 OnDashExecuted를 발생시키도록 추가했다. PlayerAnimationController는 활성화·비활성화에 맞춰 이벤트를 구독·해제하고 핸들러에서 기존 시각 재장전·사격 트리거 정리와 Rifle Aim 복귀를 처리한다. dashActionsBlocked와 Update의 대시 자세 초기화 블록은 제거했다. R의 CanPerformActions 조건과 실제 이동·스태미나·공격 차단은 유지하며 전용 대시 클립·실제 재장전은 추가하지 않았다. 기존 검증 복사본 Logs/Dash_20261026/Project를 최신 소스로 동기화해 Unity 6000.3.25f1 컴파일과 대시 52개·공격 48개·기존 애니메이션(이동·점프·IK·17발 사격·재장전·벽·일시정지) 검증을 직접 실행했고 모두 종료 코드 0을 확인했다. 성공당 이벤트 한 번·거절 시 미발생·상태 초기화 이후 알림·상체 복귀 후 재시작 방지·구독 해제/재활성 동기화·일시정지 해제 프레임의 대시를 추가 검사했다. 로그는 Logs/DashEvent_20261027/의 PlayerDashValidation.log, PlayerAnimationRegression.log, PlayerAttackRegression.log다. C# 컴파일 경고·오류 없음과 변경 C# 세 파일의 검증 복사본 SHA256 일치 및 git diff --check를 확인했다. 실제 키 입력 수동 플레이·이미지 렌더링·스태미나 및 공통 Attack·투사체·Mortal 독립 검증은 이번에 다시 실행하지 않았다. 남은 애니메이션 4개와 실제 탄약·재장전·스킬 효과 등 기존 미구현 범위는 유지한다.

- 2026-10-28 탄창·장전 작업에서는 PlayerAttackController에 기본 발사 전용 탄창, R 수동 장전·0발 지속 자동 재시도, 두 발사 차단, 대시 성공·스킬 수락 시 보충 없는 취소와 OnReloadStarted/OnReloadCompleted를 구현했다. PlayerStat에 reloadDuration·reloadSpeedMultiplier를 추가하고 dashDistance를 Motor에서 옮겼다. 씬의 30발·3.3초·1배·5m 설정과 기존 공격/원점 참조를 저장했다. Animation은 입력·별도 타이머 대신 시작/완료 이벤트와 실제 장전 상태를 사용하고 Reloading 상태만 배속하며, Aim은 장전 중 사격 홀딩으로 덮어쓰지 않는다. 스킬 사용 구간은 Attack 선딜·후딜과 수락/실행 프레임이며 시각 클립·쿨타임·투사체 수명은 제외한다. 이번에 원본 프로젝트에서 Unity 6000.3.25f1로 새 PlayerReloadValidation 39개, PlayerAttackValidation 50개, PlayerDashValidation 60개, PlayerStaminaValidation과 PlayerAnimationValidation(17발·이동·점프·IK·장전·정지)을 직접 실행해 모두 종료 코드 0을 확인했다. 로그는 Logs/AmmoReload_20261028/의 PlayerReloadValidation.Final.log, PlayerDashValidation.Final2.log와 나머지 검증별 log다. 최초 장전 검사에서 확인한 일시정지 전환 프레임의 타이머 차감은 timeScale 검사로 수정했다. 대시 추가 검사에서 연사 후 기존 공격 후딜을 기다리지 않은 검증 준비 오류는 검사에서 보완했으며 초기 실패 로그를 보존했다. 최종 .NET 런타임·에디터 빌드는 경고 0·오류 0(DotnetBuild.Final.log)이었고 git diff --check를 확인했다. 이전 날짜의 검증은 과거 기록이다. 실제 키 입력 수동 플레이·이미지 렌더링·공통 Attack/투사체/Mortal 독립 검증은 이번에 수행하지 않았다. 탄약 HUD·Skill2/궁극기 효과·전용 대시 클립·적 AI·사망 이동 중단·반복 전투 흐름은 남아 있다.

- 2026-10-29 공격 행동 제한에서는 Attack.BlocksActions와 Combatant.IsPerformingAttack을 연결해 같은 캐릭터의 공격 선딜·후딜 중 다른 공격·점프·대시·재장전을 거절한다. Motor.CanPerformActions는 공격 제한을 함께 확인하고 IsDashActionLocked는 대시 전용 취소·시각 복구에 사용한다. 대시·R은 이미 시작한 선딜을 취소하지 못하며 거절된 대시는 스태미나를 소모하지 않는다. 이동·시점·중력, 기존 배속·일시정지·피해·탄약·Muzzle 참조를 유지하고 쿨타임만 남으면 다른 행동을 허용한다. 실행 프레임도 예약해 시간 0 공격의 동시 입력과 실행 이벤트 재진입을 차단한다. 별도 복사본 `Logs/AttackActionLock_20261029/Project`에서 Unity 6000.3.25f1로 새 PlayerActionDelayValidation 99개, AttackValidation 68개, PlayerAttackValidation 50개, PlayerDashValidation 61개, PlayerReloadValidation 40개와 스태미나·애니메이션(17발·이동·점프·IK·장전·투척·정지) 검증을 이번에 직접 실행해 통과했다. 원본 Assets 189개와 복사본의 SHA256 일치, 최종 C# 런타임·에디터 빌드 경고 0·오류 0과 git diff --check를 확인했다. 실제 저장값인 수류탄 후딜 1초·쿨타임 6초와 소총 탄퍼짐 3도를 0으로 가정하던 기존 검증, 공격 겹침·선딜 취소를 전제로 하던 검사 준비를 보완했다. 대시 시각 중단 검사는 런타임 복사본에서만 수류탄 시간을 0으로 설정한다. 중간 실패·최종 통과 로그는 `Logs/AttackActionLock_20261029/`에 있다. Unity Search의 기존 시작 시 인덱스 예외는 기능 검증과 구분한다. 실제 키 입력 수동 플레이·이미지 렌더링·투사체/Mortal 독립 검증은 이번에 수행하지 않았다. 미구현 스킬 효과·추가 애니메이션·적 AI·사망 행동·반복 전투 흐름은 남아 있다.

- 2026-10-29 후속 점프 예외에서는 Motor.CanJump를 추가해 공격 Windup·Recovery와 dashPostDelay 중 지상 점프를 허용했다. 실제 IsDashing, 시간 정지, 비활성 Motor·CharacterController에서는 요청을 거절하며 접지 여부는 Motor.Update에서 검사한다. Controller 입력과 Motor.RequestJump·실제 실행이 같은 조건을 확인하고 공중 입력은 한 업데이트에 소비해 재점프·착지 후 예약 점프를 만들지 않는다. CanPerformActions·IsDashActionLocked는 공격·장전·추가 대시 제한으로 유지하며 점프가 공격·대시 타이머·스태미나를 바꾸지 않는다. 점프 로그도 CanJump 조건으로 분리했다. 검증 복사본 `Logs/AttackActionLock_20261029/Project`에서 Unity 6000.3.25f1로 PlayerActionDelayValidation 110개, PlayerDashValidation 63개, PlayerAnimationValidation(17발·이동·점프·IK·장전·투척·정지)을 이번에 직접 실행해 모두 종료 코드 0을 확인했다. 다섯 슬롯의 선딜·후딜 각각 실제 점프, 공중 재점프 방지, 대시 중 점프 거절·후딜 중 점프와 다른 행동 차단 유지를 검사했다. 로그·종료 코드는 `Logs/JumpDuringDelay_20261029/`의 각 Initial.log·Results.Initial.json이며 DotnetBuild.log는 경고 0·오류 0이다. 원본 Assets 189개와 복사본 SHA256 일치, 씬·공격 데이터·프리팹 변경 없음과 git diff --check를 확인했다. 이전 99개 행동 제한 검증의 점프 차단은 후속 요청 전 기록이다. 이번에는 수동 키 입력·이미지 렌더링·공통 Attack/플레이어 공격/장전/스태미나/투사체/Mortal 독립 검증을 재실행하지 않았다. 남은 스킬 효과·추가 애니메이션·적 AI·사망 행동·반복 전투 흐름은 유지한다.

후속 작업에서는 기준 날짜와 함께 구현한 기능, 남은 제약, 실제 수행한 검증을 갱신해 이 문서의 진행 상태를 최신으로 유지한다.

### 2026-10-30 한 개 선입력·대시 우선순위

PlayerController에 입력 선택을 모으고 PlayerAttackController는 RequestAttack(slot)·RequestReload의 한 프레임 명령을 기존 LateUpdate 100에서 소비하도록 변경했다. 공격·대시 잠금 중 다음 행동 한 개를 최대 maxInputBufferTime(코드·SampleScene 1초)까지 유지한다. 일반 예약은 최신 누름으로 교체하고 대시는 다른 입력으로 교체·유효 시간 연장되지 않는다. 첫 행동 기회에 실행 조건을 재검사하고 실패하면 버린다. 이동·점프는 비예약이며 홀딩 반복은 CanAttack(slot)으로 실행 가능한 후보를 선택하고, 0발 자동 장전은 가능한 상태가 될 때까지 다시 요청한다. InputReader의 IsGameplayInputEnabled 스냅샷과 포커스·비활성 처리로 조작 중단 시 예약·명령을 지운다. 클래스명·기존 공격/대시 시간·자원·피해·바인딩·총구·프리팹·GUID는 유지했고 씬에는 maxInputBufferTime: 1만 추가했다.

이번에 Unity 6000.3.25f1 복사본에서 EnhancedInputValidation 51개·PlayerActionDelayValidation 110개·PlayerDashValidation 63개·PlayerReloadValidation 40개·PlayerAttackValidation 50개와 애니메이션(17발·이동·점프·IK·장전·투척·정지)·스태미나 검증을 직접 실행해 모두 종료 코드 0을 확인했다. 최종 런타임·에디터 C# 빌드 경고 0·오류 0, 원본/검증 복사본 Assets 191개 SHA256 일치와 git diff --check를 확인했다. 로그·종료 코드·해시는 `Logs/EnhancedInput_20261030/`에 있다. 기존 검증은 선입력 시간을 0으로 두고 즉시 차단·실행 기능을 분리 검사하며 새 51개가 예약·만료·중단 폐기를 검증한다. 실제 키·마우스 수동 플레이·이미지 렌더링·공통 Attack/투사체/Mortal 독립 검증은 이번에 실행하지 않았다. 배치 입력 활성 스냅샷·중복 OnDisable·홀딩 쿨타임 후보 선택의 중간 실패와 Unity Search 시작 예외·라이선스 메시지는 최종 검증과 구분한다. 상세 클래스 책임·설정·남은 작업은 [EnhancedInput.md](EnhancedInput.md)를 참고한다.

### 2026-10-30 기능 컴포넌트 이름 변경

PlayerController만 입력·행동 선택 주체의 Controller 이름을 유지하고 PlayerAimController → PlayerAim, PlayerAnimationController → PlayerAnimation, PlayerAttackController → PlayerCombat, PlayerCameraController → PlayerCamera로 클래스·파일·참조를 변경했다. 네 스크립트의 meta 전체와 GUID를 보존하고 SampleScene의 네 m_EditorClassIdentifier만 새 타입명으로 갱신했다. 직렬화 필드명·설정·컴포넌트 ID·바인딩·Muzzle·실행 순서·선입력 규칙·게임플레이는 유지한다. 현재 문서와 검증 도구는 새 이름을 사용하며 날짜별 과거 기록의 이전 이름은 당시 이름이다.

이번에 Logs/PlayerComponentRename_20261030/Project 복사본에서 Unity 6000.3.25f1로 EnhancedInputValidation 51개·PlayerAttackValidation 50개·PlayerAnimationValidation(17발·이동·점프·조준·총구·양팔 IK·장전·투척·정지)을 직접 실행해 모두 종료 코드 0을 확인했다. 런타임·에디터 C# 빌드 경고 0·오류 0, Assets 191개가 네 타입명·파일명 치환만 한 예상 결과 및 검증 복사본과 SHA256 일치, 런타임 Controller 클래스는 PlayerController 하나, 소스·씬의 이전 타입명 참조 없음과 git diff --check를 확인했다. 로그·무결성·종료 코드는 Logs/PlayerComponentRename_20261030/에 있다. Unity Search의 기존 시작 인덱스 예외는 기능 검증 결과와 구분한다. 이번에는 수동 키·마우스 플레이·이미지 렌더링과 행동 제한·대시·장전·스태미나·공통 Attack/투사체/Mortal 독립 검증을 다시 실행하지 않았다. 앞선 선입력 작업의 7종 검증과 구분하며 상세 책임·규칙·남은 작업은 EnhancedInput.md 7절을 참고한다.

### 2026-10-30 접지 중에만 대시 시작

PlayerMotor.CanDash에 IsGrounded를 추가해 접지 중에만 대시를 시작한다. Controller는 스태미나 소모 전에 검사하고 직접 TryDash도 같은 조건으로 거절한다. 예약 대시는 첫 실행 기회에 공중이면 스태미나 소모 없이 폐기하며 착지를 기다리지 않는다. 이번에 원본 프로젝트의 Unity 6000.3.25f1로 대시 70개·선입력 53개·행동 제한 110개를 직접 실행해 모두 통과했다. 상승·낙하 중 입력/직접 호출 거절, 자원 미소모, 착지 시 홀딩 재실행 방지·새 누름 성공, 공중 예약 폐기와 기존 대시·이동·점프·취소·정지를 확인했다. Unity 컴파일 오류·경고와 git diff --check 오류는 없으며 씬·프리팹·공격 데이터·meta는 변경하지 않았다. 로그는 Logs/GroundedDash_20261030/에 있으며 샌드박스 라이선스 초기화 대기로 중단한 Initial.log는 최종 통과 결과와 구분한다. 이전 날짜 검증을 이번 결과로 사용하지 않는다. 수동 키·마우스 플레이·이미지 렌더링과 다른 독립 검증은 다시 실행하지 않았으며 대시 전용 애니메이션·스킬 효과·적 AI·사망 행동·반복 전투 흐름은 후속 작업이다. 상세 규칙은 EnhancedInput.md 8절과 플레이어 조작 가이드 33절을 참고한다.

### 2026-10-30 선입력 수신 시 실행 자격 검사

공중에서 공격 딜레이 중 대시를 누른 뒤 딜레이 종료 전에 착지하면 실행되던 문제를 수정했다. Controller가 수신 시 공격·대시 딜레이 외 접지·스태미나·쿨타임·탄약·장전·활성·구성 조건을 검사한 입력만 예약한다. 실패 입력은 기존 예약을 교체·연장하지 않으며 실행 직전 재검사도 유지한다. Motor·PlayerCombat·Attack의 내부 자격 조회를 공유하며 별도 Controller·예약 저장소는 추가하지 않았다. 이번에 원본 Unity 6000.3.25f1에서 선입력 85개·공통 공격 68개·플레이어 공격 50개·대시 70개·장전 40개·행동 제한 110개, 총 423개와 종료 코드 0을 직접 확인했다. 수정 전 재현 실패·최종 통과 로그는 Logs/InputEligibility_20261030/에 있으며 C# 컴파일 오류·경고는 없다. 씬·프리팹·공격 에셋·meta·실행 순서는 유지했다. 이전 검증은 이번 결과와 구분한다. 수동 키·마우스 플레이·이미지 렌더링·애니메이션/스태미나/투사체/Mortal 독립 검증은 다시 수행하지 않았다. 남은 조작감 조정·애니메이션·스킬 효과·적 AI·사망 행동·반복 전투 흐름은 유지하며 상세 규칙·결과는 EnhancedInput.md 9절과 플레이어 조작 가이드 34절을 참고한다.

### 2026-10-30 PlayerAction과 Soilder 전투 액션 리팩토링·검증

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
