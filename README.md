# 모바일 Portrait 클라이밍 게임 구현 README

이 문서는 MainScene 구성, 프리펩 계층 구조, 런타임 인스턴스화 방식, 그리고 현재 구현된 주요 시스템의 배치 기준을 정리한다.

게임은 하나의 `MainScene`에서 동작한다. `RunManager`가 런 시작, 런 재시작, 상태 전환, 매니저 초기화 순서를 통제한다. 화면 출력은 `Canvas` 기준으로 처리하며, 플레이 영역의 맵 오브젝트와 고정 HUD를 계층으로 분리한다.

---

## 1. MainScene 하이어라키 구조

권장 하이어라키는 다음과 같다.

```text
MainScene
├─ Main Camera
├─ Canvas
│  ├─ Background
│  ├─ MapRoot
│  │  ├─ GeneratedChunks
│  │  │  ├─ Chunk_0_0
│  │  │  │  ├─ HoldPrefab instance
│  │  │  │  │  ├─ HoldImage
│  │  │  │  │  └─ HoldSuccessCircle
│  │  │  │  └─ ...
│  │  │  ├─ Chunk_0_1
│  │  │  └─ ...
│  │  └─ WaterVisual
│  ├─ GameplayVisualRoot
│  │  ├─ CharacterPrefab instance
│  │  │  └─ CharacterImage
│  │  │     └─ HoldingPoint
│  │  ├─ DragStartCircle
│  │  ├─ DragCurrentCircle
│  │  ├─ HandPointVisual
│  │  └─ TrajectoryPoint instances
│  ├─ ButtonRoot
│  │  └─ HoldButtonPrefab instance
│  │     └─ HoldButtonImage
│  └─ WaterDistanceText
└─ Managers
   ├─ RunManager
   ├─ GameRandomManager
   ├─ WorldChunkManager
   ├─ PlayerClimbManager
   └─ WaterManager
```

`Background`, `WaterDistanceText`, `Managers`는 직접 만들어 두는 것을 권장한다. `MapRoot`, `GameplayVisualRoot`, `ButtonRoot`, `GeneratedChunks`, `WaterVisual`은 비어 있으면 런 시작 시 자동으로 준비된다. 직접 만들어 연결해도 된다.

---

## 2. Canvas 설정

`Canvas`는 플레이 화면 전체를 담당한다.

```text
Canvas
- Render Mode: Screen Space - Overlay
- Canvas Scaler: Scale With Screen Size
- Reference Resolution: 1080 x 1920
- Match Width Or Height: 1
```

`RunManager`는 시작 시 Canvas 설정을 기준 해상도에 맞춰 정리한다. 기준 해상도는 1080 x 1920이다. 이 게임의 좌표와 거리 계산은 이 Canvas 기준 좌표계 위에서 처리된다.

`Main Camera`는 2D 기준 카메라로 사용한다.

```text
Main Camera
- Projection: Orthographic
- Orthographic Size: 10
```

카메라는 씬 기준 설정값을 유지하되, 실제 게임 오브젝트 배치와 이동은 Canvas 좌표계를 기준으로 처리한다.

---

## 3. Canvas 루트 역할

### 3.1 MapRoot

`MapRoot`는 맵에 속하는 요소의 기준 루트다.

여기에 들어가는 요소는 다음과 같다.

```text
GeneratedChunks
WaterVisual
```

플레이어가 이동하면 내부 논리 좌표인 `playerWorldPosition`이 갱신된다. 화면에서는 `MapRoot`가 그 반대 방향으로 이동한다.

```text
MapRoot.anchoredPosition = -playerWorldPosition
```

이 구조 때문에 플레이어는 화면 중앙 근처에 고정된 것처럼 보이고, 실제로는 맵이 반대로 움직이는 형태가 된다.

### 3.2 GeneratedChunks

`GeneratedChunks`는 `MapRoot` 아래에 생성된다. 모든 청크 오브젝트는 이 루트의 자식으로 생성된다.

청크는 다음 이름으로 생성된다.

```text
Chunk_x_y
```

예시:

```text
Chunk_0_0
Chunk_1_0
Chunk_-1_2
```

각 청크의 크기는 현재 화면 크기와 같다.

```text
chunkWidth = viewportWidth
chunkHeight = viewportHeight
```

청크의 `anchoredPosition`은 청크 좌표와 화면 크기를 곱해서 결정된다.

```text
chunkAnchoredPosition = (chunkX * viewportWidth, chunkY * viewportHeight)
```

### 3.3 GameplayVisualRoot

`GameplayVisualRoot`는 플레이어와 조작 보조 시각 요소를 담는다.

여기에 들어가는 요소는 다음과 같다.

```text
CharacterPrefab instance
DragStartCircle
DragCurrentCircle
HandPointVisual
TrajectoryPoint instances
```

캐릭터는 이 루트 아래에 생성되며, `HoldingPoint`가 항상 화면 중앙에 오도록 캐릭터 루트 위치가 보정된다.

드래그 시작 원, 현재 드래그 위치 원, 포물선 프리뷰 점, 손 위치 시각점도 이 루트 아래에 생성된다.

### 3.4 ButtonRoot

`ButtonRoot`는 홀드 시도 버튼을 담는다.

손을 뗀 위치에 `HoldButtonPrefab` 인스턴스가 생성된다. 버튼의 시각 크기는 프리펩에서 조절하고, 실제 입력 판정은 별도의 화면 반경으로 처리한다.

### 3.5 WaterDistanceText

`WaterDistanceText`는 화면 상단에 고정되는 TMP 텍스트다.

표시 내용은 플레이어와 수면 사이의 거리다. 화면 전체 높이를 9미터로 보고, 현재 플레이어와 수면 사이의 Canvas 거리 비율을 미터 단위로 환산한다.

```text
distanceMeters = (playerY - waterY) / viewportHeight * 9
```

표시값은 정수로 반올림하며, 수면이 플레이어보다 위에 있으면 0m로 표시한다.

---

## 4. Managers 구조

### 4.1 RunManager

`RunManager`는 MainScene의 최상위 실행 매니저다.

주요 역할은 다음과 같다.

```text
런 시작
런 재시작
상태 전환
Canvas 루트 준비
Canvas Scaler 설정
Camera 설정
5개 매니저 초기화 순서 통제
매 프레임 Water, Player, World 갱신 호출
게임오버 처리
R 키 입력 시 런 재시작
```

`RunManager`가 사용하는 상태는 다음과 같다.

```text
Holding
Aiming
Flying
HoldCooldown
FallingNoRescue
GameOver
```

Inspector 연결 항목은 다음과 같다.

```text
Scene References
- Main Canvas
- Main Camera
- GameRandomManager
- WorldChunkManager
- PlayerClimbManager
- WaterManager

Canvas Roots
- MapRoot
- GameplayVisualRoot
- ButtonRoot

Reference Canvas
- Reference Resolution = 1080 x 1920
- Configure Canvas Scaler On Start
- Force Screen Space Overlay

Camera
- Orthographic Size = 10
```

`MapRoot`, `GameplayVisualRoot`, `ButtonRoot`는 비워 두면 Canvas 아래에서 이름 기준으로 찾고, 없으면 자동 생성한다.

### 4.2 GameRandomManager

`GameRandomManager`는 런 단위와 청크 단위 랜덤을 담당한다.

주요 역할은 다음과 같다.

```text
런 시작 시 runSeed 생성
runSeed와 chunkX, chunkY를 조합해 chunkSeed 생성
청크별 deterministic random 제공
```

같은 런에서는 같은 청크 좌표가 항상 같은 홀드 배치를 가진다. 다른 런에서는 같은 청크 좌표라도 다른 홀드 배치가 나온다.

### 4.3 WorldChunkManager

`WorldChunkManager`는 청크와 홀드 생성을 담당한다.

주요 역할은 다음과 같다.

```text
현재 플레이어 위치 기준으로 필요한 청크 범위 계산
수면 기준으로 필요 없는 청크 제거
청크 생성
홀드 생성
각 홀드의 성공 기준 원 생성
활성 홀드 목록 관리
MapRoot 오프셋 갱신
```

Inspector 연결 항목은 다음과 같다.

```text
Prefab
- Hold Prefab

Chunk Range
- Horizontal Chunk Radius
- Vertical Screen Ahead

Hold Placement
- Min Hold Count
- Max Hold Count
- Min Hold Distance Screen Width Ratio
- Edge Margin Screen Width Ratio
- Max Placement Attempts Per Hold

Hold Visual
- Hold Visual Size Multiplier

Hold Success Circle
- Hold Success Circle Radius
- Hold Success Circle Color
```

청크 생성 기준은 다음과 같다.

```text
가로: 현재 청크 기준 좌우 horizontalChunkRadius만큼 유지
세로 위쪽: 현재 화면 위 verticalScreenAhead 화면 높이까지 유지
세로 아래쪽: 수면 위에 걸쳐 있는 청크 유지
```

홀드는 청크마다 8~12개 생성된다. 시작 청크 `(0, 0)`에는 중앙 시작 홀드가 포함된다.

각 홀드에는 `HoldSuccessCircle`이 자동 생성된다. 이 원은 홀드 이미지 위에 표시되며, 캐릭터 손 위치 중앙점이 이 원 안에 들어온 상태에서 홀드 버튼을 누르면 홀드 성공으로 판정한다.

여러 홀드의 성공 기준 원에 동시에 들어오면, 손 위치 중앙점에서 가장 가까운 홀드를 선택한다.

### 4.4 PlayerClimbManager

`PlayerClimbManager`는 조작, 발사, 비행, 홀드 시도를 담당한다.

주요 역할은 다음과 같다.

```text
캐릭터 프리펩 생성
HoldingPoint 화면 중앙 정렬
터치/마우스 입력 처리
드래그 시작점 표시
현재 드래그 위치 표시
발사 방향과 발사 속도 계산
포물선 프리뷰 표시
캐릭터 비행 처리
홀드 버튼 생성
홀드 버튼 입력 판정
홀드 성공/실패 처리
손 위치 시각점 표시
```

Inspector 연결 항목은 다음과 같다.

```text
Prefabs
- Character Prefab
- Hold Button Prefab

Launch
- Max Drag Distance Screen Width Ratio
- Max Launch Speed
- Gravity
- Flight Motion Speed Multiplier
- Min Launch Pull Pixels
- Cancel Dead Zone Pixels

Character Visual
- Character Visual Size Multiplier

Hold
- Hand Point Visual Size
- Max Hold Attempts
- Hold Attempt Cooldown
- Hold Button Tap Radius Pixels
- Hand Point Visual Color

Trajectory Preview
- Trajectory Point Count
- Trajectory Time Step
- Trajectory Point Size
- Trajectory Preview Strength Multiplier
- Trajectory Point Color

Generated Visual Sizes
- Drag Start Circle Size
- Drag Current Circle Size
```

조작 기본식은 다음과 같다.

```text
발사 방향 = 터치 시작점 - 현재 드래그 위치
드래그 거리 = 발사 파워
손을 떼면 launchVelocity 부여
```

드래그 중 현재 드래그 위치는 최대 드래그 거리 안으로 제한된다.

```text
maxDragDistance = viewportWidth * Max Drag Distance Screen Width Ratio
```

손을 떼면 실제 손을 뗀 화면 위치에 홀드 버튼이 생성된다.

발사 속도는 드래그 거리 비율과 `Max Launch Speed`로 결정된다.

```text
pullRatio = dragDistance / maxDragDistance
launchSpeed = MaxLaunchSpeed * pullRatio
launchVelocity = launchDirection * launchSpeed
```

비행은 중력 기반 포물선 운동으로 처리된다.

```text
velocity.y -= gravity * deltaTime
playerWorldPosition += velocity * deltaTime
```

`Flight Motion Speed Multiplier`는 실제 비행 시간 진행 속도만 조정한다. 포물선 프리뷰에는 영향을 주지 않는다.

`Trajectory Preview Strength Multiplier`는 프리뷰 표시 강도만 조정한다. 실제 캐릭터 이동에는 영향을 주지 않는다.

### 4.5 WaterManager

`WaterManager`는 수면 상승과 수면 거리 HUD를 담당한다.

주요 역할은 다음과 같다.

```text
수면 위치 초기화
시간에 따른 수면 상승
수면 상승 속도 증가
WaterVisual 생성 및 위치 갱신
플레이어 잠김 여부 검사
수면 거리 TMP 텍스트 갱신
```

Inspector 연결 항목은 다음과 같다.

```text
Water Motion
- Base Water Speed
- Speed Growth Interval Seconds
- Speed Growth Multiplier
- Initial Offset Below Screen Bottom

Visual
- Water Visual
- Generated Water Color

Distance Text
- Water Distance Text
- Distance Text Fixed Root
- Force Distance Text Screen Fixed
- Distance Text Anchor
- Distance Text Pivot
- Distance Text Anchored Position
- Meters Per Screen Height
- Distance Text Prefix
- Distance Text Suffix
```

수면 속도는 다음 식을 사용한다.

```text
waterSpeed = baseWaterSpeed * pow(speedGrowthMultiplier, elapsedTime / speedGrowthIntervalSeconds)
```

수면의 위치가 플레이어 위치 이상으로 올라오면 게임오버 조건을 만족한다.

```text
playerWorldPosition.y <= waterSurfaceY
```

---

## 5. 프리펩 계층 구조

### 5.1 CharacterPrefab

캐릭터 프리펩 구조는 다음과 같다.

```text
CharacterPrefab
└─ CharacterImage
   └─ HoldingPoint
```

필요 컴포넌트는 다음과 같다.

```text
CharacterPrefab
- RectTransform

CharacterImage
- RectTransform
- Image

HoldingPoint
- RectTransform
```

`CharacterImage`에는 캐릭터 PNG 스프라이트를 연결한다. `HoldingPoint`는 캐릭터 손 위치 기준점이다.

런타임에서는 `HoldingPoint`가 화면 중앙에 오도록 캐릭터 루트의 위치를 보정한다. 캐릭터 이미지 자체의 위치와 크기는 프리펩에서 조정한다.

`Character Visual Size Multiplier`를 사용하면 `CharacterImage`의 표시 크기를 런타임에서 추가 조정할 수 있다.

### 5.2 HoldPrefab

홀드 프리펩 구조는 다음과 같다.

```text
HoldPrefab
└─ HoldImage
```

필요 컴포넌트는 다음과 같다.

```text
HoldPrefab
- RectTransform

HoldImage
- RectTransform
- Image
```

`HoldImage`에는 실제 구조물 홀드 PNG 스프라이트를 연결한다.

런타임에서 각 홀드 인스턴스에는 `HoldSuccessCircle`이 자동으로 추가된다.

```text
HoldPrefab instance
├─ HoldImage
└─ HoldSuccessCircle
```

`HoldSuccessCircle`은 홀드 성공 기준 영역을 표시한다. 원의 중심은 `HoldImage`의 위치와 일치한다. 이 원의 반지름과 색상은 `WorldChunkManager`에서 조정한다.

`Hold Visual Size Multiplier`를 사용하면 모든 `HoldImage`의 표시 크기를 런타임에서 추가 조정할 수 있다.

### 5.3 HoldButtonPrefab

홀드 버튼 프리펩 구조는 다음과 같다.

```text
HoldButtonPrefab
└─ HoldButtonImage
```

필요 컴포넌트는 다음과 같다.

```text
HoldButtonPrefab
- RectTransform

HoldButtonImage
- RectTransform
- Image
```

`HoldButtonImage`에는 홀드 버튼 PNG 스프라이트를 연결한다.

홀드 버튼은 드랍 지점에 생성된다. 버튼의 시각 크기는 프리펩에서 조정한다. 입력 판정은 `Hold Button Tap Radius Pixels`로 처리한다.

---

## 6. 런타임 인스턴스화 흐름

### 6.1 런 시작

런 시작 시 흐름은 다음과 같다.

```text
1. RunManager가 Main Canvas, Main Camera, 각 매니저 참조를 확인한다.
2. Canvas를 기준 해상도 1080 x 1920에 맞춘다.
3. MapRoot, GameplayVisualRoot, ButtonRoot를 준비한다.
4. GameRandomManager가 새 runSeed를 만든다.
5. WorldChunkManager가 GeneratedChunks를 준비하고 기존 청크를 정리한다.
6. PlayerClimbManager가 캐릭터와 조작 시각 요소를 생성한다.
7. WaterManager가 수면과 거리 텍스트를 준비한다.
8. 상태를 Holding으로 설정한다.
9. 시작 위치 기준 주변 청크를 생성한다.
```

### 6.2 캐릭터 생성

`PlayerClimbManager`는 `CharacterPrefab`을 `GameplayVisualRoot` 아래에 생성한다.

```text
GameplayVisualRoot
└─ CharacterPrefab instance
   └─ CharacterImage
      └─ HoldingPoint
```

생성 후 `CharacterImage`와 `HoldingPoint`를 이름으로 찾는다. 이후 매 프레임 `HoldingPoint`가 화면 중앙에 오도록 캐릭터 루트의 위치를 보정한다.

### 6.3 청크 생성

`WorldChunkManager`는 `GeneratedChunks` 아래에 청크를 생성한다.

```text
MapRoot
└─ GeneratedChunks
   └─ Chunk_x_y
```

청크 생성 위치는 청크 좌표와 화면 크기로 결정된다.

```text
chunkPosition.x = chunkX * viewportWidth
chunkPosition.y = chunkY * viewportHeight
```

청크는 현재 플레이어 위치, 화면 크기, 수면 위치를 기준으로 필요한 범위만 유지한다.

### 6.4 홀드 생성

각 청크 안에는 `HoldPrefab` 인스턴스가 생성된다.

```text
Chunk_x_y
└─ HoldPrefab instance
   ├─ HoldImage
   └─ HoldSuccessCircle
```

홀드 배치는 청크 내부 로컬 좌표로 결정된다. 시작 청크에는 중앙 시작 홀드가 포함된다.

각 홀드는 다음 조건을 만족하도록 배치된다.

```text
홀드끼리 최소 거리 유지
청크 테두리 여백 유지
완전 겹침 방지
청크별 최대 배치 시도 횟수 적용
```

### 6.5 수면 생성

`WaterManager`는 `MapRoot` 아래에 수면 표시 오브젝트를 준비한다.

```text
MapRoot
└─ WaterVisual
```

수면은 플레이어의 월드 위치와 수면의 월드 Y 위치를 기준으로 표시된다.

```text
WaterVisual.anchoredPosition = (playerWorldPosition.x, waterSurfaceY)
```

수면은 화면보다 넓게 생성되어 좌우 이동 중에도 화면을 덮는다.

### 6.6 홀드 버튼 생성

손을 떼면 `PlayerClimbManager`가 `HoldButtonPrefab`을 `ButtonRoot` 아래에 생성한다.

```text
ButtonRoot
└─ HoldButtonPrefab instance
   └─ HoldButtonImage
```

홀드 버튼의 위치는 손을 뗀 화면 위치다.

```text
holdButtonPosition = releaseScreenPosition
```

버튼 입력은 버튼 중심과 현재 터치 위치 사이의 거리로 판정한다.

```text
distance(pointerPosition, holdButtonPosition) <= Hold Button Tap Radius Pixels
```

### 6.7 조작 시각 요소 생성

`PlayerClimbManager`는 조작 시각 요소를 `GameplayVisualRoot` 아래에 생성한다.

```text
GameplayVisualRoot
├─ DragStartCircle
├─ DragCurrentCircle
├─ HandPointVisual
└─ TrajectoryPoint instances
```

`DragStartCircle`은 터치 시작점을 표시한다.

`DragCurrentCircle`은 최대 드래그 거리 안으로 제한된 현재 조준 위치를 표시한다.

`HandPointVisual`은 캐릭터 손 위치 중앙점을 표시한다. 홀드 판정에는 이 중앙점만 사용한다.

`TrajectoryPoint`들은 현재 발사 입력 기준 예상 포물선을 표시한다.

---

## 7. 홀드 판정 구조

홀드 성공은 다음 두 조건이 동시에 만족될 때 발생한다.

```text
1. 플레이어가 홀드 버튼을 누름
2. 캐릭터 손 위치 중앙점이 어느 홀드의 HoldSuccessCircle 안에 있음
```

캐릭터 손 위치는 `HoldingPoint`의 화면 중앙 정렬 기준으로 계산된다. `HandPointVisual`은 이 위치를 시각적으로 보여주는 원이다.

각 홀드는 자신의 `HoldImage` 위에 `HoldSuccessCircle`을 가진다. 이 원의 반지름이 실제 홀드 성공 영역이다.

판정식은 다음과 같다.

```text
distance(handPoint, holdSuccessCircleCenter) <= holdSuccessCircleRadius
```

여러 홀드가 조건을 만족하면 가장 가까운 홀드를 선택한다.

홀드 성공 시 흐름은 다음과 같다.

```text
1. 포물선 이동 정지
2. 선택된 홀드 중심이 캐릭터 손 위치에 맞도록 위치 보정
3. 홀드 버튼 제거
4. 상태를 Holding으로 전환
```

홀드 실패 시 흐름은 다음과 같다.

```text
1. 남은 홀드 시도 횟수 감소
2. 남은 횟수가 있으면 HoldCooldown으로 전환
3. 쿨타임 동안 비행은 계속 진행
4. 쿨타임 후 다시 Flying으로 전환
5. 횟수를 모두 사용하면 FallingNoRescue로 전환
```

---

## 8. 입력과 발사 구조

화면 어디에서든 Press Down하면 드래그가 시작된다.

드래그 방향과 발사 방향은 반대다.

```text
launchDirection = pressScreenPosition - currentDragScreenPosition
```

드래그 거리는 최대 드래그 거리 안으로 제한된다.

```text
maxDragDistance = viewportWidth * maxDragDistanceScreenWidthRatio
```

드래그 중 현재 터치가 시작점보다 위로 올라가면 조준을 취소한다. 작은 흔들림은 `Cancel Dead Zone Pixels`로 흡수한다.

손을 떼면 발사한다. 너무 짧게 당긴 경우에는 발사하지 않는다.

발사 속도는 다음 기준으로 계산한다.

```text
pullRatio = dragDistance / maxDragDistance
launchSpeed = maxLaunchSpeed * pullRatio
launchVelocity = launchDirection.normalized * launchSpeed
```

비행 중에는 중력이 적용된다.

```text
velocity.y -= gravity * deltaTime
playerWorldPosition += velocity * deltaTime
```

`Flight Motion Speed Multiplier`는 이 비행 진행 시간에만 영향을 준다. 값이 커지면 같은 물리 궤적 진행이 더 빨라지고, 값이 작아지면 더 느려진다.

---

## 9. 포물선 프리뷰 구조

드래그 중에는 현재 발사 입력을 기준으로 포물선 프리뷰가 표시된다.

포물선 프리뷰는 `TrajectoryPoint` 인스턴스들로 구성된다.

```text
GameplayVisualRoot
└─ TrajectoryPoint instances
```

프리뷰 길이는 다음 두 값으로 조정한다.

```text
Trajectory Point Count
Trajectory Time Step
```

`Trajectory Point Count`가 커지면 표시 점 수가 늘어난다.

`Trajectory Time Step`이 커지면 점 사이 시간 간격이 늘어나 더 먼 시점까지 표시한다.

`Trajectory Preview Strength Multiplier`는 프리뷰 표시 강도를 조정한다. 실제 캐릭터 이동에는 영향을 주지 않는다.

---

## 10. 수면 거리 HUD 구조

수면 거리 텍스트는 화면 상단에 고정되는 HUD다.

```text
Canvas
└─ WaterDistanceText
```

이 오브젝트는 `TextMeshProUGUI` 컴포넌트를 가진다.

`WaterManager`의 `Water Distance Text`에 연결한다.

기본 표시 위치는 다음 기준이다.

```text
Anchor = (0.5, 1)
Pivot = (0.5, 1)
Anchored Position = (0, -48)
```

거리 계산은 다음 식을 사용한다.

```text
distanceMeters = max(0, playerY - waterY) / viewportHeight * metersPerScreenHeight
```

기본적으로 `metersPerScreenHeight`는 9다. 즉 화면 전체 높이를 9미터로 본다.

표시는 정수 단위다.

```text
예: 7m
```

---

## 11. 파일 구성

현재 구현 파일은 다음 5개다.

```text
RunManager.cs
GameRandomManager.cs
WorldChunkManager.cs
PlayerClimbManager.cs
WaterManager.cs
```

권장 위치는 다음과 같다.

```text
Assets/Scripts/ClimbGameManagers/
```

각 스크립트는 MainScene의 `Managers` 아래 빈 오브젝트에 하나씩 붙인다.

```text
Managers
├─ RunManager
├─ GameRandomManager
├─ WorldChunkManager
├─ PlayerClimbManager
└─ WaterManager
```

---

## 12. Inspector 연결 체크리스트

### RunManager

```text
Main Canvas = Canvas
Main Camera = Main Camera
Random Manager = GameRandomManager
World Chunk Manager = WorldChunkManager
Player Climb Manager = PlayerClimbManager
Water Manager = WaterManager
Map Root = MapRoot
Gameplay Visual Root = GameplayVisualRoot
Button Root = ButtonRoot
Reference Resolution = 1080 x 1920
Orthographic Size = 10
```

### WorldChunkManager

```text
Hold Prefab = HoldPrefab
Horizontal Chunk Radius = 3
Vertical Screen Ahead = 3
Min Hold Count = 8
Max Hold Count = 12
Min Hold Distance Screen Width Ratio = 1 / 8
Edge Margin Screen Width Ratio = 1 / 32
Hold Visual Size Multiplier = 1
Hold Success Circle Radius = 원하는 기준 반경
Hold Success Circle Color = 원하는 표시 색상
```

### PlayerClimbManager

```text
Character Prefab = CharacterPrefab
Hold Button Prefab = HoldButtonPrefab
Max Drag Distance Screen Width Ratio = 1 / 6
Max Launch Speed = 발사 힘 기준값
Gravity = 중력 기준값
Flight Motion Speed Multiplier = 실제 비행 진행 속도 배율
Character Visual Size Multiplier = 1
Hand Point Visual Size = 원하는 표시 크기
Max Hold Attempts = 3
Hold Attempt Cooldown = 0.5
Hold Button Tap Radius Pixels = 원하는 버튼 입력 반경
Trajectory Point Count = 원하는 프리뷰 점 개수
Trajectory Time Step = 원하는 프리뷰 시간 간격
Trajectory Preview Strength Multiplier = 원하는 프리뷰 표시 강도
```

### WaterManager

```text
Base Water Speed = 초기 수면 상승 속도
Speed Growth Interval Seconds = 10
Speed Growth Multiplier = 1.05
Initial Offset Below Screen Bottom = 시작 시 수면 하단 오프셋
Water Distance Text = WaterDistanceText의 TMP_Text
Meters Per Screen Height = 9
Distance Text Suffix = m
```

---

## 13. 기본 플레이 흐름

```text
1. 런 시작 시 시작 청크와 주변 청크가 생성된다.
2. 플레이어는 화면 중앙의 시작 홀드를 잡은 상태에서 시작한다.
3. 화면을 누르고 아래 방향으로 드래그한다.
4. 드래그 중 시작점, 현재 조준점, 포물선 프리뷰가 표시된다.
5. 손을 떼면 캐릭터가 포물선 운동을 시작한다.
6. 손을 뗀 화면 위치에 홀드 버튼이 생성된다.
7. 캐릭터 손 위치 중앙점이 홀드의 성공 기준 원 안에 들어왔을 때 홀드 버튼을 누른다.
8. 성공하면 해당 홀드를 잡고 다시 Holding 상태가 된다.
9. 실패하면 쿨타임 후 다시 시도할 수 있다.
10. 한 번의 발사에서 최대 3회까지 시도할 수 있다.
11. 수면이 플레이어 위치에 도달하면 게임오버가 된다.
12. R 키를 누르면 현재 런이 즉시 다시 시작된다.
```
