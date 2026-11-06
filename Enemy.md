# Enemy 구현 가이드

기준 날짜: **2026-11-06, 스테이지 진행 연결**. 이 문서는 현재 실행 코드·프리팹·SampleScene을 기준으로 적의 능력치, 상태, 행동, 이동, 공격, 사망과 새 적 추가 방법을 설명한다. 검증 결과는 11절, 스폰 구조 요약은 13절, 상세 규칙과 데이터 작성법은 [스폰 규칙.md](스폰%20규칙.md), 진행 클래스의 책임은 [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md)에 기록한다.

2026-11-06 직접 회귀: EnemyValidation 95개·EnemySpawnerValidation 90개·StageDataDictionaryValidation 42개를 다시 종료 코드 0으로 확인했다. 새 진행·자동 시작·전체 전투 회귀와 플레이어 컴파일 결과는 [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md) 10절 및 `Logs/StageProgression_20261106/`에 기록하며 이전 날짜 결과에 합산하지 않는다.

## 1. 현재 구현 범위

- `TempEnemy.prefab`은 빨간 캡슐 메시와 CapsuleCollider를 사용하는 근접형 적이다. 편집 상태에는 적 없이 50×50m 아레나와 스포너 4개를 저장한다. 2026-11-06 3단계 리팩토링 후에는 Play 시작 시 BeforeStage에서 기다리고 G 상호작용으로 TempStage 첫 웨이브를 실행한다. 보상 더미 UI 선택 완료 후에도 다음 상호작용까지 적을 생성하지 않는다.
- `EnemySpawnManager`가 적 프리팹 등록·그룹 배정·전체 위치 확보 후 생성을 담당한다. StageRunner가 이 API를 호출하고 반환한 적을 생존 목록에 등록해 웨이브 전이·클리어를 판단한다.
- 최대 HP·이동속도·공격력 배율·공격속도 배율은 `EnemyStatData` ScriptableObject에서 설정한다. `EnemyStat`이 생성 시 값을 복사해 적용하며 현재 HP는 적마다 Mortal이 관리한다.
- 시각 감지 또는 실제 피격으로 플레이어를 인식하고, NavMesh로 추격한 뒤 근접 피해를 준다.
- `Mortal`의 HP가 소진되면 `OnDead`에서 Dead 상태로 전환하고 `Destroy(gameObject)`를 호출한다. 실제 제거는 Unity의 프레임 종료 시점에 일어난다.
- 플레이어·적은 `ActionBase`의 행동 생명주기를 공유한다. 공격 종류는 `Attack` 컴포넌트로 지정한다.
- 적 전용 애니메이션, 수색·귀환, 보스 패턴과 돌진형 적은 아직 구현하지 않았다.

## 2. 파일과 책임

런타임 네임스페이스는 `ProjectU`, 에디터 도구는 `ProjectU.Editor`다.

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/Enemy/EnemyStatData.cs` | 공유하는 기본 능력치 ScriptableObject, 읽기 전용 속성과 값 검증 |
| `Assets/Scripts/Enemy/EnemyStat.cs` | 데이터 참조, 생성 시 능력치 복사·Mortal HP 초기화·NavMeshAgent 속도 적용 |
| `Assets/Data/Enemies/TempEnemyStat.asset` | 생성되는 TempEnemy가 공유하는 최대 HP 100·이동속도 3.5·공격 배율 1·공격속도 1 설정 |
| `Assets/Scripts/Enemy/EnemyType.cs` | 안정적인 정수 값을 갖는 적 종류 enum |
| `Assets/Scripts/Enemy/EnemySpawnManager.cs` | 공통 enum→프리팹 등록, 스포너 셔플·그룹 배정·전체 생성 API |
| `Assets/Scripts/Enemy/EnemySpawner.cs` | 개별 영역의 NavMesh·충돌·공유 예약 검사와 실제 생성 |
| `Assets/Scripts/Stage/StageData.cs`, `WaveData.cs`, `EnemySpawnGroup.cs` | 스테이지→웨이브→그룹→종류/수량 데이터와 검증 |
| `Assets/Data/Stages/TempStage.asset` | 실행 호출을 연결하지 않은 3웨이브 편집 예제 |
| `Assets/Editor/StageDataEditor.cs` | 잘못된 데이터의 이유를 Inspector에 표시 |
| `Assets/Editor/StageDataDictionaryValidation.cs` | 딕셔너리 검증·복사·중복·실제 에셋 저장/재로드·이관 값 검사와 플레이어 스크립트 컴파일 |
| `Assets/Editor/StageSetup.cs` | 50×50m 바닥·벽, 기존 적 제거, 스포너 4개와 NavMesh를 저장하는 도구 |
| `Assets/Editor/EnemySpawnerValidation.cs` | 진행 매니저를 격리한 씬 구성·혼합 배치·충돌·실패 처리 자동 플레이 검증 |
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
| `Assets/Scripts/Mortal.cs` | 현재 HP·무적·피해·사망 이벤트, 생성 시 한 번의 HP 초기화 |
| `Assets/Prefabs/Enemies/TempEnemy.prefab`, `TempEnemy.mat` | 재사용 가능한 적 프리팹과 임시 URP 머티리얼 |
| `Assets/Scenes/Navigation/SampleSceneNavMesh.asset` | 씬의 베이크된 이동 영역 |
| `Assets/Editor/TempEnemySetup.cs` | 프리팹·NavMesh·5기 배치를 만드는 에디터 도구 |
| `Assets/Editor/EnemyValidation.cs` | 저장된 씬과 격리된 검증 아레나를 사용하는 자동 플레이 검증 |

### 적 능력치 에셋 사용법

Project 창에서 `Create > ProjectU > Enemy > Stat Data`로 에셋을 만들고 적 프리팹 루트의 `EnemyStat > Data`에 연결한다. 기본 적은 `Assets/Data/Enemies/TempEnemyStat.asset`을 편집하면 된다. 종류별로 다른 값을 사용하려면 에셋을 복제해 해당 프리팹에 연결한다.

| Inspector 필드 | 기본값 | 적용 대상 |
|---|---|---|
| Max Health | 100 | 생성 시 Mortal의 시작·현재 HP |
| Move Speed | 3.5m/s | NavMeshAgent.speed |
| Attack Multiplier | 1 | 각 EnemyAttackAction의 피해 배율에 추가로 곱함 |
| Attack Speed | 1 | 각 EnemyAttackAction의 선딜·후딜·쿨타임을 나누는 배율 |

최대 HP는 최소 1, 이동속도·공격력 배율은 최소 0, 공격속도 배율은 최소 0.01이다. 음수와 NaN·Infinity는 각 최소값으로 보정한다. 에셋에는 현재 HP·대상·쿨타임 등의 실행 상태를 저장하지 않는다. 공격의 기본 피해·범위·투사체 설정은 AttackData, 기본 타이밍은 액션, 가속도·반경·정지 거리는 NavMeshAgent, 회전 속도는 EnemyMotor, 시야 설정은 EnemySensor에서 관리한다.

`EnemyStat.Awake`는 실행 순서 -150으로 AI·공격 액션보다 먼저 데이터를 각 인스턴스의 읽기 전용 속성에 복사한다. 에셋 또는 Data 참조를 실행 중 바꿔도 이미 생성한 적에는 재적용하지 않는다. 이후 생성한 적부터 변경값을 사용한다. 오브젝트나 컴포넌트를 껐다 켜도 현재 HP를 회복하거나 사망 상태를 되돌리지 않는다. 런타임 강화·감속·웨이브 배율과 풀 재생성 API는 후속 작업이다.

데이터가 없거나 피해·사망 이후 초기화를 시도하면 오류를 기록하고 EnemyAI를 비활성화한다. 프리팹 Mortal의 `hp`와 NavMeshAgent의 `Speed` 저장값은 시작 시 데이터로 덮어쓰므로, 적의 HP·속도 밸런스는 연결된 에셋에서 편집한다. 플레이어는 같은 날짜의 후속 작업으로 PlayerStat.Awake에서 MaxHealth를 Mortal 시작 HP에 적용한다.

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
| Player | 프리팹은 비어 있음, 매니저가 생성 후 Player 연결 | 검사할 플레이어 Combatant |
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
| EnemyStatData | Move Speed 3.5m/s → 생성 시 NavMeshAgent.speed에 적용 |
| NavMeshAgent | Acceleration 16, Radius 0.45m, Height 2m, Stopping Distance 1.6m |
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

EnemyStat이 있는 적은 `AttackData.Damage × EnemyStat.AttackMultiplier × EnemyAttackAction.damageMultiplier`를 피해 계산에 전달한다. 치명타와 정수 반올림은 기존 DamageResolver 규칙을 따른다. `EnemyStat.AttackSpeed`는 공격 액션의 `SpeedMultiplier`에 적용한다. 예를 들어 2이면 기본 선딜 0.5초·후딜 0.8초·쿨타임 1.2초가 각각 0.25초·0.4초·0.6초가 된다. 공격 없는 EnemyAction에는 공격속도를 적용하지 않는다. EnemyStat 없이 단독 사용하는 공격 어댑터는 기존 배율 1을 사용한다.

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

TempEnemy는 일반 Mortal을 사용하며 `EnemyStatData.MaxHealth`를 시작 HP로 적용한다. 기본 에셋은 100이다. 최대 HP는 EnemyStat의 생성 시 복사값, 현재 HP는 Mortal.HP가 담당한다. 같은 에셋을 공유해도 한 적의 피해가 다른 적이나 에셋을 변경하지 않는다. 무적·실제 HP 감소량·공격자 전달·사망 이벤트는 Mortal이 처리한다.

`Mortal.TryInitializeHealth(int maxHealth)`는 첫 초기화에만 성공하고, 최초 실제 피해 또는 사망 이후에는 초기화를 거절한다. 최소 HP는 1이며 초기화로 피해·사망 이벤트를 발생시키거나 무적 설정을 변경하지 않는다. EnemyStat과 PlayerStat은 각각 Awake에서 이 API를 한 번 호출한다. 두 Stat을 사용하지 않는 일반 Mortal은 기존 직렬화된 시작 HP를 유지한다.

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
2. 루트의 EnemyStat·Mortal·Combatant·NavMeshAgent·EnemyMotor·EnemySensor·EnemyCombat·EnemyAI·EnemyAttackAction·MeleeAttack과 Collider 연결을 유지한다.
3. `Capsule Visual` 자식을 새 메시로 교체한다. 적 루트는 발밑 기준이고 이동 Collider는 루트에서 관리한다. 모델 애니메이션의 루트 이동과 NavMeshAgent 이동을 동시에 적용하지 않는다.
4. `Create > ProjectU > Enemy > Stat Data`로 능력치 에셋을 만들고 EnemyStat.Data에 연결한다. 최대 HP·이동속도·공격력/공격속도 배율은 이 에셋에서, Agent의 가속도·반경·정지 거리와 Sensor의 시야·거리·눈 위치는 해당 컴포넌트에서 편집한다.
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

- `ProjectU > Setup > Create TempEnemy Demo`: SampleScene을 열어 TempEnemy 프리팹, 능력치 에셋, Enemy Navigation, Temp Enemies 그룹과 5기 배치를 구성한다. 능력치 에셋이 이미 있으면 해당 값을 유지해 연결한다. 해당 데모 프리팹·그룹을 다시 구성하므로 개별 적을 편집할 때 반복 실행하지 않는다.
- 위 데모 메뉴는 과거 배치를 재현하는 도구다. 현재 스테이지 준비에는 `ProjectU > Setup > Prepare Stage Arena`를 사용한다. 저장된 씬을 열어 크기를 절대값으로 설정하므로 반복 실행해도 1.25배씩 누적되지 않는다. 배치·프리팹 매핑을 사용자 지정한 뒤에는 초기 설정 도구를 다시 실행하지 않는다.
- `ProjectU > Validation > Validate Enemy Spawners`: 빈 씬·4개 스포너·확장 NavMesh와 실제 생성 API를 검증한다. 혼합 종류는 검증 중 만든 별도 크기의 임시 개체를 사용한다.
- `ProjectU > Validation > Validate Enemies`: Edit 모드에서 빈 스테이지·스포너 수·프리팹 능력치 연결을 확인하고, Play 모드에서만 전투 검증용 적 5기를 생성한다. 기존 AI·공격 검증은 유지하며 씬·프리팹을 저장하지 않는다.
- `ProjectU > Validation > Capture Enemy Scene Preview`: 그래픽 장치가 있는 에디터에서 씬 전체와 캡슐 근접 이미지를 렌더링한다. 씬은 저장하지 않는다.
- 기존 플레이어 검증은 `EnemyValidationIsolation.DisableDemoEnemies()`로 데모 적만 실행 중 비활성화한다. 실제 적 상호작용은 EnemyValidation이 별도로 검증한다.

### 2026-11-05 딕셔너리 이관 직접 검증

Unity **6000.3.25f1**에서 StageDataDictionaryValidation **42개**·EnemySpawnerValidation **90개**·EnemyValidation **95개**, 총 **227개**를 종료 코드 0으로 통과했다. 설치 당시 Edit 모드 7개나 아래 이전 날짜 검증과 별도다. 실제 StageData 저장·재로드, Inspector 값 변경·중복 거절·수정 후 복구, 데이터 오류·복사·반환 순서와 실제 다중 그룹 생성·겹침 방지·실패·기존 적 전투를 확인했다.

Windows 64비트 플레이어 스크립트도 별도 컴파일했다. 로그는 `Logs/DictionaryMigration_20261105/`의 `StageDataDictionaryValidation.log`, `EnemySpawnerValidation.log`, `EnemyValidation.log`, `PlayerScriptCompilation.log`다. 새 API·Editor 전용 중복 검사·이관 값은 [스폰 규칙 9절](스폰%20규칙.md#9-딕셔너리-이관과-직접-검증-2026-11-05)에 정리했다. 실제 Inspector 화면 렌더링·키/마우스 수동 플레이·전체 실행 파일 빌드는 이번에 수행하지 않았다.

### 2026-11-03 스테이지 준비 직접 검증

Unity **6000.3.25f1**에서 EnemySpawnerValidation **52개**, EnemyValidation **95개**, 총 **147개**를 모두 종료 코드 0으로 통과했다. 로그는 `Logs/StagePreparation_20261103/`에 있다.

| 검증 | 이번 확인 결과 | 로그 |
|---|---|---|
| StageSetup | 50×50m 아레나·NavMesh 재베이크·적 0기·스포너 4개 저장 | `StageSetup.log` |
| EnemySpawnerValidation | 씬 크기·배치·외곽 NavMesh·자동 생성 없음, 4개 스포너의 생성/HP/접지/Player 연결, 같은 프레임 반복·공유 영역, 미등록/잘못된 요청·비활성화, 공간 부족 전체 실패, 벽·플레이어 회피, 크기가 다른 혼합 종류와 반환 순서 | `EnemySpawnerValidation.Final.log` |
| EnemyValidation | 검증 중에만 생성하는 5기 전투와 기존 감지·추격·공격·사망·공통 액션·능력치 95개 | `EnemyValidation.log` |
| .NET 빌드 | 런타임·에디터 코드 경고 0·오류 0 | `DotnetBuild.Final.log` |

스포너 최초 52개 통과 후 Unity가 저장하며 바꾼 플레이어 액션의 필드 순서를 원본대로 복원하고 최종 52개를 재확인했다. 같은 검사를 합계에 중복 산입하지 않았다. 기존 씬 블록 112개를 보존하고 기존 블록 변경은 바닥 이름·크기, 벽 4개의 Transform, SceneRoots의 그룹 참조 총 7개로 제한했다. 기존 적 그룹/인스턴스 블록 12개를 제거하고 스포너 그룹/4개 개체 블록 14개를 추가했다. 기존 프리팹·공격 데이터·애니메이션·meta 변경 0, 전체 GUID 중복 0과 `git diff --check`도 확인했다.

초기 Unity 라이선스 초기화 대기(`StageSetup.Sandbox.log`)와 .NET SDK 경로 접근 제한(`DotnetBuild.log`)은 일반 권한으로 다시 실행한 최종 성공 결과와 구분한다. 스포너 최초 실행의 종료 중 네트워크 `Curl error 42`는 52개 통과 이후 메시지이며 기능 검사 실패는 없었다. 이번에는 실제 키·마우스 수동 플레이, 이미지 렌더링과 다른 독립 검증은 수행하지 않았다. GameManager·웨이브 생성 명령·풀·생존 수 추적은 후속 작업이다.

### 2026-11-02 능력치 ScriptableObject 직접 검증

Unity **6000.3.25f1**에서 다음 네 검증이 모두 종료 코드 0으로 통과했다. 정량 검사 **220개**와 Mortal 시나리오 검증 결과이며 아래 2026-11-01 전체 회귀 기록과 구분한다.

| 검증 | 결과 | 로그 (`Logs/EnemyStats_20261102/`) |
|---|---|---|
| EnemyValidation | 95개 + Edit 모드 프리팹 5기·데이터 참조 | `EnemyValidation.log` |
| MortalValidation | 기존 피해·사망 + 초기화·재초기화 거절·최소 HP·이벤트·무적 유지 | `MortalValidation.log` |
| AttackValidation | 75개 | `AttackValidation.log` |
| PlayerAttackValidation | 50개 | `PlayerAttackValidation.Final.log` |

적 검증에는 저장된 기본 능력치, 잘못된 숫자 보정, HP 240·속도 2·공격/공격속도 2의 별도 데이터 적용, 같은 에셋을 공유하는 두 적의 HP 독립, 재활성화 시 현재 HP 유지, 생성 후 에셋 변경과 이후 생성 개체의 차이, 실제 NavMesh 이동속도 제한, 기본 피해 15 × 캐릭터 2 × 액션 1.5 = 45 피해, 선딜/후딜/쿨타임 0.25/0.4/0.6초, 실제 사망 이벤트·제거를 추가했다. 원본 데이터는 검증 중 변경하지 않았다.

최초 PlayerAttackValidation에서는 0.05초 후딜을 확인한 다음 입력 프레임까지 시간이 지나 장전이 정상 허용될 수 있는 검사 타이밍 문제가 발생했다. 검증에 고정 60Hz 게임 시간을 사용하고 입력 전달까지 후딜이 남는 시점에 검사하도록 보완한 뒤 50개를 통과했다. 실패 로그 `PlayerAttackValidation.log`와 최종 로그를 구분한다. 플레이어 런타임 코드의 행동 제한은 변경하지 않았다.

최종 .NET 런타임·에디터 빌드는 경고 0·오류 0(`DotnetBuild.Final.log`)이다. 씬·NavMesh, 기존 공격 데이터·투사체·애니메이션, 기존 meta 변경 0과 전체 meta GUID 중복 0, `git diff --check`를 확인했다. `Results.json`은 최종 종료 코드, `Results.Initial.json`은 중간 실행 결과, `Integrity.json`은 에셋 무결성 결과다. 초기 샌드박스 Unity 라이선스 접근 실패(`EnemyValidation.Initial.log`)와 .NET SDK 접근/복원 메타데이터 부족은 최종 성공 결과와 구분한다.

이번에는 합성 입력 자동 플레이를 수행했으며 실제 키·마우스 수동 플레이, 이미지 렌더링, 다른 독립 검증은 재실행하지 않았다. 런타임 능력치 변경·웨이브 배율·적 풀 재생성, 아래 적 연출·반복 전투 흐름은 후속 작업이다.

### 2026-11-01 적·공통 액션 검증 기록

**이전 직접 검증: 2026-11-01, Unity 6000.3.25f1.** 다음 13종이 모두 종료 코드 0으로 통과했다. 정량 검사 합계는 **582개**이며 애니메이션·스태미나·Mortal·Projectile의 별도 시나리오 통과도 함께 확인했다. 아래 표는 능력치 데이터 추가 전 결과다.

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

- 런타임 능력치 변경·버프/감속·웨이브 배율, 풀 재생성 시 상태 초기화.
- 적 메시·피격·공격 예고·공격·사망 애니메이션과 사운드.
- Dead 연출 → 일정 시간 후 비활성화 → 스테이지 종료 시 일괄 Destroy.
- Gunner의 거리 유지, 돌진형 적, 보스 패턴과 단계 전환.
- StageData를 실행할 스테이지 진행 담당, 웨이브 생성 API 호출·적 생존 수·클리어 판정·강화 선택·결과·재시작.
- 플레이어의 사망 시 이동 중단·연출·복귀와 HP HUD.
- 최종 밸런스, 다수 적의 경로·회피·감지 및 할당 비용 최적화.

## 13. 스포너 매니저와 스테이지 데이터 (2026-11-05)

Serialized Dictionary의 제작자 공식 GitHub UPM 배포본을 설치한 뒤 데이터·API 이관까지 완료했다. EnemySpawnGroup은 SerializedDictionary<EnemyType, int>를 저장하고 Enemies를 IReadOnlyDictionary로 노출한다. 매니저의 혼합 TrySpawn도 IReadOnlyDictionary를 받으며 EnemySpawnRequest는 제거했다. 패키지 설치 기록은 [스폰 규칙 8절](스폰%20규칙.md#8-serialized-dictionary-설치-2026-11-05), 이번 이관·검증은 9절을 참고한다.

SampleScene의 아레나는 50×50m, 벽 중심은 X/Z ±25m이며 기존 NavMesh를 유지한다. Enemy Spawners 아래 NW·NE·SW·SE는 `(±16,0,±16)`에 배치돼 있다. 이번에는 Enemy Spawn Manager 오브젝트를 추가하고 개별 스포너의 프리팹 등록·Player 참조를 한 곳으로 옮겼다.

`EnemySpawnManager.TrySpawnWave(WaveData, out EnemyAI[])`는 요청마다 스포너를 셔플하고 그룹마다 서로 다른 스포너를 배정한다. 공간 부족 시 다른 배정을 시도하고, 모든 그룹이 공유하는 예약 목록에서 겹침을 검사한다. 전체 위치를 확보해야 같은 호출·같은 프레임에 생성하며, 계획 실패 시 부분 생성 없이 false·빈 배열을 반환한다. 단일 타입·혼합 타입 한 그룹의 편의 TrySpawn도 매니저에 있다.

데이터 계층은 `StageData → WaveData[] → EnemySpawnGroup[1~4] → SerializedDictionary<EnemyType, int>`다. 같은 그룹 안의 직렬화된 중복 타입·0 이하 수량·빈 그룹·5개 이상 그룹 등을 거절한다. 그룹 내 반환 순서는 EnemyType 값 오름차순으로 고정한다. StageRunner가 전체 생존 수를 기준값과 비교해 다음 웨이브를 호출한다. TempStage의 9개 그룹·TempEnemy 4/6/8마리·전이 기준·GUID는 유지하며 현재 SampleScene의 자동 진행에 연결했다.

설정·코드 예제·방향 선택·실패 정책·프리팹 계약은 **[스폰 규칙.md](스폰%20규칙.md)**를 기준으로 한다. `ProjectU > Setup > Connect Stage System`에서 진행 컴포넌트까지 연결하며 StageData Inspector는 검증 오류를 표시한다. `EnemyAI.OnRemoved`는 OnDestroy에서 실제 제거를 알린다. StageRunner가 OnDead와 OnRemoved를 함께 구독해 중복 없이 생존 목록에서 해제하며, 비활성화만으로는 해제하지 않는다. 스테이지 회복·보상·다음 스테이지·승패는 [스테이지 진행 가이드.md](스테이지%20진행%20가이드.md)를 참고한다.

### 이전 확인 결과 (2026-11-03) — 배열 기반 매니저 구현 당시

Unity 6000.3.25f1에 포함된 C# 컴파일러와 프로젝트 참조로 런타임·에디터 전체 코드를 컴파일했고, 순수 관리 코드의 EnemySpawnGroup·WaveData 검증 26개를 실행했다. 이 결과는 Unity Play 모드의 NavMesh·물리·생성 실행 검증이 아니다. 로그는 `Logs/StageSystem_20261103/Compile/`과 `DataValidation.log`다.

EnemySpawnerValidation은 매니저 API로 옮기고 여러 그룹의 배정·셔플·공유 위치 예약·전부 실패·다른 배정 재시도 검사를 추가했다. 당시 Unity 배치 실행은 자동 승인 검토의 사용량 한도 오류로 실행되지 않았다. 이후 2026-11-05에는 딕셔너리 이관 후 Play 모드 검증까지 통과했으며, 현재 결과는 위 11절의 227개 기록이다.
