// 수면 위치와 상승 속도를 관리한다.
// 시간에 따라 수면 상승 속도를 증가시킨다.
// 월드 공간에 수면 SpriteRenderer를 생성하고 갱신한다.
// 플레이어와 수면 사이의 거리를 screen-space HUD TMP 텍스트로 표시한다.
// 플레이어 기준으로 수면에 잠겼는지 검사한다.
using TMPro;
using UnityEngine;

public sealed class WaterManager : MonoBehaviour
{
    [Header("Water Motion")]
    [Tooltip("초기 수면 상승 속도. 월드 단위/초 기준.")]
    [SerializeField]
    private float baseWaterSpeed = 0.42f;

    [Tooltip("수면 상승 속도 증가 간격. 이 시간마다 speedGrowthMultiplier만큼 성장한다.")]
    [SerializeField]
    private float speedGrowthIntervalSeconds = 10f;

    [Tooltip("수면 상승 속도 증가 배율. 연속 증가식에 사용된다.")]
    [SerializeField]
    private float speedGrowthMultiplier = 1.05f;

    [Tooltip("런 시작 시 수면을 화면 하단보다 얼마나 아래에 둘지 정한다. 월드 단위 기준.")]
    [SerializeField]
    private float initialOffsetBelowViewportBottom = 4f;

    [Header("Visual")]
    [Tooltip("수면 표시용 Transform. 비워두면 MapRoot 아래에 WaterVisual을 자동 생성한다.")]
    [SerializeField]
    private Transform waterVisual;

    [Tooltip("WaterVisual을 자동 생성할 때 사용할 색상.")]
    [SerializeField]
    private Color generatedWaterColor = new Color(0.1f, 0.45f, 1f, 0.55f);

    [Tooltip("수면 표시 폭. 뷰포트 가로 길이에 곱한다.")]
    [SerializeField]
    private float waterWidthViewportMultiplier = 3f;

    [Tooltip("수면 표시 높이. 뷰포트 세로 길이에 곱한다.")]
    [SerializeField]
    private float waterHeightViewportMultiplier = 2f;

    [Header("Distance Text")]
    [Tooltip("플레이어와 수면 사이의 거리를 표시할 TMP 텍스트. Canvas에 고정된 TextMeshProUGUI를 연결한다.")]
    [SerializeField]
    private TMP_Text waterDistanceText;

    [Tooltip("텍스트를 고정할 screen-space UI 루트. 비워두면 Main Canvas를 사용한다.")]
    [SerializeField]
    private RectTransform distanceTextFixedRoot;

    [Tooltip("켜면 distance text를 화면 고정 UI 위치에 둔다.")]
    [SerializeField]
    private bool forceDistanceTextScreenFixed = true;

    [Tooltip("화면 기준 anchor 위치. (0.5, 1)은 화면 상단 중앙이다.")]
    [SerializeField]
    private Vector2 distanceTextAnchor = new Vector2(0.5f, 1f);

    [Tooltip("텍스트 RectTransform의 pivot. (0.5, 1)은 상단 중앙 기준이다.")]
    [SerializeField]
    private Vector2 distanceTextPivot = new Vector2(0.5f, 1f);

    [Tooltip("anchor 기준 anchoredPosition. 화면 상단 중앙에서 아래로 내리려면 y를 음수로 둔다.")]
    [SerializeField]
    private Vector2 distanceTextAnchoredPosition = new Vector2(0f, -48f);

    [Tooltip("화면 전체 높이를 몇 미터로 볼지 정한다.")]
    [SerializeField]
    private float metersPerScreenHeight = 9f;

    [Tooltip("거리 숫자 앞에 붙일 텍스트. 숫자만 보이게 하려면 비워둔다.")]
    [SerializeField]
    private string distanceTextPrefix = "";

    [Tooltip("거리 숫자 뒤에 붙일 텍스트.")]
    [SerializeField]
    private string distanceTextSuffix = "m";

    private RunManager runManager;
    private Transform mapRoot;
    private RectTransform canvasRect;
    private Vector2 viewportWorldSize;
    private SpriteRenderer waterRenderer;
    private float elapsedTime;
    private bool initialized;

    private const string WaterSortingLayer = "Water";
    private const int WaterSortingOrder = 0;

    public float WaterSurfaceY { get; private set; }

    public void Initialize(RunManager runManager, Transform mapRoot, Vector2 viewportWorldSize, RectTransform canvasRect)
    {
        this.runManager = runManager;
        this.mapRoot = mapRoot;
        this.viewportWorldSize = viewportWorldSize;
        this.canvasRect = canvasRect;
        elapsedTime = 0f;
        WaterSurfaceY = -viewportWorldSize.y * 0.5f - initialOffsetBelowViewportBottom;
        EnsureWaterVisual();
        EnsureDistanceTextFixedPlacement();
        RefreshDistanceText(Vector2.zero);
        initialized = true;
    }

    public void ManagedUpdate(float deltaTime, Vector2 playerWorldPosition)
    {
        if (!initialized || runManager.State == RunManager.RunState.GameOver)
        {
            return;
        }

        elapsedTime += deltaTime;
        float speed = GetCurrentWaterSpeed();
        WaterSurfaceY += speed * deltaTime;
        RefreshVisual(playerWorldPosition);
        EnsureDistanceTextFixedPlacement();
        RefreshDistanceText(playerWorldPosition);
    }

    public void RefreshVisual(Vector2 playerWorldPosition)
    {
        if (waterVisual == null)
        {
            return;
        }

        float width = Mathf.Max(0.01f, viewportWorldSize.x * waterWidthViewportMultiplier);
        float height = Mathf.Max(0.01f, viewportWorldSize.y * waterHeightViewportMultiplier);
        waterVisual.localPosition = new Vector3(playerWorldPosition.x, WaterSurfaceY - height * 0.5f, 0f);
        waterVisual.localRotation = Quaternion.identity;
        waterVisual.localScale = new Vector3(width, height, 1f);

        if (waterRenderer != null)
        {
            waterRenderer.color = generatedWaterColor;
            ApplySpriteSorting(waterRenderer, WaterSortingLayer, WaterSortingOrder);
        }
    }

    public bool IsPlayerSubmerged(Vector2 playerWorldPosition)
    {
        return playerWorldPosition.y <= WaterSurfaceY;
    }

    private float GetCurrentWaterSpeed()
    {
        if (speedGrowthIntervalSeconds <= 0f)
        {
            return baseWaterSpeed;
        }

        float exponent = elapsedTime / speedGrowthIntervalSeconds;
        return baseWaterSpeed * Mathf.Pow(speedGrowthMultiplier, exponent);
    }

    private void RefreshDistanceText(Vector2 playerWorldPosition)
    {
        if (waterDistanceText == null)
        {
            return;
        }

        int distanceMeters = CalculateDistanceMetersFromPlayer(playerWorldPosition);
        waterDistanceText.text = distanceTextPrefix + distanceMeters + distanceTextSuffix;
    }

    private int CalculateDistanceMetersFromPlayer(Vector2 playerWorldPosition)
    {
        if (viewportWorldSize.y <= 0f || metersPerScreenHeight <= 0f)
        {
            return 0;
        }

        float distanceWorldUnits = playerWorldPosition.y - WaterSurfaceY;
        float clampedDistanceWorldUnits = Mathf.Max(0f, distanceWorldUnits);
        float distanceMeters = clampedDistanceWorldUnits / viewportWorldSize.y * metersPerScreenHeight;
        return Mathf.RoundToInt(distanceMeters);
    }

    private void EnsureDistanceTextFixedPlacement()
    {
        if (!forceDistanceTextScreenFixed || waterDistanceText == null)
        {
            return;
        }

        RectTransform textRect = waterDistanceText.rectTransform;
        RectTransform targetRoot = GetDistanceTextFixedRoot();
        if (targetRoot != null && textRect.parent != targetRoot)
        {
            textRect.SetParent(targetRoot, false);
        }

        textRect.anchorMin = distanceTextAnchor;
        textRect.anchorMax = distanceTextAnchor;
        textRect.pivot = distanceTextPivot;
        textRect.anchoredPosition = distanceTextAnchoredPosition;
        textRect.localScale = Vector3.one;
        textRect.localRotation = Quaternion.identity;
        textRect.SetAsLastSibling();
    }

    private RectTransform GetDistanceTextFixedRoot()
    {
        if (distanceTextFixedRoot != null)
        {
            return distanceTextFixedRoot;
        }

        return canvasRect;
    }

    private static void ApplySpriteSorting(SpriteRenderer renderer, string sortingLayerName, int sortingOrder)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
    }

    private void EnsureWaterVisual()
    {
        if (waterVisual == null && mapRoot != null)
        {
            waterVisual = mapRoot.Find("WaterVisual");
        }

        if (waterVisual == null)
        {
            GameObject waterObject = new GameObject("WaterVisual", typeof(SpriteRenderer));
            waterVisual = waterObject.transform;
            waterVisual.SetParent(mapRoot, false);
        }

        waterVisual.localRotation = Quaternion.identity;
        waterRenderer = waterVisual.GetComponent<SpriteRenderer>();
        if (waterRenderer == null)
        {
            waterRenderer = waterVisual.gameObject.AddComponent<SpriteRenderer>();
        }

        waterRenderer.sprite = RuntimeSpriteFactory.GetSquareSprite();
        waterRenderer.color = generatedWaterColor;
        ApplySpriteSorting(waterRenderer, WaterSortingLayer, WaterSortingOrder);
    }
}
