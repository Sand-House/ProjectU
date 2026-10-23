# ProjectU 프로젝트 지침

진행 현황 기준: **2026-10-23**. Soldier 기본 애니메이션 10개와 이동 문제 해결은 완료했고 남은 애니메이션은 **Dash·Skill1·Skill2·Ultimate 4개**다. 이동 임포트·플레이어 forward 기준 상체 보정·양팔 IK를 유지한다. SampleScene의 Player에 Mortal·Combatant(Player)와 3개 Attack을 연결했다. 좌클릭은 TempPrimaryFire 히트스캔 피해 30, 우클릭은 TempSecondaryFire의 기존 Bullet 피해 50, E는 TempGrenade의 기존 Grenade 폭발 피해 100이다. 공격 데이터의 위치 속성을 제거했고, Launcher가 바인딩별 Origin Transform을 전달하면 실행 시 최신 위치를 읽고 캐릭터~총구의 벽을 검사·보정한다. 좌·우클릭은 짧은 누름과 홀딩 반복 발사를 지원하고 E는 누름당 한 번 실행한다. PlayerStat 공격력·공격속도 배율을 연결했으며 스태미나·기본 이동·점프는 유지했다. 실제 대시·Skill2·궁극기, 탄약·재장전·적 AI·사망 행동과 반복 전투 흐름은 남아 있다. 공격·투사체 설명은 공격 구현 가이드로 통합했다. PlayerAttackBinding 직렬화 클래스로 슬롯·공통 Attack·Origin을 프리팹별 설정하고 홀딩 반복은 공유 AttackData.RepeatWhileHeld에서 설정하며, 공통 Launcher의 배열 순회와 AttackExecuted(slot) 이벤트를 사용한다. SampleScene의 기존 세 공격·Muzzle 원점은 보존했고 손 투척 지점은 후속 작업이다.

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
- 전술 대시는 Shift로 짧은 거리를 빠르게 이동한다. 역동적이고 캐주얼한 플레이를 위해 이동기는 별도 자원인 **스태미나(Stamina)**를 소모하며 **쿨타임 없이** 사용할 수 있게 한다. 스태미나가 충분해야 사용할 수 있으며 최대량·소모량·초당 회복량·회복 지연 시간, 대시 무적 여부와 행동 제한은 아직 미정이다.
- 전술 수류탄은 물리 투사체로 날아가 일정 시간 후 폭발해 범위 피해를 준다. 기본 사격과 별도인 **Skill1(E)**로 제공한다.
- **Skill2(F)**는 두 번째 스킬이며 효과는 아직 정하지 않았다. 대시 입력과 스태미나 자원은 Skill1·Skill2와 구분한다.
- 궁극기 입력은 Q로 예정되어 있으나 효과와 발동 조건은 아직 정하지 않았다.

| 입력 | 최종 목표 | 현재 프로토타입 |
|---|---|---|
| WASD | 카메라 기준 스트레이프 이동 | 구현 |
| 마우스 이동 | 카메라 회전·조준, 캐릭터 수평 회전 | 구현 |
| 좌클릭 | 기본 사격 | TempPrimaryFire 히트스캔, 기본 피해 30, 짧은 누름·홀딩 연사·상체 사격; 탄약 없음 |
| 우클릭 | 정밀 조준 | TempSecondaryFire의 기존 Bullet, 기본 피해 50, 짧은 누름·홀딩 연사에 임시 연결 |
| R | 재장전 | 입력·로그와 상체 재장전 동작; 탄약 처리 없음 |
| Space | 점프 | 지상 점프와 Jump Up/Loop/Down 애니메이션 구현 |
| 왼쪽·오른쪽 Shift | 스태미나를 소모하는 대시, 쿨타임 없음 | 누름당 스태미나 25 소모, 부족 시 소모 거절, 지연 자동 회복; 실제 대시 이동 없음 |
| E | Skill1: 수류탄 투척 | TempGrenade의 기존 Grenade, 2.5초 후 기본 폭발 피해 100, 누름당 한 번 |
| F | Skill2: 효과 미정 | `Skill2` 입력 상태와 로그만 구현 |
| Q | 궁극기 | `Ultimate` 입력 상태와 로그만 구현 |
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

**현재는 플레이어 조작 및 물리 투사체 프로토타입 단계다. 조작 기반은 갖춰졌으며, 최소 전투와 한 판의 반복 흐름은 아직 구현하지 않았다.**

| 시스템 | 상태 | 실제 구현과 남은 범위 |
|---|---|---|
| 테스트 아레나 | 프로토타입 구현 | `SampleScene`에 40×40m 바닥, 경계 벽, X Bot 플레이어와 임시 소총; 기존 캡슐 렌더러·전방 표시는 숨기고 CharacterController는 유지 |
| Soldier 메시·애니메이션 | 기본 애니메이션 적용 완료 | Humanoid 클립 10개를 2D 이동 Blend Tree·점프 3상태·상체 소총 레이어에 연결. 대기·전후좌우 이동·점프·사격·재장전과 이동 문제 해결을 2026-10-15 사용자 확인으로 완료 처리. 루트·반복 보정과 Rotation Offset 0, 플레이어 forward 기준 상체 보정·총구 조준·양팔 IK 사용. 남은 애니메이션은 Dash·Skill1·Skill2·Ultimate; 최종 무기 모델·손가락 정렬은 별도 시각 완성도 작업 |
| 입력 | 구현 | WASD·마우스, Dash를 포함한 8개 게임플레이 버튼의 `Pressed/Held/Released`, 커서·포커스·일시정지 처리 |
| 이동·점프 | 구현 | `CharacterController` 이동, 스트레이프, 카메라 Yaw 방향 회전, 중력·지상 점프·충돌; 실제 수평 속도와 접지·수직 속도로 애니메이션 구동 |
| TPS 카메라 | 구현 | 어깨 뒤 추적, Yaw·pitch, 수직 각도 제한, SphereCast 장애물 회피 |
| 발사·조준 | 프로토타입 구현 | 화면 중앙 조준점과 소총 자식 Muzzle을 상체·총구·실제 공격이 공유. 카메라 LateUpdate 이후 상체·총구 정렬 후 발사. 선딜이 끝나는 프레임의 총구 위치를 읽고 캐릭터~총구의 벽 검사·원점 보정·방향 재계산을 실행. 가까운 조준점의 총 후퇴와 수류탄 발사각 보정 유지 |
| 투사체·풀 | 구현 | 공통 생명주기, Bullet, Grenade, 프리팹별 사전 생성·확장·대여·반환·용량 제한 |
| 공통 공격 | 구현·플레이어 입력 연결 | 공통 진영·부위·타이밍·피해와 3종 실행을 구현. Player에 히트스캔 1개·투사체 공격 2개와 Temp 에셋을 연결했다. 적 AI·공격 간 행동 제한은 후속 작업 |
| 수류탄 스킬 | 임시 기능 연결 | E에서 TempGrenade 투척, 기존 물리 튕김·2.5초 지연 폭발·기본 범위 피해 100·연출·풀 반환. 예제 쿨타임 0이며 최종 밸런스·엄폐물·넉백·투척 애니메이션은 후속 작업 |
| 소총·탄약·재장전 | 홀딩 연사·시각 재장전 | 좌클릭 히트스캔 30·우클릭 시험 Bullet 50, 공격 시간에 따른 홀딩 연사와 실제 실행 성공으로 상체 사격·재장전 중단. 탄창·실제 재장전은 미구현 |
| 플레이어 능력치 | 구현·일부 연결 | 이동속도→Motor, 최대 스태미나→PlayerStamina, 공격력·공격속도 배율→세 Attack을 연결했다. 최대 체력의 Mortal 동기화·최대 탄창의 탄약 적용은 후속 작업 |
| 대시·스태미나 | 자원 구현·씬 연결 | PlayerStamina의 잔량 검증·소모·회복·이벤트와 게임 시간 기준 지연 자동 회복 구현. Shift.Pressed에서 소모하며 홀딩 반복·사용 쿨타임 없음. 실제 대시 이동·무적·행동 제한·애니메이션은 미구현 |
| 정밀 조준·Skill2·궁극기 | 미구현 | F의 Skill2와 Q의 Ultimate는 입력·로그만 구현; 우클릭 정밀 조준 연결과 스킬 효과 구현 필요 |
| 체력·피해·사망 | 플레이어·공격 연결 | Player의 Mortal HP 100·Combatant와 실제 공격 피해 전달을 연결했다. 사망 시 Attack 요청·대기 실행 중단. 사망 시 이동 중단·연출·부활과 적 AI·씬 연결은 미구현 |
| 적 AI·적 공격 | 미구현 | Striker·Gunner의 추적, 거리 유지, 공격 예고, 공격·사망 처리 필요 |
| 웨이브·강화 선택 | 미구현 | 적 생성, 클리어 판정, 전투 정지, 강화 제시·적용 필요 |
| 보스·결과·재시작 | 미구현 | 10웨이브 보스, 성공·실패, 한 판 상태 초기화 필요 |
| 게임 UI·사운드·완성도 | 시각 프로토타입, 나머지 미구현 | X Bot 이동·점프·상체 동작, 임시 소총과 폭발 구체 제공; HUD·보상·결과 UI, 사운드·파티클·추가 애니메이션·밸런스·성능 개선 필요 |

### Soldier 애니메이션 후속 작업

기본 애니메이션과 이동 관련 문제 해결은 완료했다. 남은 Soldier 애니메이션 작업은 다음 4개다.
애니메이션 작업 현황과 실제 행동·자원·피해 기능의 구현 현황을 구분한다.

| 애니메이션 | 입력 | 현재 기능과 남은 범위 |
|---|---|---|
| Dash | 좌·우 Shift | 스태미나 소모·자동 회복 구현; 애니메이션·실제 대시 이동·무적·행동 제한은 후속 작업 |
| Skill1 | E | TempGrenade 투척·지연 폭발 피해 연결; 투척 애니메이션 추가 예정 |
| Skill2 | F | 애니메이션 추가 예정; 입력·로그만 구현, 효과 설계·실행 기능 필요 |
| Ultimate | Q | 애니메이션 추가 예정; 입력·로그만 구현, 효과·발동 조건 설계·실행 기능 필요 |

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
| `Assets/Scripts/Player/PlayerStat.cs` | 플레이어 6개 능력치의 Inspector 설정과 읽기·쓰기 프로퍼티; 이동속도·최대 스태미나의 원본 |
| `Assets/Scripts/Player/PlayerStamina.cs` | 현재 스태미나, 소모 검증·차감·회복, 지연 자동 회복과 값 변경 이벤트 |
| `Assets/Scripts/Player/PlayerController.cs` | 카메라 Yaw로 입력을 월드 이동 명령으로 변환, 점프 요청, Shift 스태미나 소모, 버튼 로그 |
| `Assets/Scripts/Player/PlayerMotor.cs` | 한 프레임 명령을 소비해 이동·회전·중력·점프·충돌 처리 |
| `Assets/Scripts/Player/PlayerAnimationController.cs` | Motor 상태로 이동·점프 파라미터 갱신, 발사 성공 이벤트·R로 상체 동작 실행, 일시정지 시 Animator 평가 정지 |
| `Assets/Scripts/Player/PlayerAimController.cs` | 카메라 갱신 후 척추·가슴 회전, 소총·총구 조준, 양팔 IK·가까운 조준점에서 소총 후퇴 |
| `Assets/Scripts/Player/PlayerCameraController.cs` | 시점 회전, 플레이어 추적, 카메라 장애물 검사 |
| `Assets/Scripts/Player/PlayerAttackBinding.cs` | AttackSlot·공통 Attack·Origin Transform의 직렬화 클래스; 반복 여부는 연결된 Attack.Data에서 조회 |
| `Assets/Scripts/Player/PlayerProjectileLauncher.cs` | 바인딩 배열에 따른 중앙 조준·PlayerStat 배율·입력 요청과 AttackExecuted(slot) 성공 이벤트 |
| `Assets/Scripts/Projectiles/Projectile.cs` | 추상 기반 클래스; Rigidbody 초기화, 수명, 충돌 전달, 발사자 충돌 제외·복구, 반환 |
| `Assets/Scripts/Projectiles/ProjectileBullet.cs` | 중력 없는 직선 물리 총알; 충돌 시 반환 |
| `Assets/Scripts/Projectiles/ProjectileGrenade.cs` | 중력·튕김, 수명 만료 시 물리 정지·폭발 연출·반환 |
| `Assets/Scripts/Projectiles/ProjectilePool.cs` | 프리팹별 Queue·Dictionary·HashSet으로 생성·대여·반환 관리 |
| `Assets/Scenes/SampleScene.unity` | 현재 테스트 씬과 직렬화된 컴포넌트 참조 |
| `Assets/Prefabs/Projectiles/` | `Bullet.prefab`, `Grenade.prefab`, 물리 머티리얼과 시각 머티리얼 |
| `Assets/Characters/Soldier/Models/` | 임시 Soldier의 `X Bot.fbx`; Humanoid Avatar와 전신 스킨드 메시 |
| `Assets/Characters/Soldier/Animations/` | Mixamo의 사격·재장전·소총 대기·전후좌우 달리기·Jump Up/Loop/Down 클립 10개; Soldier Animator에서 사용 |
| `Assets/Characters/Soldier/Animator/` | `Soldier.controller`와 `SoldierUpperBody.mask` |
| `Assets/Characters/Soldier/Materials/` | 임시 소총의 URP/Lit 머티리얼 2개 |
| `Assets/Editor/ProjectileValidation.cs` | 기존 씬·프리팹을 사용하는 투사체 배치 검증 |
| `Assets/Editor/MortalValidation.cs` | 씬을 저장하지 않는 공통 체력·피해·사망 검증; 에디터 메뉴 또는 배치 실행 |
| `Assets/Editor/PlayerAnimationValidation.cs` | 씬을 저장하지 않는 이동·점프·상체 동작·조준·총구 발사·일시정지 검증과 선택적 렌더링 |
| `Assets/Editor/PlayerStaminaValidation.cs` | 씬을 저장하지 않는 능력치·자원 API·Shift 소모·자동 회복·일시정지 검증 |
| `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` | 패키지·Unity 버전의 기준 |

### 유지해야 할 현재 구조

- `Update` 순서는 Reader **-200** → Camera **-100** → Stamina **-75** → Controller **-50** → Motor **0** → Animation **50**이다. Animator 평가 후 `LateUpdate`는 Camera **-100** → Aim **75** → Launcher **100** → Attack **110** 순서다. Controller는 같은 프레임의 Yaw를 사용하고 Launcher는 조준·배율 갱신과 요청, Attack은 실제 실행을 담당한다.
- 이동은 Motor, 애니메이션은 실제 Motor 상태, Aim은 시각 자세를 담당한다. GetAimPoint를 시각 조준과 공격이 공유한다. AttackExecuted(AttackSlot)은 연결된 Attack의 실제 실행 성공 후 발생한다. Soldier Animation은 PrimaryFire·SecondaryFire 슬롯 성공을 구독하며, 소총 시각 Muzzle은 Aim에서 관리하고 공격별 Origin은 바인딩에서 관리한다.
- Reader는 입력 수집, Controller는 행동 명령, Motor는 실제 이동을 담당한다. Motor는 입력과 카메라를 참조하지 않으며 이동·점프 명령은 한 업데이트에 소비한다.
- 입력은 코드에서 만든 액션 맵을 사용한다. 템플릿 `Assets/InputSystem_Actions.inputactions`는 현재 플레이어 조작에 사용하지 않는다.
- 입력 매핑은 Dash=좌·우 Shift, Skill1=E, Skill2=F다. Dash.Pressed는 PlayerStamina.TryConsume, Skill1.Pressed는 TempGrenade 요청을 실행한다. Skill2는 로그만 출력하며 실제 대시 이동은 없다.
- 액션·공개 멤버·로그 상태명은 PascalCase, 비공개 필드·지역 변수는 camelCase를 따른다. Inspector 설정은 `[SerializeField]` 필드로 관리한다.
- `Mortal.isDead`는 사용자가 요청한 공개 이름을 그대로 유지하는 예외다. `HP`와 `isDead`는 읽기 전용, `IsInvincible`은 읽기·쓰기가 가능하다. `TakeDamage(int damage, GameObject attacker)`는 실제 HP 감소량과 공격자를 `OnDamaged`에 전달하고, 치명 피해에서는 이후 `Dead(attacker)`로 `OnDead`를 한 번 발생시킨다. 파생 클래스는 재정의 시 기본 구현을 호출한다.
- 투사체 이동은 Rigidbody가 담당한다. `ProjectilePool.Spawn()`이 `Launch()`까지 호출하며 반환은 `Despawn()`을 사용한다. 외부의 직접 비활성화·삭제는 풀의 대여 상태를 어긋나게 할 수 있다.
- 재사용 시 발사자, 수명, 물리 상태, 충돌 제외, 추가 연출 상태를 초기화한다. 수명과 폭발 연출은 게임 시간에 따라 진행한다.
- 새 Attack은 LateUpdate 110에서 실행한다. 설정은 공유 ScriptableObject, 타이밍·연속 발사 상태는 컴포넌트별로 관리한다. 쿨타임·후딜은 실행 성공부터 동시에 시작하며 Time.timeScale 0에서 멈춘다. 히트스캔 탄퍼짐은 SpreadAngle이 양수이고 SpreadStartShot에 도달했을 때 적용하며 0이면 비활성화한다. 별도 탄퍼짐 활성화 필드는 없다. 현재 TempPrimaryFire와 SoldierRifleAttack 예제는 3도다. TempPrimaryFire의 후딜은 0.25초, TempSecondaryFire의 후딜은 0.5초이며 기존 설정을 보존한다. HitscanAttack은 기본 노란색·1초의 Debug.DrawLine을 실제 시작점에서 벽·관통 한도 대상의 표면 또는 최대 사거리까지 표시하며, 컴포넌트의 Debug 설정과 각 뷰의 Gizmos로 확인한다. LastShotEndPoint는 실제 궤적 끝점이다. 진영은 Combatant, 치명타 부위는 Hitbox로 판별한다. 투사체에 발사 시점 피해·진영을 값으로 복사하며 풀 재사용 시 배율·피격 목록·충돌 제외를 복구한다. 기존 Launcher와 새 Attack이 같은 입력을 중복 실행하지 않게 한다.
- AttackData에는 총구·공통 공격 위치를 저장하지 않는다. 호출자가 TryAttack(Transform origin, Vector3 worldAimPoint)으로 Muzzle을 넘기면 선딜 완료 시 최신 위치를 읽는다. TryAttack(Vector3 worldAttackPosition, Vector3 worldAimPoint)은 고정 월드 좌표를 받고, 기존 편의 호출은 캐릭터 Transform을 사용한다. WorldAttackPosition은 요청한 현재 위치, LastAttackPosition은 벽을 보정한 실제 위치다. 캐릭터 중심축의 총구 높이에서 실제 총구까지 검사하고 가장 가까운 고체 앞에서 최대 0.2m 캐릭터 쪽으로 원점을 당긴 뒤 조준 방향을 다시 계산한다. 선딜 중 총구가 삭제되면 발사 실패로 처리한다. Melee.OriginOffset은 별도 범위 중심 보정이다. Projectile.LaunchAngle은 조준 기준 0~45도이며 TempGrenade는 12도다.
- 카메라·총구 장애물 검사의 기본 대상은 Default 레이어이며 플레이어는 Ignore Raycast 레이어를 사용한다. 새 장애물과 마스크 설정을 함께 확인한다.
- 코드 초기값과 씬·프리팹의 저장값은 별개다. 지속할 설정은 Edit 모드에서 저장하고 `.meta` GUID와 직렬화된 참조를 보존한다.

### 스태미나·대시 설계 결정

2026-10-09 자원 분리 설계를 바탕으로 2026-10-15 PlayerStat·PlayerStamina와 Shift 소모 시험 연결을 구현했다. 실제 대시 이동은 사용자 요청에 따라 구현하지 않았다.
스태미나는 별도 `PlayerStamina` 컴포넌트로 관리한다. 체력과 스태미나의 소모·회복 규칙을 각각 관리해 강화와 밸런스 조정을 쉽게 한다.

| 구성 | 책임 |
|---|---|
| `Mortal` | 공통 HP, 무적, 피해·사망 처리 |
| `PlayerStat` | 6개 능력치 보관·변경; 최대 스태미나와 이동속도의 원본 |
| `PlayerStamina` | 현재 스태미나와 PlayerStat을 참조하는 최대량, 소모 검증·소모·회복과 값 변경 알림 |
| `PlayerController` | Dash.Pressed에서 스태미나 소모 시도; 대시 상태 확인·이동 요청은 후속 연결 |
| `PlayerMotor` | 실제 대시 이동과 충돌 처리; 대시 기능 추가 예정 |

- `PlayerStamina`는 읽기 전용 `Current`, `Max`, `RecoveryDelayRemaining`, 읽기·쓰기 `RecoveryDelay`, `RecoveryRate`와 `CanConsume(float amount)`, `TryConsume(float amount)`, `Recover(float amount)`를 제공한다. `Current`는 Inspector의 current 필드로 편집한다. 검증 함수는 값을 바꾸지 않으며, 소모는 양수·유한한 양과 충분한 잔량·활성 컴포넌트일 때만 성공한다.
- 최대 스태미나는 PlayerStat, 소모량은 Controller, 현재량·회복 대기·초당 회복량은 PlayerStamina의 Inspector에서 조절한다. 시험 기본값은 최대·시작 100, 1회 소모 25, 회복 대기 1초, 초당 20이다. 최종 밸런스는 미정이다.
- 성공한 소모마다 회복 대기를 다시 시작하고, 부족하거나 유효하지 않은 요청은 잔량·대기를 바꾸지 않는다. `Time.deltaTime`으로 대기를 줄이고 대기를 초과한 프레임 시간에만 자동 회복한다. 일시정지 중 대기·자동 회복은 함께 멈춘다. 비활성화·재활성화는 잔량·대기를 초기화하지 않으며 비활성 중 자동 회복하지 않는다. 회복 지연 시간은 사용 쿨타임이 아니다.
- `Recover`는 실제 회복량을 반환하고 최대량을 넘지 않는다. 명시적 회복은 대기를 무시하되 타이머를 바꾸지 않는다. 최대량 증가 시 잔량을 유지하고 감소 시 잔량을 제한한다. 런타임 최대량·Inspector 현재량 변경은 다음 Stamina.Update에서 동기화하고 `OnChanged(current, max)`를 알린다. HUD는 이벤트를 구독하고 초기값은 직접 읽으며 자원을 직접 변경하지 않는다.
- 대시 실행 흐름은 **입력 → 대시 가능한 상태 확인 → 스태미나 소모 성공 → Motor에 이동 요청**이다. 행동 제한으로 실행할 수 없는 대시에 자원만 소모하지 않도록 상태를 먼저 확인한다. 대시를 마친 뒤에는 자원이 충분하고 행동 가능한 상태면 쿨타임 없이 다시 사용할 수 있게 한다.
- `PlayerMortal`은 플레이어 고유의 피해 판정이나 사망 처리가 필요할 때 도입한다. 스태미나 추가를 위해 만들지는 않는다. 사망 시 입력 중단 같은 반응은 우선 `Mortal.OnDead`를 구독하는 컴포넌트로 연결할 수 있다.

### 현재 설정과 제약

- 이동 속도 6m/s, 점프 높이 1.5m, Motor 중력 25m/s². 수류탄은 별도로 프로젝트 Physics 중력을 사용한다.
- 스태미나 소모·부족 시 거절·지연 자동 회복은 구현했다. Shift 입력은 자원 소모 시험용이며 실제 대시 이동·무적·행동 제한과 대시 애니메이션은 미구현이다. PlayerStat.AttackMultiplier는 기본 공격력에 곱할 float 배율(기본 1, 최소 0)이고 방어력은 없다. 공격속도는 기본 발사속도의 배율(기본 1)이다. 공격력·공격속도 배율은 현재 세 공격에 전달하며 최대 체력의 Mortal 동기화·최대 탄창의 탄약 적용은 미구현이다.
- Bullet: 현재 저장된 프리팹은 속력 10m/s, 수명 5초, 중력 없음, 사전 생성 24개, 최대 생성 128개.
- Grenade: 속력 14m/s, 수명 2.5초, 중력 있음, 폭발 구체 연출 약 0.2초, 연출 반경 설정 4m, 사전 생성 8개, 최대 생성 32개.
- Launcher는 조준 거리 100m와 PlayerAttackBinding 배열을 사용하고 슬롯별 Attack에 Origin Transform·조준점·입력을 전달한다. 슬롯·공격 컴포넌트·Origin은 프리팹에서 연결하고 홀딩 반복 여부는 해당 AttackData SO에서 설정하며 같은 슬롯·같은 Attack 중복과 누락 참조는 활성화 시 거절한다. 실제 원점은 실행 시 현재 Muzzle.position과 장애물 보정이다. 기존 Prototype Rifle/Muzzle의 로컬 위치 `(0, 0, 0.665)`와 씬 참조를 재사용한다. 현재 임시 수류탄도 같은 총구를 쓰며 손의 별도 투척 지점·애니메이션은 후속 작업이다. 좌·우클릭은 Pressed 또는 Held, E는 Pressed를 사용한다. TempGrenade 발사각은 12도이며 선딜·후딜·쿨타임은 0이다. 현재 소총 후딜은 좌클릭 0.25초·우클릭 0.5초이고 좌클릭 탄퍼짐은 3도다. Launcher는 Attack.Data.RepeatWhileHeld가 true이면 Pressed 또는 Held, false이면 Pressed에서 요청한다. 새 데이터 기본값은 false이며 같은 SO를 쓰는 바인딩은 반복 정책을 공유한다. 요청은 해당 Attack의 시간을 지키며 발사 간격의 최종 밸런스는 후속 작업이다.
- 좌클릭은 실제 히트스캔이며 우클릭 물리 Bullet은 임시 별도 공격이다. 둘 다 Attack의 선딜·후딜·쿨타임을 지키며 홀딩 반복 발사한다. 버튼 해제는 새 요청을 중단하고 이미 수락한 선딜은 완료할 수 있다. 홀딩 사격 중 조준을 적용하고 새 시각 재장전보다 사격을 우선한다. 최종 소총의 탄약·실제 재장전은 아직 없다.
- 수류탄은 E에 연결했다. 우클릭은 임시 Bullet 발사이며 정밀 조준은 후속 작업이다.
- `IDamageable`은 존재하지 않는다. 새 공격은 Combatant·Hitbox·DamageResolver를 거쳐 Mortal에 피해를 준다. 기존 인자 4개짜리 Pool.Spawn은 피해 정보 없는 발사이고 새 ProjectileLaunchData 전달 경로만 실제 투사체 피해를 실행한다. 프리팹 YAML의 옛 `damage/damageMask` 값은 사용하지 않는다.
- 공통 `Mortal`의 HP 기본값은 100이며 Inspector에서 시작값을 설정한다. 공격자는 `GameObject`이며 환경 피해는 null을 허용한다. 무적·사망 상태와 0 이하 피해는 무시하고 HP는 0 아래로 감소하지 않는다. `Dead()` 직접 호출은 무적과 무관하게 HP를 0으로 만들며, 이동·사격 중단·애니메이션·객체 삭제·부활은 처리하지 않는다. 현재 SampleScene의 Player에는 Mortal·Combatant를 연결했다. 적 AI·프리팹의 체력·사망 행동 연결은 후속 작업이다.
- X Bot은 임시 시각 에셋이며 현재 씬에 적용했다. 기존 `Run Backwards`는 삭제하고 `Run Backward`로 교체했다. 이전 원본 루트 검사에서는 수평 누적 변위가 한 주기 약 0.0034mm, 5주기 약 0.0175mm, 주기 중 최대 편차가 약 2.43cm였다. 현재는 Animator의 Apply Root Motion을 끄고 Motor가 이동한다. 상체 마스크·양팔 IK·점프 연결과 기본 애니메이션 작업은 완료했으며 소총은 기본 도형으로 만든 임시 메시다. 최종 무기 에셋·손가락 정렬은 별도 시각 완성도 작업이고, 남은 Soldier 애니메이션은 Dash·Skill1·Skill2·Ultimate다.
- 2026-10-12 대기·전후좌우 이동 5개 클립에 적용한 Rotation·Position(Y/XZ)의 Bake Into Pose와 Loop Pose를 유지한다. Rotation·XZ는 Original 기준이며 2026-10-14 후속 요청으로 Rotation Offset만 모두 0으로 되돌렸다. 평균 골반 방향을 전방에 맞추던 보정은 제거했으며, 이전 정렬값과 측정은 플레이어 조작 가이드 14절의 과거 기록이고 현재 설정은 16절이다. FBX·Avatar·클립 GUID와 프레임 범위를 유지했고 Soldier Visual의 Transform은 변경하지 않았다. 점프·사격·재장전의 임포트 설정은 기존 값이다. 상체 보정은 플레이어의 `transform.forward` 기준이며 소총 자세의 몸통 비틀림을 유지한다. 몸통 보정은 기존 80도 제한·Spine Weight를 사용하고 총구와 양손 IK가 실제 조준점을 맞춘다. 루트 누적과 반복 이음새를 다루는 임포트 보정은 유지하지만 클립별 기본 골반 방향이 서로 같다고 가정하지 않는다.
- 점프 3종은 X Bot Avatar를 공유하며 `Jump Up`은 약 0.533초, `Jump Loop`는 1초, `Jump Down`은 약 0.667초다. 모두 60 FPS·압축 Off이며 Loop만 Loop Time을 켰다. 원본 루트 이동은 에셋에 남아 있지만 씬 Animator에서는 적용하지 않는다. 실제 접지·수직 속도로 상승·공중·착지 상태를 전환한다. 재생 속도는 Up 1.5배, Down 1.4배이며 기본 점프 애니메이션 작업은 2026-10-15 사용자 확인으로 완료 처리했다.
- R은 약 3.3초의 상체 재장전 시각 동작이며 탄창·탄약을 변경하거나 사격을 막지 않는다. 재장전 중 발사하면 조준 자세와 사격 동작으로 전환한다. 사격 트리거는 실제 발사 성공 뒤 다음 Animator 평가에서 처리되며 시각 총구 조준과 Attack 실행은 같은 프레임의 LateUpdate에서 처리된다. Time.timeScale이 0이면 Animator 평가와 조준 보정을 함께 멈추고 재개 시 복구한다.
- E의 TempGrenade는 2.5초 후 기본 범위 피해 100과 폭발 구체 연출을 실행한다. 근접·폭발의 넉백·엄폐물 차단·거리별 감쇠는 미구현이다. 수류탄 조준은 초기 발사각만 보정하며 정확한 착탄 탄도 계산은 없다.
- 풀은 프리팹별 고정 등록 방식으로 동적 등록·자동 축소를 지원하지 않는다. 투사체끼리의 충돌 제외도 별도로 설정하지 않았다. 조준은 32개 버퍼의 `RaycastNonAlloc`을 사용하며 버퍼가 가득 찬 경우 정확한 최근접 판정을 위해 `RaycastAll`로 재검사한다.
- 임시 `OnGUI` 크로스헤어와 `PlayerPrototypeSetup`, `PlayerInputValidation`, `ProjectilePrototypeSetup`은 제거된 상태다. 삭제된 도구의 실행을 전제로 작업하지 않는다.

## 5. 앞으로의 구현 순서

사용자가 지정한 작업을 먼저 진행하되, 전체 개발은 다음 흐름을 기준으로 한다.

1. **조작 확인:** 입력·이동·카메라 기반과 Soldier 기본 애니메이션 작업·이동 문제 해결은 완료했다. 후속 Soldier 애니메이션은 Dash·Skill1·Skill2·Ultimate이며 새 행동·전투 기능을 연결하면서 실제 조작 감각을 확인한다.
2. **최소 전투:** 체력·피해·사망, 소총 사격·탄창·재장전, 근접형·원거리형 적을 작은 테스트 아레나에서 연결하고 검증한다.
3. **반복 플레이:** 웨이브·클리어 판정·강화 3개 중 선택·강화 적용을 만들고 Shift 실제 대시를 구현한 스태미나 자원에 연결한다. E의 임시 수류탄을 바탕으로 최종 밸런스와 행동 제한을 확정한다. F의 Skill2 효과, 정밀 조준과 행동 제한도 구체화한다.
4. **한 판 완성:** 10웨이브, 보스 패턴과 단계 변화, 성공·실패·결과·재시작 및 상태 초기화를 완성한다.
5. **완성도 개선:** HUD·보상·결과 UI, 사운드·효과·애니메이션, 밸런스, 테스트와 성능 개선을 진행한다.
6. **확장:** 초기 흐름 완성 후 Assassin·Tank를 진행하고 Charger·강화 개체 등 추가 콘텐츠를 검토한다.

아직 확정하지 않은 항목은 해당 기능 개발 시 구체화하고 결정 내용을 이 문서 또는 관련 상세 가이드에 남긴다.

- 피해량, 체력, 이동속도, 탄창·재장전·발사 간격, 스킬 쿨타임.
- 스태미나 최대량·대시 소모량·초당 회복량·회복 지연 시간, 대시 거리·속도·무적 여부. 별도 PlayerStamina 컴포넌트, 게임 시간 기준 자동 회복, 쿨타임 없이 스태미나를 소모하는 이동기 구조는 확정했다.
- Skill2 효과, 정밀 조준 감각, 재장전·대시·스킬 간 행동 제한, 궁극기 효과·발동 조건.
- 웨이브별 적 수·배치·등장 간격, 보스 세부 패턴·단계 전환 조건.
- 강화 목록·등급·등장 확률·중첩·선행 조건.
- UI 구성, 에셋, 애니메이션과 연출.
- 새 기능의 코드 구조, 데이터 관리, 테스트와 최적화 방법.

## 6. 검증 방법과 기록

변경한 기능에 맞게 컴파일, 씬·프리팹 참조, 실제 동작을 확인한다. 문서만 변경한 경우 내용·경로·링크 검사를 수행한다. 플레이 감각은 `SampleScene`의 Game 뷰에서 확인한다.

자동 검증은 `ProjectU.Editor.ProjectileValidation.Run()`, `ProjectU.Editor.MortalValidation.Run()`, `ProjectU.Editor.PlayerAnimationValidation.Run()`, `ProjectU.Editor.PlayerStaminaValidation.Run()`, `ProjectU.Editor.AttackValidation.Run()`, `ProjectU.Editor.PlayerAttackValidation.Run()`이다. 공격 검증 범위와 실행 방법은 공격 구현 가이드를 참고한다. 투사체 검증은 동일 프로젝트를 연 Unity 에디터가 닫힌 상태에서 다음 PowerShell 명령을 사용한다.

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

후속 작업에서는 기준 날짜와 함께 구현한 기능, 남은 제약, 실제 수행한 검증을 갱신해 이 문서의 진행 상태를 최신으로 유지한다.
