// 플레이어 입력, 발사, 포물선 이동, 홀드 시도를 관리한다.
// 캐릭터 손 기준점을 월드 원점에 정렬한다.
// 드래그 거리를 월드 발사 속도로 변환한다.
// 포물선 preview와 손 위치 시각점을 SpriteRenderer로 표시한다.
// 홀드 버튼은 screen-space UI로 생성한다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class PlayerClimbManager : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("월드 원점에 정렬할 캐릭터 프리펩.")]
    [SerializeField]
    private GameObject characterPrefab;

    [Tooltip("드랍 지점에 생성할 홀드 시도 버튼 UI 프리펩.")]
    [SerializeField]
    private GameObject holdButtonPrefab;

    [Header("Launch")]
    [Tooltip("화면 가로 길이 대비 최대 드래그 거리 비율.")]
    [SerializeField]
    private float maxDragDistanceScreenWidthRatio = 1f / 6f;

    [Tooltip("풀 드래그 기준 초기 발사 속도. 월드 단위/초 기준.")]
    [SerializeField]
    private float maxLaunchSpeed = 27f;

    [Tooltip("아래 방향 가속도. 월드 단위/초^2 기준.")]
    [SerializeField]
    private float gravity = 40f;

    [Tooltip("실제 비행 시간 진행 배율. 포물선 preview에는 영향을 주지 않는다.")]
    [SerializeField]
    private float flightMotionSpeedMultiplier = 1f;

    [Tooltip("이 값보다 짧게 당기고 놓으면 발사하지 않는다. 화면 픽셀 기준.")]
    [SerializeField]
    private float minLaunchPullPixels = 24f;

    [Tooltip("터치 시작점보다 위로 이 픽셀 이상 올라가면 조준을 취소한다.")]
    [SerializeField]
    private float cancelDeadZonePixels = 24f;

    [Header("Character Visual")]
    [Tooltip("CharacterImage의 월드 표시 크기 배율.")]
    [SerializeField]
    private float characterVisualSizeMultiplier = 1f;

    [Header("Hold")]
    [Tooltip("캐릭터 손 위치를 표시하는 원의 월드 지름. 판정에는 중앙점만 사용한다.")]
    [SerializeField]
    private float handPointVisualSize = 0.25f;

    [Tooltip("한 번의 발사에서 허용되는 홀드 시도 횟수.")]
    [SerializeField]
    private int maxHoldAttempts = 3;

    [Tooltip("홀드 실패 후 다음 홀드 시도까지의 대기 시간.")]
    [SerializeField]
    private float holdAttemptCooldown = 0.5f;

    [Tooltip("홀드 버튼 입력을 인정하는 화면 픽셀 반경. 시각 크기와 별개.")]
    [SerializeField]
    private float holdButtonTapRadiusPixels = 96f;

    [Tooltip("캐릭터 손 위치 시각 원의 표시 색상.")]
    [SerializeField]
    private Color handPointVisualColor = new Color(1f, 1f, 1f, 0.18f);

    [Header("Trajectory Preview")]
    [Tooltip("표시할 포물선 점 개수. 값을 키우면 더 긴 시간 범위를 본다.")]
    [SerializeField]
    private int trajectoryPointCount = 28;

    [Tooltip("포물선 점 사이의 시간 간격. 값을 키우면 더 멀리까지 본다.")]
    [SerializeField]
    private float trajectoryTimeStep = 0.055f;

    [Tooltip("포물선 점의 월드 표시 지름.")]
    [SerializeField]
    private float trajectoryPointSize = 0.12f;

    [Tooltip("포물선 preview 표시 강도. 실제 캐릭터 운동에는 영향이 없다.")]
    [SerializeField]
    private float trajectoryPreviewStrengthMultiplier = 1f;

    [Tooltip("포물선 점의 표시 색상.")]
    [SerializeField]
    private Color trajectoryPointColor = new Color(1f, 1f, 1f, 0.5f);

    [Header("Generated Visual Sizes")]
    [Tooltip("드래그 시작점 원의 월드 표시 지름.")]
    [SerializeField]
    private float dragStartCircleSize = 0.25f;

    [Tooltip("현재 드래그 위치 원의 월드 표시 지름.")]
    [SerializeField]
    private float dragCurrentCircleSize = 0.35f;

    [Header("Sorting")]
    [SerializeField]
    private int characterSortingOrder = 20;

    [SerializeField]
    private int generatedVisualSortingOrder = 30;

    public Vector2 PlayerWorldPosition { get; private set; }
    public Vector2 Velocity => velocity;

    private RunManager runManager;
    private WorldChunkManager worldChunkManager;
    private WaterManager waterManager;
    private Camera mainCamera;
    private RectTransform canvasRect;
    private Transform gameplayVisualRoot;
    private RectTransform buttonRoot;

    private Transform characterRoot;
    private Transform characterImage;
    private Transform holdingPoint;
    private Transform dragStartCircle;
    private Transform dragCurrentCircle;
    private Transform handPointVisual;
    private RectTransform holdButtonRoot;
    private Image holdButtonImage;
    private Vector2 holdButtonScreenPosition;

    private readonly List<Transform> trajectoryPoints = new List<Transform>();

    private Vector2 velocity;
    private Vector2 pressScreenPosition;
    private Vector2 lastClampedDragScreenPosition;
    private int activeFingerId = NoFinger;
    private int remainingHoldAttempts;
    private float cooldownTimer;
    private bool initialized;
    private float appliedCharacterVisualSizeMultiplier = -1f;
    private float appliedHandPointVisualSize = -1f;

    private const int NoFinger = int.MinValue;
    private const int MouseFinger = -100;

    public void Initialize(
        RunManager runManager,
        WorldChunkManager worldChunkManager,
        WaterManager waterManager,
        Camera mainCamera,
        RectTransform canvasRect,
        Transform gameplayVisualRoot,
        RectTransform buttonRoot
    )
    {
        this.runManager = runManager;
        this.worldChunkManager = worldChunkManager;
        this.waterManager = waterManager;
        this.mainCamera = mainCamera;
        this.canvasRect = canvasRect;
        this.gameplayVisualRoot = gameplayVisualRoot;
        this.buttonRoot = buttonRoot;

        ClearChildren(gameplayVisualRoot);
        ClearChildren(buttonRoot);
        CreateCharacter();
        CreateGeneratedVisuals();
        CreateTrajectoryPoints();

        initialized = true;
    }

    public void ResetForNewRun(Vector2 startWorldPosition)
    {
        PlayerWorldPosition = startWorldPosition;
        velocity = Vector2.zero;
        activeFingerId = NoFinger;
        remainingHoldAttempts = 0;
        cooldownTimer = 0f;
        HideAimingVisuals();
        HideTrajectory();
        DestroyHoldButton();
        RefreshCharacterVisualSizing();
        worldChunkManager.RefreshVisualTuning();
        AlignHoldingPointToWorldCenter();
        RefreshHandPointVisual();
        worldChunkManager.SetMapOffset(PlayerWorldPosition);
    }

    public void ManagedUpdate(float deltaTime)
    {
        if (!initialized || runManager.State == RunManager.RunState.GameOver)
        {
            return;
        }

        AlignHoldingPointToWorldCenter();
        RefreshHandPointVisual();
        worldChunkManager.RefreshVisualTuning();

        switch (runManager.State)
        {
            case RunManager.RunState.Holding:
                HandleHoldingInput();
                break;
            case RunManager.RunState.Aiming:
                HandleAimingInput();
                break;
            case RunManager.RunState.Flying:
                UpdateFlight(deltaTime);
                HandleHoldButtonInput();
                break;
            case RunManager.RunState.HoldCooldown:
                UpdateFlight(deltaTime);
                UpdateHoldCooldown(deltaTime);
                break;
            case RunManager.RunState.FallingNoRescue:
                UpdateFlight(deltaTime);
                break;
        }
    }

    public void OnGameOver()
    {
        HideAimingVisuals();
        HideTrajectory();
        DestroyHoldButton();
        velocity = Vector2.zero;
        activeFingerId = NoFinger;
    }

    private void CreateCharacter()
    {
        if (characterPrefab == null)
        {
            Debug.LogError("PlayerClimbManager requires CharacterPrefab.");
            enabled = false;
            return;
        }

        GameObject characterObject = Instantiate(characterPrefab, gameplayVisualRoot);
        characterRoot = characterObject.transform;
        characterRoot.localPosition = Vector3.zero;
        characterRoot.localRotation = Quaternion.identity;
        characterRoot.localScale = Vector3.one;

        characterImage = FindChildByName(characterRoot, "CharacterImage");
        holdingPoint = FindChildByName(characterRoot, "HoldingPoint");

        if (characterImage == null || holdingPoint == null)
        {
            Debug.LogError("CharacterPrefab requires CharacterImage and HoldingPoint children.");
            enabled = false;
            return;
        }

        SpriteRenderer characterRenderer = characterImage.GetComponent<SpriteRenderer>();
        if (characterRenderer != null)
        {
            characterRenderer.sortingOrder = characterSortingOrder;
        }

        RefreshCharacterVisualSizing();
        AlignHoldingPointToWorldCenter();
    }

    private void CreateGeneratedVisuals()
    {
        dragStartCircle = CreateCircle("DragStartCircle", gameplayVisualRoot, dragStartCircleSize, new Color(1f, 1f, 1f, 0.55f));
        dragCurrentCircle = CreateCircle("DragCurrentCircle", gameplayVisualRoot, dragCurrentCircleSize, new Color(0.2f, 0.55f, 1f, 0.75f));
        handPointVisual = CreateCircle("HandPointVisual", gameplayVisualRoot, handPointVisualSize, handPointVisualColor);
        HideAimingVisuals();
    }

    private void CreateTrajectoryPoints()
    {
        trajectoryPoints.Clear();
        for (int i = 0; i < trajectoryPointCount; i++)
        {
            Transform point = CreateCircle("TrajectoryPoint", gameplayVisualRoot, trajectoryPointSize, trajectoryPointColor);
            point.gameObject.SetActive(false);
            trajectoryPoints.Add(point);
        }
    }

    private void HandleHoldingInput()
    {
        if (TryGetPointerDown(out Vector2 screenPosition, out int fingerId))
        {
            activeFingerId = fingerId;
            pressScreenPosition = screenPosition;
            lastClampedDragScreenPosition = screenPosition;
            runManager.SetState(RunManager.RunState.Aiming);
            ShowAimingVisuals(screenPosition, screenPosition);
        }
    }

    private void HandleAimingInput()
    {
        bool hasPosition = TryGetActivePointerPosition(out Vector2 currentScreenPosition);
        if (!hasPosition)
        {
            CancelAiming();
            return;
        }

        if (currentScreenPosition.y > pressScreenPosition.y + cancelDeadZonePixels)
        {
            CancelAiming();
            return;
        }

        if (currentScreenPosition.y > pressScreenPosition.y)
        {
            currentScreenPosition.y = pressScreenPosition.y;
        }

        Vector2 clampedScreenPosition = ClampDragPosition(pressScreenPosition, currentScreenPosition);
        lastClampedDragScreenPosition = clampedScreenPosition;
        ShowAimingVisuals(pressScreenPosition, clampedScreenPosition);

        Vector2 launchVelocity = CalculateLaunchVelocity(clampedScreenPosition);
        UpdateTrajectory(launchVelocity);

        if (TryGetActivePointerUp(out Vector2 releaseScreenPosition))
        {
            if ((pressScreenPosition - clampedScreenPosition).magnitude < minLaunchPullPixels)
            {
                CancelAiming();
                return;
            }

            Launch(launchVelocity, releaseScreenPosition);
        }
    }

    private void UpdateFlight(float deltaTime)
    {
        float simulationDeltaTime = deltaTime * Mathf.Max(0f, flightMotionSpeedMultiplier);
        velocity.y -= gravity * simulationDeltaTime;
        PlayerWorldPosition += velocity * simulationDeltaTime;
        worldChunkManager.SetMapOffset(PlayerWorldPosition);
    }

    private void HandleHoldButtonInput()
    {
        if (holdButtonRoot == null || remainingHoldAttempts <= 0)
        {
            return;
        }

        if (!TryGetPointerDown(out Vector2 screenPosition, out _))
        {
            return;
        }

        if ((screenPosition - GetHoldButtonScreenPosition()).sqrMagnitude > holdButtonTapRadiusPixels * holdButtonTapRadiusPixels)
        {
            return;
        }

        TryHoldCurrentPosition();
    }

    private void UpdateHoldCooldown(float deltaTime)
    {
        cooldownTimer -= deltaTime;
        if (cooldownTimer > 0f)
        {
            return;
        }

        SetHoldButtonAlpha(1f);
        runManager.SetState(RunManager.RunState.Flying);
    }

    private void TryHoldCurrentPosition()
    {
        bool success = worldChunkManager.TryGetNearestHoldContainingPoint(
            PlayerWorldPosition,
            out Vector2 nearestHoldWorldPosition,
            out _
        );

        if (success)
        {
            PlayerWorldPosition = nearestHoldWorldPosition;
            velocity = Vector2.zero;
            DestroyHoldButton();
            worldChunkManager.SetMapOffset(PlayerWorldPosition);
            runManager.SetState(RunManager.RunState.Holding);
            return;
        }

        remainingHoldAttempts -= 1;
        if (remainingHoldAttempts <= 0)
        {
            DestroyHoldButton();
            runManager.SetState(RunManager.RunState.FallingNoRescue);
            return;
        }

        cooldownTimer = holdAttemptCooldown;
        SetHoldButtonAlpha(0.35f);
        runManager.SetState(RunManager.RunState.HoldCooldown);
    }

    private void Launch(Vector2 launchVelocity, Vector2 buttonScreenPosition)
    {
        velocity = launchVelocity;
        remainingHoldAttempts = maxHoldAttempts;
        cooldownTimer = 0f;
        activeFingerId = NoFinger;
        HideAimingVisuals();
        HideTrajectory();
        CreateHoldButton(buttonScreenPosition);
        runManager.SetState(RunManager.RunState.Flying);
    }

    private void CancelAiming()
    {
        activeFingerId = NoFinger;
        HideAimingVisuals();
        HideTrajectory();
        runManager.SetState(RunManager.RunState.Holding);
    }

    private Vector2 ClampDragPosition(Vector2 pressPosition, Vector2 currentPosition)
    {
        float maxDragDistance = Screen.width * maxDragDistanceScreenWidthRatio;
        Vector2 offset = currentPosition - pressPosition;
        if (offset.magnitude > maxDragDistance)
        {
            offset = offset.normalized * maxDragDistance;
        }

        return pressPosition + offset;
    }

    private Vector2 CalculateLaunchVelocity(Vector2 clampedScreenPosition)
    {
        float maxDragDistance = Screen.width * maxDragDistanceScreenWidthRatio;
        Vector2 aimScreenVector = pressScreenPosition - clampedScreenPosition;
        Vector2 aimWorldVector = runManager.ScreenDeltaToWorldDelta(aimScreenVector);
        float pullRatio = Mathf.Clamp01(aimScreenVector.magnitude / maxDragDistance);
        if (aimWorldVector.sqrMagnitude <= 0.0001f)
        {
            return Vector2.zero;
        }

        float launchSpeed = maxLaunchSpeed * pullRatio;
        return aimWorldVector.normalized * launchSpeed;
    }

    private void ShowAimingVisuals(Vector2 startScreenPosition, Vector2 currentScreenPosition)
    {
        dragStartCircle.gameObject.SetActive(true);
        dragCurrentCircle.gameObject.SetActive(true);
        dragStartCircle.position = ScreenToWorldPointOnGameplayPlane(startScreenPosition);
        dragCurrentCircle.position = ScreenToWorldPointOnGameplayPlane(currentScreenPosition);
    }

    private void HideAimingVisuals()
    {
        if (dragStartCircle != null)
        {
            dragStartCircle.gameObject.SetActive(false);
        }

        if (dragCurrentCircle != null)
        {
            dragCurrentCircle.gameObject.SetActive(false);
        }
    }

    private void UpdateTrajectory(Vector2 launchVelocity)
    {
        for (int i = 0; i < trajectoryPoints.Count; i++)
        {
            float t = (i + 1) * trajectoryTimeStep;
            Vector2 offset = launchVelocity * t + new Vector2(0f, -0.5f * gravity * t * t);
            Transform point = trajectoryPoints[i];
            SpriteRenderer pointRenderer = point.GetComponent<SpriteRenderer>();
            if (pointRenderer != null)
            {
                pointRenderer.color = trajectoryPointColor;
            }

            point.gameObject.SetActive(true);
            point.localPosition = new Vector3(
                offset.x * Mathf.Max(0f, trajectoryPreviewStrengthMultiplier),
                offset.y * Mathf.Max(0f, trajectoryPreviewStrengthMultiplier),
                0f
            );
        }
    }

    private void HideTrajectory()
    {
        for (int i = 0; i < trajectoryPoints.Count; i++)
        {
            trajectoryPoints[i].gameObject.SetActive(false);
        }
    }

    private void CreateHoldButton(Vector2 desiredScreenPosition)
    {
        DestroyHoldButton();

        if (holdButtonPrefab == null)
        {
            Debug.LogError("PlayerClimbManager requires HoldButtonPrefab.");
            return;
        }

        holdButtonScreenPosition = ClampToSafeScreenArea(desiredScreenPosition);
        GameObject buttonObject = Instantiate(holdButtonPrefab, buttonRoot);
        holdButtonRoot = buttonObject.GetComponent<RectTransform>();
        if (holdButtonRoot == null)
        {
            Debug.LogError("HoldButtonPrefab root requires RectTransform.");
            Destroy(buttonObject);
            return;
        }

        PrepareRectTransform(holdButtonRoot);
        holdButtonRoot.anchoredPosition = ScreenToCanvasLocal(holdButtonScreenPosition);
        holdButtonRoot.gameObject.SetActive(true);

        RectTransform holdButtonImageRect = FindChildRectByName(holdButtonRoot, "HoldButtonImage");
        holdButtonImage = holdButtonImageRect != null ? holdButtonImageRect.GetComponent<Image>() : null;
        if (holdButtonImageRect == null)
        {
            Debug.LogError("HoldButtonPrefab requires child RectTransform named HoldButtonImage.");
        }

        SetHoldButtonAlpha(1f);
    }

    private void DestroyHoldButton()
    {
        if (holdButtonRoot != null)
        {
            Destroy(holdButtonRoot.gameObject);
            holdButtonRoot = null;
            holdButtonImage = null;
            holdButtonScreenPosition = Vector2.zero;
        }
    }

    private Vector2 GetHoldButtonScreenPosition()
    {
        if (holdButtonRoot == null)
        {
            return Vector2.negativeInfinity;
        }

        return holdButtonScreenPosition;
    }

    private void SetHoldButtonAlpha(float alpha)
    {
        if (holdButtonImage == null)
        {
            return;
        }

        Color color = holdButtonImage.color;
        color.a = alpha;
        holdButtonImage.color = color;
    }

    private void RefreshCharacterVisualSizing()
    {
        if (characterImage == null)
        {
            return;
        }

        float safeCharacterMultiplier = Mathf.Max(0.01f, characterVisualSizeMultiplier);
        float safeHandPointVisualSize = Mathf.Max(0.01f, handPointVisualSize);

        if (!Mathf.Approximately(appliedCharacterVisualSizeMultiplier, safeCharacterMultiplier))
        {
            characterImage.localScale = Vector3.one * safeCharacterMultiplier;
            appliedCharacterVisualSizeMultiplier = safeCharacterMultiplier;
        }

        if (!Mathf.Approximately(appliedHandPointVisualSize, safeHandPointVisualSize))
        {
            appliedHandPointVisualSize = safeHandPointVisualSize;
        }
    }

    private void AlignHoldingPointToWorldCenter()
    {
        if (characterRoot == null || holdingPoint == null || gameplayVisualRoot == null)
        {
            return;
        }

        Vector3 centerWorld = gameplayVisualRoot.TransformPoint(Vector3.zero);
        Vector3 delta = centerWorld - holdingPoint.position;
        characterRoot.position += delta;
    }

    private void RefreshHandPointVisual()
    {
        if (handPointVisual == null)
        {
            return;
        }

        float safeHandPointVisualSize = Mathf.Max(0.01f, handPointVisualSize);
        handPointVisual.localPosition = Vector3.zero;
        handPointVisual.localRotation = Quaternion.identity;
        handPointVisual.localScale = Vector3.one * safeHandPointVisualSize;

        SpriteRenderer handPointRenderer = handPointVisual.GetComponent<SpriteRenderer>();
        if (handPointRenderer != null)
        {
            handPointRenderer.color = handPointVisualColor;
        }

        handPointVisual.gameObject.SetActive(true);
    }

    private Vector2 ClampToSafeScreenArea(Vector2 screenPosition)
    {
        float margin = Mathf.Max(0f, holdButtonTapRadiusPixels);
        return new Vector2(
            Mathf.Clamp(screenPosition.x, margin, Mathf.Max(margin, Screen.width - margin)),
            Mathf.Clamp(screenPosition.y, margin, Mathf.Max(margin, Screen.height - margin))
        );
    }

    private Vector3 ScreenToWorldPointOnGameplayPlane(Vector2 screenPosition)
    {
        if (mainCamera == null)
        {
            return Vector3.zero;
        }

        float targetZ = gameplayVisualRoot != null ? gameplayVisualRoot.position.z : 0f;
        float zDistance = targetZ - mainCamera.transform.position.z;
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, zDistance);
        Vector3 worldPoint = mainCamera.ScreenToWorldPoint(screenPoint);
        worldPoint.z = targetZ;
        return worldPoint;
    }

    private Vector2 ScreenToCanvasLocal(Vector2 screenPosition)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPoint);
        return localPoint;
    }

    private bool TryGetPointerDown(out Vector2 screenPosition, out int fingerId)
    {
#if ENABLE_INPUT_SYSTEM
        if (TryGetPointerDownInputSystem(out screenPosition, out fingerId))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (UnityEngine.Input.touchCount > 0)
        {
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                UnityEngine.Touch touch = UnityEngine.Input.GetTouch(i);
                if (touch.phase == UnityEngine.TouchPhase.Began)
                {
                    screenPosition = touch.position;
                    fingerId = touch.fingerId;
                    return true;
                }
            }
        }

        if (UnityEngine.Input.GetMouseButtonDown(0))
        {
            screenPosition = UnityEngine.Input.mousePosition;
            fingerId = MouseFinger;
            return true;
        }
#endif

        screenPosition = Vector2.zero;
        fingerId = NoFinger;
        return false;
    }

    private bool TryGetActivePointerPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (TryGetActivePointerPositionInputSystem(out screenPosition))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (activeFingerId == MouseFinger)
        {
            if (!UnityEngine.Input.GetMouseButton(0) && !UnityEngine.Input.GetMouseButtonUp(0))
            {
                screenPosition = Vector2.zero;
                return false;
            }

            screenPosition = UnityEngine.Input.mousePosition;
            return true;
        }

        for (int i = 0; i < UnityEngine.Input.touchCount; i++)
        {
            UnityEngine.Touch touch = UnityEngine.Input.GetTouch(i);
            if (touch.fingerId == activeFingerId)
            {
                screenPosition = touch.position;
                return touch.phase != UnityEngine.TouchPhase.Canceled;
            }
        }
#endif

        screenPosition = Vector2.zero;
        return false;
    }

    private bool TryGetActivePointerUp(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (TryGetActivePointerUpInputSystem(out screenPosition))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (activeFingerId == MouseFinger)
        {
            if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                screenPosition = UnityEngine.Input.mousePosition;
                return true;
            }

            screenPosition = Vector2.zero;
            return false;
        }

        for (int i = 0; i < UnityEngine.Input.touchCount; i++)
        {
            UnityEngine.Touch touch = UnityEngine.Input.GetTouch(i);
            if (touch.fingerId == activeFingerId && (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled))
            {
                screenPosition = touch.position;
                return true;
            }
        }
#endif

        screenPosition = Vector2.zero;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private bool TryGetPointerDownInputSystem(out Vector2 screenPosition, out int fingerId)
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    screenPosition = touch.position.ReadValue();
                    fingerId = touch.touchId.ReadValue();
                    return true;
                }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPosition = mouse.position.ReadValue();
            fingerId = MouseFinger;
            return true;
        }

        screenPosition = Vector2.zero;
        fingerId = NoFinger;
        return false;
    }

    private bool TryGetActivePointerPositionInputSystem(out Vector2 screenPosition)
    {
        if (activeFingerId == MouseFinger)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || (!mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame))
            {
                screenPosition = Vector2.zero;
                return false;
            }

            screenPosition = mouse.position.ReadValue();
            return true;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
        {
            screenPosition = Vector2.zero;
            return false;
        }

        foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
        {
            if (touch.touchId.ReadValue() != activeFingerId)
            {
                continue;
            }

            screenPosition = touch.position.ReadValue();
            return touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Canceled;
        }

        screenPosition = Vector2.zero;
        return false;
    }

    private bool TryGetActivePointerUpInputSystem(out Vector2 screenPosition)
    {
        if (activeFingerId == MouseFinger)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            screenPosition = Vector2.zero;
            return false;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
        {
            screenPosition = Vector2.zero;
            return false;
        }

        foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
        {
            if (touch.touchId.ReadValue() != activeFingerId)
            {
                continue;
            }

            UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
            if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled || touch.press.wasReleasedThisFrame)
            {
                screenPosition = touch.position.ReadValue();
                return true;
            }
        }

        screenPosition = Vector2.zero;
        return false;
    }
#endif

    private Transform CreateCircle(string objectName, Transform parent, float diameter, Color color)
    {
        GameObject circleObject = new GameObject(objectName, typeof(SpriteRenderer));
        Transform circleTransform = circleObject.transform;
        circleTransform.SetParent(parent, false);
        circleTransform.localPosition = Vector3.zero;
        circleTransform.localRotation = Quaternion.identity;
        circleTransform.localScale = Vector3.one * Mathf.Max(0.01f, diameter);

        SpriteRenderer renderer = circleObject.GetComponent<SpriteRenderer>();
        renderer.sprite = RuntimeSpriteFactory.GetCircleSprite();
        renderer.color = color;
        renderer.sortingOrder = generatedVisualSortingOrder;
        return circleTransform;
    }

    private static void PrepareRectTransform(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
            {
                return children[i];
            }
        }

        return null;
    }

    private static RectTransform FindChildRectByName(RectTransform root, string childName)
    {
        RectTransform[] children = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
            {
                return children[i];
            }
        }

        return null;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Destroy(root.GetChild(i).gameObject);
        }
    }
}
