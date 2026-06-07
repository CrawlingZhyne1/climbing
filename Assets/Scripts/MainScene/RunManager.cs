// MainScene의 런 초기화와 상태 전환을 관리한다.
// Camera 기준 월드 좌표계와 생성 루트를 준비한다.
// 5개 매니저의 초기화 순서를 통제한다.
// 매 프레임 수면, 플레이어, 청크 갱신을 호출한다.
// R 키 입력 시 현재 런을 즉시 초기화한다.
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class RunManager : MonoBehaviour
{
    public enum RunState
    {
        Holding,
        Aiming,
        Flying,
        HoldCooldown,
        FallingNoRescue,
        GameOver,
    }

    [Header("Scene References")]
    [SerializeField]
    private Canvas mainCanvas;

    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private GameRandomManager randomManager;

    [SerializeField]
    private WorldChunkManager worldChunkManager;

    [SerializeField]
    private PlayerClimbManager playerClimbManager;

    [SerializeField]
    private WaterManager waterManager;

    [Header("World Roots")]
    [SerializeField]
    private Transform worldRoot;

    [SerializeField]
    private Transform mapRoot;

    [SerializeField]
    private Transform gameplayVisualRoot;

    [Header("UI Roots")]
    [SerializeField]
    private RectTransform buttonRoot;

    [Header("Reference UI")]
    [SerializeField]
    private Vector2 referenceResolution = new Vector2(1080f, 1920f);

    [SerializeField]
    private bool configureCanvasScalerOnStart = true;

    [SerializeField]
    private bool forceScreenSpaceOverlay = true;

    [Header("Camera")]
    [SerializeField]
    private float orthographicSize = 10f;

    public RunState State { get; private set; } = RunState.GameOver;
    public RectTransform CanvasRect { get; private set; }
    public Transform WorldRoot => worldRoot;
    public Transform MapRoot => mapRoot;
    public Transform GameplayVisualRoot => gameplayVisualRoot;
    public RectTransform ButtonRoot => buttonRoot;
    public Vector2 ViewportWorldSize { get; private set; }
    public float WorldUnitsPerScreenPixelY { get; private set; }
    public bool IsRunning => State != RunState.GameOver;

    private bool initialized;

    private void Start()
    {
        BeginRun();
    }

    private void Update()
    {
        if (IsRestartKeyPressed())
        {
            RestartRun();
            return;
        }

        if (!initialized || State == RunState.GameOver)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        waterManager.ManagedUpdate(deltaTime, playerClimbManager.PlayerWorldPosition);
        playerClimbManager.ManagedUpdate(deltaTime);
        worldChunkManager.UpdateChunks(playerClimbManager.PlayerWorldPosition, waterManager.WaterSurfaceY);
        waterManager.RefreshVisual(playerClimbManager.PlayerWorldPosition);

        if (waterManager.IsPlayerSubmerged(playerClimbManager.PlayerWorldPosition))
        {
            EnterGameOver();
        }
    }

    private bool IsRestartKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (UnityEngine.Input.GetKeyDown(KeyCode.R))
        {
            return true;
        }
#endif

        return false;
    }

    public void BeginRun()
    {
        ResolveReferences();
        if (!enabled)
        {
            return;
        }

        ConfigureCanvas();
        ConfigureCamera();
        RefreshViewportWorldSize();

        worldRoot = EnsureSceneRoot(worldRoot, "WorldRoot");
        mapRoot = EnsureWorldChildRoot(mapRoot, worldRoot, "MapRoot");
        gameplayVisualRoot = EnsureWorldChildRoot(gameplayVisualRoot, worldRoot, "GameplayVisualRoot");
        buttonRoot = EnsureCanvasRoot(buttonRoot, "ButtonRoot");

        randomManager.BeginNewRun();
        worldChunkManager.Initialize(this, randomManager, mapRoot, ViewportWorldSize);
        playerClimbManager.Initialize(
            this,
            worldChunkManager,
            waterManager,
            mainCamera,
            CanvasRect,
            gameplayVisualRoot,
            buttonRoot
        );
        waterManager.Initialize(this, mapRoot, ViewportWorldSize, CanvasRect);

        SetState(RunState.Holding);
        playerClimbManager.ResetForNewRun(Vector2.zero);
        worldChunkManager.UpdateChunks(playerClimbManager.PlayerWorldPosition, waterManager.WaterSurfaceY);
        waterManager.RefreshVisual(playerClimbManager.PlayerWorldPosition);

        initialized = true;
    }

    public void RestartRun()
    {
        BeginRun();
    }

    public void SetState(RunState nextState)
    {
        if (State == RunState.GameOver && nextState != RunState.Holding)
        {
            return;
        }

        State = nextState;
    }

    public void EnterGameOver()
    {
        if (State == RunState.GameOver)
        {
            return;
        }

        State = RunState.GameOver;
        playerClimbManager.OnGameOver();
    }

    public Vector2 ScreenDeltaToWorldDelta(Vector2 screenDelta)
    {
        float safeScreenWidth = Mathf.Max(1f, Screen.width);
        float safeScreenHeight = Mathf.Max(1f, Screen.height);
        return new Vector2(
            screenDelta.x / safeScreenWidth * ViewportWorldSize.x,
            screenDelta.y / safeScreenHeight * ViewportWorldSize.y
        );
    }

    private void ResolveReferences()
    {
        if (mainCanvas == null)
        {
            mainCanvas = FindObjectOfType<Canvas>();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (randomManager == null)
        {
            randomManager = FindObjectOfType<GameRandomManager>();
        }

        if (worldChunkManager == null)
        {
            worldChunkManager = FindObjectOfType<WorldChunkManager>();
        }

        if (playerClimbManager == null)
        {
            playerClimbManager = FindObjectOfType<PlayerClimbManager>();
        }

        if (waterManager == null)
        {
            waterManager = FindObjectOfType<WaterManager>();
        }

        if (mainCanvas == null)
        {
            Debug.LogError("RunManager requires a Canvas in MainScene.");
            enabled = false;
            return;
        }

        if (mainCamera == null)
        {
            Debug.LogError("RunManager requires a Main Camera in MainScene.");
            enabled = false;
            return;
        }

        if (randomManager == null || worldChunkManager == null || playerClimbManager == null || waterManager == null)
        {
            Debug.LogError("RunManager requires GameRandomManager, WorldChunkManager, PlayerClimbManager, and WaterManager.");
            enabled = false;
            return;
        }

        CanvasRect = mainCanvas.GetComponent<RectTransform>();
        if (CanvasRect == null)
        {
            Debug.LogError("Main Canvas requires RectTransform.");
            enabled = false;
        }
    }

    private void ConfigureCanvas()
    {
        if (forceScreenSpaceOverlay)
        {
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        if (configureCanvasScalerOnStart)
        {
            CanvasScaler scaler = mainCanvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = mainCanvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
        }

        Canvas.ForceUpdateCanvases();
    }

    private void ConfigureCamera()
    {
        mainCamera.orthographic = true;
        mainCamera.orthographicSize = Mathf.Max(0.01f, orthographicSize);
    }

    private void RefreshViewportWorldSize()
    {
        float height = mainCamera.orthographicSize * 2f;
        float aspect = mainCamera.aspect;

        if (aspect <= 0f)
        {
            aspect = Screen.width > 0 && Screen.height > 0 ? (float)Screen.width / Screen.height : referenceResolution.x / referenceResolution.y;
        }

        ViewportWorldSize = new Vector2(height * aspect, height);
        WorldUnitsPerScreenPixelY = ViewportWorldSize.y / Mathf.Max(1f, Screen.height);
    }

    private static Transform EnsureSceneRoot(Transform existingRoot, string rootName)
    {
        Transform root = existingRoot;
        if (root == null)
        {
            GameObject found = GameObject.Find(rootName);
            root = found != null ? found.transform : null;
        }

        if (root == null)
        {
            GameObject rootObject = new GameObject(rootName);
            root = rootObject.transform;
        }

        root.SetParent(null, false);
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;
        return root;
    }

    private static Transform EnsureWorldChildRoot(Transform existingRoot, Transform parent, string rootName)
    {
        Transform root = existingRoot;
        if (root == null && parent != null)
        {
            root = parent.Find(rootName);
        }

        if (root == null)
        {
            GameObject rootObject = new GameObject(rootName);
            root = rootObject.transform;
        }

        root.SetParent(parent, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        return root;
    }

    private RectTransform EnsureCanvasRoot(RectTransform existingRoot, string rootName)
    {
        RectTransform root = existingRoot;
        if (root == null)
        {
            Transform found = CanvasRect.Find(rootName);
            root = found as RectTransform;
        }

        if (root == null)
        {
            GameObject rootObject = new GameObject(rootName, typeof(RectTransform));
            root = rootObject.GetComponent<RectTransform>();
            root.SetParent(CanvasRect, false);
        }

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = CanvasRect != null ? CanvasRect.rect.size : referenceResolution;
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;
        return root;
    }
}
