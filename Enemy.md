# Enemy 구현 가이드

기준 날짜: **2026-11-01**. 이 문서는 현재 실행 코드·프리팹·SampleScene을 기준으로 적의 상태, 행동, 이동, 공격, 사망과 새 적 추가 방법을 설명한다. 검증 결과는 마지막 절에 별도로 기록한다.

## 1. 현재 구현 범위

- `TempEnemy.prefab`은 빨간 캡슐 메시와 CapsuleCollider를 사용하는 근접형 적이다. SampleScene에 연결된 프리팹 인스턴스 5기가 있다.
- 시각 감지 또는 실제 피격으로 플레이어를 인식하고, NavMesh로 추격한 뒤 근접 피해를 준다.
- `Mortal`의 HP가 소진되면 `OnDead`에서 Dead 상태로 전환하고 `Destroy(gameObject)`를 호출한다. 실제 제거는 Unity의 프레임 종료 시점에 일어난다.
- 플레이어·적은 `ActionBase`의 행동 생명주기를 공유한다. 공격 종류는 `Attack` 컴포넌트로 지정한다.
- 적 전용 애니메이션, 웨이브 생성·클리어, 수색·귀환, 보스 패턴과 돌진형 적은 아직 구현하지 않았다.

## 2. 파일과 책임

런타임 네임스페이스는 `ProjectU`, 에디터 도구는 `ProjectU.Editor`다.

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/Enemy/EnemyAI.cs` | 대상과 FSM 소유, 감지·피격·사망 연결, 현재 상태 알림 |
| `Assets/Scripts/Enemy/EnemySensor.cs` | 시야 거리·수평 시야각·장애물 검사, 플레이어 후보 참조 |
| `Assets/Scripts/Enemy/EnemyMotor.cs` | NavMeshAgent의 목적지·정지·회전, 접지와 경로 조회 |
| `Assets/Scripts/Enemy/EnemyCombat.cs` | 액션 목록·우선순위·시작 요청·취소. 타이머는 보유하지 않음 |
| `Assets/Scripts/Enemy/EnemyAction.cs` | 적 전용 실행 조건·대상·Motor/Combat 연결, 대상 무효화 처리 |
| `Assets/Scripts/Enemy/EnemyAttackAction.cs` | Attack 참조·공격 가능 위치·방향·피해 배율과 실행 |
| `Assets/Scripts/Enemy/States/EnemyStateBase.cs` | 네 상태의 추상 부모, `Enter / Tick / Exit` 계약 |
| 같은 폴더의 `EnemyIdleState / EnemyChaseState / EnemyCombatState / EnemyDeadState` | 상태별 실제 행동 |
| `Assets/Scripts/Combat/ActionBase.cs` | 공통 생명주기·접지 조건·이동 제한·타이머·취소·이벤트 |
| `Assets/Scripts/Combat/ActionTypes.cs` | `ActionContext / ActionPhase / ActionResult / ActionCancelReason` |
| `Assets/Scripts/Combat/Combatant.cs` | 진영·생존과 같은 유닛의 ActionBase 제한 집계 |
| `Assets/Scripts/Combat/WorldObstruction.cs` | 시야·근접 공격의 고체 월드 장애물 검사 |
| `Assets/Scripts/Combat/MeleeAttack.cs` | 근접 범위·높이·각도·벽 가림 판정과 실제 피해 |
| `Assets/Scripts/Mortal.cs` | HP·무적·피해·사망 이벤트 |
| `Assets/Prefabs/Enemies/TempEnemy.prefab`, `TempEnemy.mat` | 재사용 가능한 적 프리팹과 임시 URP 머티리얼 |
| `Assets/Scenes/Navigation/SampleSceneNavMesh.asset` | 씬의 베이크된 이동 영역 |
| `Assets/Editor/TempEnemySetup.cs` | 프리팹·NavMesh·5기 배치를 만드는 에디터 도구 |
| `Assets/Editor/EnemyValidation.cs` | 저장된 씬과 격리된 검증 아레나를 사용하는 자동 플레이 검증 |

## 3. 상태 구조와 전이

```text
EnemyStateBase (일반 C# 추상 클래스)
├─ EnemyIdleState
├─ EnemyChaseState
├─ EnemyCombatState
└─ EnemyDeadState
```

각 EnemyAI가 네 상태 인스턴스를 따로 생성한다. 현재 상태의 `Tick()`만 호출하며 전이할 때 이전 상태의 `Exit()`와 새 상태의 `Enter()`를 호출한다. 상태 인스턴스에 저장된 정보가 다른 적과 공유되지 않는다. `State`는 읽기 전용이고 `OnStateChanged(EnemyState)`로 변경을 알린다.

| 상태 | 행동 | 전이 |
|---|---|---|
| Idle | 이동 정지, 일정 간격으로 시야 검사 | 감지·피격 시 Chase |
| Chase | 현재 플레이어 위치로 이동·회전 | 실행 가능한 액션을 수락하면 Combat |
| Combat | 일반 이동 정지, 액션 종료 및 다음 실행 기회 대기 | 후딜 종료 후 공격 가능한 위치를 벗어나면 Chase |
| Dead | 모든 적 액션 취소, 이동 정지, 자식 Collider 비활성화 | 종료 상태 |

Chase와 Combat에서 대상이 사망·삭제·비활성화되거나 적대 플레이어 자격을 잃으면 액션을 취소하고 대상을 지운 뒤 Idle로 돌아간다. 적의 사망은 모든 상태보다 우선한다.

처음 인식한 뒤에는 시야에서 벗어나도 대상을 유지하고 현재 위치를 추격한다. 수색 시간·최종 목격 위치·귀환 거리는 현재 사용하지 않는다. 공격 범위 안에서 쿨타임만 남았다면 접근을 멈추고 기다린다. 선딜·후딜 타이머를 상태 전환으로 초기화하지 않는다.

## 4. 감지와 피격

`EnemySensor`의 Inspector 설정은 다음과 같다.

| 필드 | TempEnemy 기본값 | 의미 |
|---|---:|---|
| Player | 프리팹은 비어 있음, 씬 인스턴스는 Player 연결 | 검사할 플레이어 Combatant |
| View Distance | 12m | 눈 위치에서 대상 피격체 중심까지의 최대 거리 |
| View Angle | 120도 | 전방 기준 수평 전체 시야각 |
| Eye Offset | (0, 1.5, 0) | 적 로컬 좌표의 눈 위치 |
| Scan Interval | 0.15초 | Idle에서 시각 감지를 시도하는 게임 시간 간격 |
| Obstruction Mask | Everything | 시야를 막는 고체 월드 레이어 |

Player 참조가 비어 있으면 검사 시 `Player` 태그의 오브젝트에서 Combatant를 찾는다. 생성 시스템은 `EnemySensor.SetPlayer(playerCombatant)`로 참조를 명시할 수 있다. `CanSee(candidate)`는 실제 시야 검사, `TryDetect(out candidate)`는 검사 간격을 포함한 조회다. Scene 뷰에서 적을 선택하면 시야 반경·양쪽 경계선 Gizmo가 보인다.

시각 감지는 **살아 있는 적대 Player 진영 → 거리 → 수평 각도 → 장애물** 순서다. 시야각은 수평이며 거리는 3차원 거리다. 첫 활성 Collider의 bounds 중심을 대상 점으로 사용하고 Collider가 없으면 Transform 위치를 사용한다.

피격은 `Mortal.OnDamaged(amount, attacker)`로 처리한다. 공격자의 Combatant를 확인해 대상에 지정하므로 뒤쪽·시야 밖·벽 너머에서 받은 피해도 인식한다. 환경 피해처럼 공격자가 없거나 플레이어 진영이 아니면 인식 대상으로 삼지 않는다. 무적 때문에 HP가 감소하지 않은 공격은 OnDamaged를 발생시키지 않으므로 현재 피격 인식도 발생하지 않는다.

Mortal은 치명 피해에서도 OnDamaged를 먼저 보낸다. EnemyAI는 이때 HP가 0인지 확인해 Chase 전이를 생략하며, 이어지는 OnDead에서 바로 Dead로 전환한다.

## 5. 이동과 씬 Navigation

NavMeshAgent가 실제 이동을 담당하며 EnemyMotor가 목적지와 정지·회전을 제어한다. Transform을 별도 이동 코드로 중복 갱신하지 않는다.

| 설정 위치 | TempEnemy 기본값 |
|---|---|
| NavMeshAgent | Speed 3.5m/s, Acceleration 16, Radius 0.45m, Height 2m, Stopping Distance 1.6m |
| EnemyMotor | Repath Interval 0.2초, Sample Distance 2m, Turn Speed 540도/초 |

EnemyMotor는 목적지 주변의 NavMesh를 찾아 경로를 갱신한다. 목적지를 샘플링할 수 없으면 이동을 멈추며, 경로가 불완전하면 도달 가능한 부분까지만 이동할 수 있다. NavMesh 도착 여부만으로 공격하지 않는다. 실제 공격 위치·높이·방향·벽 조건을 EnemyAttackAction이 확인한다. 점프 중인 플레이어의 지상 근처로 이동할 수 있어도 실제 근접 범위 높이를 벗어나면 피해를 주지 못한다.

SampleScene의 `Enemy Navigation` 오브젝트에는 NavMeshSurface가 있다. Physics Colliders와 Default 레이어로 베이크하며 NavMeshAgent는 수집에서 제외한다. Player의 Ignore Raycast 레이어도 베이크에서 제외한다. 베이크 에셋은 씬과 함께 저장되어 런타임에 새로 생성하지 않는다.

Player를 움직이지 않는 검증 외에는 기존 PlayerMotor가 플레이어 이동을 담당한다. 적 이동은 ActionBase의 `CanMove=false` 제한, 사망, `Time.timeScale=0`에서 멈춘다.

## 6. 공통 액션 구조

```text
ActionBase : MonoBehaviour
├─ PlayerAction
│  ├─ PlayerAttackAction → 기존 Soilder 기본·보조 발사·Skill1
│  └─ 기존 Soilder Skill2·Ultimate·Dash·Reload
└─ EnemyAction
   └─ EnemyAttackAction → 연결한 MeleeAttack / HitscanAttack / ProjectileAttack
```

ActionBase에 `windupTime / recoveryTime / cooldown / requiresGrounded / canMove`를 저장한다. 공통 시간·상태는 이곳에만 있다. `repeatWhileHeld`, PlayerActionSlot, 선입력 자격, 버튼 해제, 플레이어 실행 단계는 PlayerAction에 남아 있다.

공통 상태는 `Ready → Windup → Active(선택적) → Recovery → Ready`다. 실제 효과 실행은 `ExecuteCore(in ActionContext)`가 담당하고 다음 중 하나를 반환한다.

- `Completed`: 효과를 실행했고 즉시 후딜을 시작한다.
- `Running`: 유지형 행동을 Active로 진행하며 `TickActive(deltaTime)`를 호출한다. `FinishActive()`로 후딜을 시작한다.
- `Failed`: 효과 실행에 실패하고 진행을 끝낸다.

쿨타임은 효과 실행 성공부터 시작한다. 즉시 완료 행동의 후딜과 쿨타임은 동시에 흐른다. `SpeedMultiplier`는 공통 시간 배율이며 `Time.timeScale=0`에서는 진행하지 않는다. 중복 실행·같은 프레임 재진입 제한도 공통 부모가 관리한다.

`ActionContext`에는 실행 위치·Transform·조준점·방향·대상 참조를 전달한다. 플레이어 입력 스냅샷이나 AI 상태를 저장하지 않는다. `CanStartWithContext`는 조회, `TryStart`는 최종 조건 검사와 수락, `Cancel`은 선딜·Active 중단이다. 의존 컴포넌트 중단·사망에서는 후딜 잠금까지 정리한다. 획득한 쿨타임은 취소로 초기화하지 않는다.

`OnStarted / OnExecuted / OnCompleted / OnFailed / OnCancelled`를 제공한다. Attack이 없는 행동도 이 이벤트를 사용한다. `Combatant`는 같은 오브젝트의 모든 ActionBase를 모아 이동·행동 제한을 집계하고, 플레이어 슬롯별 제한은 PlayerAction에서 해석한다.

플레이어의 기존 실행 순서를 유지한다. 적은 EnemyAI Update 80 → EnemyMotor Update 90에서 선택·이동을 요청하고 EnemyAction LateUpdate 110에서 액션을 진행한다. PlayerAction의 BeforeMotor·CombatLateUpdate·AfterAim 구분은 공통 부모에 플레이어 의존성을 만들지 않고 PlayerAction에서 처리한다.

## 7. 적 액션 선택과 공격

EnemyCombat의 `Actions` 배열은 위에서부터 우선순위다. `TryStartAvailable(target)`는 현재 실행 가능한 첫 액션을 시작한다. 같은 적의 다른 액션이 제한 중이면 시작하지 않는다. 같은 액션·Attack 실행기를 배열에 중복 연결하거나 다른 오브젝트의 액션을 연결하면 구성을 거절한다.

EnemyAction은 `ActionBase`를 직접 상속하며 `CanStartFor(target) / TryStartFor(target) / IsInPosition(target)`를 제공한다. EnemyCombat은 시간 계산을 하지 않는다. 타이밍과 정리는 해당 액션 및 ActionBase의 책임이다.

EnemyAttackAction의 Attack 필드에는 세 공격 종류를 모두 연결할 수 있다.

| 연결한 공격 | 시작 위치 판정 | 실행 |
|---|---|---|
| MeleeAttack | 실제 근접 범위·높이·각도·벽 검사 | 수락 시 전방을 고정하고 선딜 완료 시 현재 Origin에서 타격 |
| HitscanAttack | Attack Distance·Facing Tolerance·가림 검사 | 실행 시 대상의 현재 피격점으로 히트스캔 |
| ProjectileAttack | 위와 같은 원거리 자격 검사 | 대상의 현재 피격점을 향해 연결한 풀에서 투사체 발사 |

근접 공격은 시작할 때 사거리 안이었어도 선딜 중 피할 수 있다. 타격 시점에 고정된 방향의 실제 범위에 없는 대상은 맞지 않으며 후딜은 진행한다. 대상 자체가 사망·삭제·비활성화되면 대기 행동을 취소한다. 타격 한 번으로 여러 Collider가 같은 유닛을 중복 타격하지 않는다.

현재 TempEnemy는 `StrikerMeleeAttack.asset`을 사용한다. **피해 15·반경 2m·전방 90도·높이 2m·OriginOffset (0,1,0)**이며 Attack.Origin은 적 루트다. 액션의 **선딜 0.5초·후딜 0.8초·쿨타임 1.2초·접지 필요·이동 금지**를 저장했다. Damage Multiplier는 1이다. 범위와 정지 거리는 다른 설정이므로, 정지 거리가 실제 공격 가능 거리보다 길면 접근 후 공격하지 못할 수 있다.

`Attack.TryExecute(ActionBase, ...)`는 같은 소유자의 실행 중인 액션만 수락한다. 피해는 기존 `DamageContext → DamageResolver → Mortal.TakeDamage`를 사용한다. 실제 피해와 타이밍을 새 AI 쪽에 중복 구현하지 않는다.

## 8. 모든 근접 공격의 벽 가림

MeleeAttack은 플레이어·적과 Circle·Sector 모두 벽 검사를 적용한다. 비활성화 토글은 없다. MeleeAttackData의 Obstruction Mask는 장애물 레이어이며 기본 Everything이다.

범위에 들어온 각 Collider의 최근접점까지 공격 범위 중심에서 고체 월드를 검사한다. 검사 시작점이 월드 고체 안에 있는 경우도 가림으로 처리한다. Trigger, Hitbox로 연결된 캐릭터, Projectile은 벽으로 취급하지 않는다. 유효한 부위만 DamageResolver에 넘기고 같은 Combatant의 피해는 한 번으로 모은다. 여러 부위 중 가리지 않은 부위가 범위에 있으면 해당 부위로 타격할 수 있다.

이 규칙은 기존 근접 공격에도 적용된다. 수류탄 폭발의 엄폐물 판정은 별도 후속 작업이며 이번 근접 변경에 포함되지 않는다.

## 9. 체력과 사망 수명

TempEnemy는 일반 Mortal을 그대로 사용하고 시작 HP는 100이다. 별도 적 전용 체력 저장소는 없다. 적대 공격에 피해를 받으며 무적·실제 HP 감소량·공격자 전달·사망 이벤트는 Mortal이 처리한다.

현재 수명 흐름은 다음과 같다.

```text
Mortal.OnDead
→ EnemyAI.HandleDeath
→ EnemyDeadState.Enter
→ 진행 액션 취소 + 이동 정지 + Collider 비활성화
→ Destroy(gameObject)
```

EnemyAI는 Awake에서 OnDead를 구독하고 OnDestroy에서 해제한다. AI 컴포넌트를 일시적으로 꺼도 Mortal.OnDead의 제거 처리는 유지한다. 피해 인식 이벤트는 AI 활성 기간에만 구독한다.

향후 사망 애니메이션을 붙일 때는 HandleDeath의 즉시 Destroy 호출을 사망 연출 시작으로 변경한다. Dead 상태 진입과 행동 정리는 유지하고, 연출 종료 후 비활성화한 다음 스테이지 관리자가 보관 목록을 일괄 Destroy하는 방향이다. 현재는 지연 제거·적 풀·재활성화 시 HP 초기화를 구현하지 않았다.

## 10. 새로운 Enemy 만들기

### A. 같은 근접 행동을 사용하는 적

1. `Assets/Prefabs/Enemies/TempEnemy.prefab`을 복제하거나 Prefab Variant를 만든다.
2. 루트의 Mortal·Combatant·NavMeshAgent·EnemyMotor·EnemySensor·EnemyCombat·EnemyAI·EnemyAttackAction·MeleeAttack과 Collider 연결을 유지한다.
3. `Capsule Visual` 자식을 새 메시로 교체한다. 적 루트는 발밑 기준이고 이동 Collider는 루트에서 관리한다. 모델 애니메이션의 루트 이동과 NavMeshAgent 이동을 동시에 적용하지 않는다.
4. Mortal의 HP, Agent의 속도·반경·정지 거리, Sensor의 시야·거리·눈 위치를 편집한다.
5. 개별 밸런스가 필요하면 `Create > ProjectU > Combat > Melee Attack`으로 데이터를 만들거나 기존 에셋을 복제한다. Damage·Range·Height·SectorAngle·OriginOffset·마스크를 설정하고 MeleeAttack.Data에 연결한다.
6. Attack.Origin을 루트 또는 공격 기준 Transform에 연결한다. 이미 가슴 높이 Origin을 사용한다면 OriginOffset.y를 다시 더하지 않도록 조정한다.
7. EnemyAttackAction.Attack과 EnemyCombat.Actions를 연결하고 선딜·후딜·쿨타임·RequiresGrounded·CanMove를 설정한다.
8. 베이크된 NavMesh 위에 배치하고 Player 참조를 연결한다. 런타임 생성 시에도 유효한 NavMesh 위치에서 생성하고 SetPlayer로 대상을 전달한다.

단순 피해·외형·감지 범위 차이에는 새로운 C# 액션 클래스가 필요하지 않다.

### B. 원거리 공격을 사용하는 적

1. EnemyAttackAction은 그대로 사용하고 HitscanAttack 또는 ProjectileAttack을 연결한다.
2. 해당 AttackData와 Origin을 지정한다. ProjectileAttack에는 투사체 프리팹이 등록된 ProjectilePool도 연결한다.
3. EnemyAttackAction의 Attack Distance·Facing Tolerance·Obstruction Mask를 설정한다. HitscanAttackData의 실제 사거리와 공격 시작 거리를 일치시킨다.
4. 기존 근접 Attack을 더 이상 사용하지 않으면 제거하고 EnemyCombat의 액션 연결을 확인한다.

현재 FSM은 공격 위치까지 접근하는 공통 패턴이다. Gunner의 거리 유지·후퇴·측면 이동까지 자동으로 추가되지는 않으며 해당 이동 정책은 후속 구현이 필요하다.

### C. 공격 외 행동이나 다른 실행 흐름

공격 없는 이동·방어·소환은 `EnemyAction`을 상속한다. `IsInPosition`으로 선택 가능한 위치, `CheckRequirements`로 시작 조건, `ExecuteCore`로 실제 효과를 구현한다. 선딜이 끝날 때 별도의 재검사가 필요하면 `CheckExecutionRequirements`를 재정의한다.

지속 행동은 `Running`을 반환하고 `TickActive`에서 진행한 뒤 `FinishActive()`를 호출한다. `Cleanup`에서는 이동 요청·임시 효과 등 소유한 상태를 정리한다. 이후 EnemyCombat.Actions에 연결하면 공통 선택과 타이밍을 사용한다.

돌진처럼 공격과 이동 흐름을 함께 바꾸는 경우에는 필요에 따라 EnemyAction 또는 EnemyAttackAction의 파생 클래스를 만든다. 단순히 근접/투사체 종류가 다르다는 이유로 EnemyMeleeAction·EnemyProjectileAction을 각각 만들지는 않는다. 보스의 단계별 액션 선택·확률·연속 패턴과 Active 중 반복 피해는 그 패턴 구현 시 명시적으로 추가해야 한다.

## 11. 씬 도구와 검증

- `ProjectU > Setup > Create TempEnemy Demo`: SampleScene을 열어 TempEnemy 에셋, Enemy Navigation, Temp Enemies 그룹과 5기 배치를 구성한다. 해당 데모 프리팹·그룹을 다시 구성하므로 개별 적을 편집할 때 반복 실행하지 않는다.
- `ProjectU > Validation > Validate Enemies`: 씬의 프리팹 연결은 Edit 모드에서, 실제 동작은 Play 모드에서 검사한다. 검증은 씬·프리팹을 저장하지 않는다.
- `ProjectU > Validation > Capture Enemy Scene Preview`: 그래픽 장치가 있는 에디터에서 씬 전체와 캡슐 근접 이미지를 렌더링한다. 씬은 저장하지 않는다.
- 기존 플레이어 검증은 `EnemyValidationIsolation.DisableDemoEnemies()`로 데모 적만 실행 중 비활성화한다. 실제 적 상호작용은 EnemyValidation이 별도로 검증한다.

**이번 직접 검증: 2026-11-01, Unity 6000.3.25f1.** 다음 13종이 모두 종료 코드 0으로 통과했다. 정량 검사 합계는 **582개**이며 애니메이션·스태미나·Mortal·Projectile의 별도 시나리오 통과도 함께 확인했다.

| 검증 | 이번 결과 |
|---|---|
| EnemyValidation | 70개 + Edit 모드의 프리팹 5기 연결 검사 |
| PlayerActionRestrictionValidation | 39개 |
| PlayerActionValidation | 43개 |
| AttackValidation | 75개 |
| PlayerAttackValidation | 50개 |
| PlayerDashValidation | 70개 |
| PlayerReloadValidation | 40개 |
| PlayerActionDelayValidation | 110개 |
| EnhancedInputValidation | 85개 |
| PlayerAnimationValidation | 17발·이동·점프·IK·장전·투척·벽·정지 |
| PlayerStaminaValidation | 능력치·소모·회복·정지·이벤트 |
| MortalValidation | 피해·무적·치명 피해·이벤트 순서·중복 사망·재진입 |
| ProjectileValidation | 풀·비행·중력·충돌·수명·발사자 제외·재사용 |

EnemyValidation은 씬에서 실제 플레이어 피해, 시야 거리·각도·벽, 가려진 공격자의 피격 인식, NavMesh 우회, 선딜/후딜·회피·정지·대상 무효화, Dead 전환과 Destroy, 플레이어/적·원/부채꼴 근접 벽 검사, 적의 히트스캔·투사체, 공격 없는 Active 행동, 플레이어 공격 액션의 뒤쪽 사격으로 적 인식·HP 감소·제거까지 확인했다.

로그·종료 코드는 `Logs/Enemy_20261101/RegressionResults.json`과 각 검증 이름의 `.log`, 전체 요약은 `ValidationSummary.json`이다. 초기 프리팹 연결 검사를 Play 모드에서 시도한 실패는 `EnemyValidation.Initial.log`이며 Edit 모드로 이동한 뒤 최종 통과했다. `Second.log`는 확장 전 62개 결과다. 초기 샌드박스 Unity 라이선스와 .NET 접근/복원 실패는 최종 결과와 구분한다.

.NET 런타임·에디터 빌드는 경고 0·오류 0이다. 기존 216개 에셋 중 누락 0, 기존 meta 변경 0, 기존 공격 데이터·투사체·애니메이션 변경 0, GUID 중복 0을 확인했다. SceneRoots에 두 루트를 추가한 것 외에 기존 씬 블록 115개를 그대로 보존했다. `git diff --check`도 통과했다.

그래픽 배치 렌더링의 `EnemyScene.Overview.png`와 `TempEnemy.Closeup.png`를 직접 열어 5기 배치·캡슐 메시·머티리얼을 확인했다. 이 이미지는 Edit 모드 렌더링이며 실제 키·마우스를 사용하는 수동 플레이는 이번에 수행하지 않았다. 이전 날짜의 검증 결과를 이번 결과에 합산하지 않았다.

## 12. 남은 작업

- 적 메시·피격·공격 예고·공격·사망 애니메이션과 사운드.
- Dead 연출 → 일정 시간 후 비활성화 → 스테이지 종료 시 일괄 Destroy.
- Gunner의 거리 유지, 돌진형 적, 보스 패턴과 단계 전환.
- 웨이브 생성·적 생존 수·강화 선택·결과·재시작.
- 플레이어의 사망 시 이동 중단·연출·복귀와 HP HUD.
- 최종 밸런스, 다수 적의 경로·회피·감지 및 할당 비용 최적화.
