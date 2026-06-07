// 수면 위치와 상승 속도를 관리한다.
// 시간에 따라 수면 상승 속도를 증가시킨다.
// Canvas 위에 수면 이미지를 생성하고 위치를 갱신한다.
// 플레이어와 수면 사이의 거리를 screen-space HUD TMP 텍스트로 표시한다.
// 플레이어 기준으로 수면에 잠겼는지 검사한다.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WaterManager : MonoBehaviour
{
    [Header("Water Motion")]
    [Tooltip("초기 수면 상승 속도. Canvas 좌표 단위/초 기준이다.")]
    [SerializeField]
    private float baseWaterSpeed = 40f;

    [Tooltip("수면 상승 속도 증가 간격. 이 시간마다 speedGrowthMultiplier만큼 성장한다.")]
    [SerializeField]
    private float speedGrowthIntervalSeconds = 10f;

    [Tooltip("수면 상승 속도 증가 배율. 연속 증가식에 사용된다.")]
    [SerializeField]
    private float speedGrowthMultiplier = 1.05f;

    [Tooltip("런 시작 시 수면을 화면 하단보다 얼마나 아래에 둘지 정한다. Canvas 좌표 단위 기준이다.")]
    [SerializeField]
    private float initialOffsetBelowScreenBottom = 160f;

    [Header("Visual")]
    [Tooltip("수면 표시용 RectTransform. 비워두면 MapRoot 아래에 WaterVisual을 자동 생성한다.")]
    [SerializeField]
    private RectTransform waterVisual;

    [Tooltip("WaterVisual을 자동 생성할 때 사용할 색상이다.")]
    [SerializeField]
    private Color generatedWaterColor = new Color(0.1f, 0.45f, 1f, 0.55f);

    [Header("Distance Text")]
    [Tooltip("플레이어와 수면 사이의 거리를 표시할 TMP 텍스트. Canvas에 고정된 TextMeshProUGUI를 연결한다.")]
    [SerializeField]
    private TMP_Text waterDistanceText;

    [Tooltip("텍스트를 고정할 screen-space UI 루트. 비워두면 mapRoot가 속한 root Canvas를 사용한다.")]
    [SerializeField]
    private RectTransform distanceTextFixedRoot;

    [Tooltip("켜면 distance text를 mapRoot에서 분리하고 화면 고정 UI로 배치한다.")]
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

    [Tooltip("거리 숫자 앞에 붙일 텍스트다. 숫자만 보이게 하려면 비워둔다.")]
    [SerializeField]
    private string distanceTextPrefix = "";

    [Tooltip("거리 숫자 뒤에 붙일 텍스트다.")]
    [SerializeField]
    private string distanceTextSuffix = "m";

    private RunManager runManager;
    private RectTransform mapRoot;
    private Vector2 viewportSize;
    private float elapsedTime;
    private bool initialized;

    public float WaterSurfaceY { get; private set; }

    public void Initialize(RunManager runManager, RectTransform mapRoot, Vector2 viewportSize)
    {
        this.runManager = runManager;
        this.mapRoot = mapRoot;
        this.viewportSize = viewportSize;
        elapsedTime = 0f;
        WaterSurfaceY = -viewportSize.y * 0.5f - initialOffsetBelowScreenBottom;
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

        waterVisual.anchoredPosition = new Vector2(playerWorldPosition.x, WaterSurfaceY);
        waterVisual.sizeDelta = new Vector2(viewportSize.x * 10f, viewportSize.y * 2f);
        waterVisual.SetAsLastSibling();
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
        if (viewportSize.y <= 0f || metersPerScreenHeight <= 0f)
        {
            return 0;
        }

        float distanceCanvasUnits = playerWorldPosition.y - WaterSurfaceY;
        float clampedDistanceCanvasUnits = Mathf.Max(0f, distanceCanvasUnits);
        float distanceMeters = clampedDistanceCanvasUnits / viewportSize.y * metersPerScreenHeight;
        return Mathf.RoundToInt(distanceMeters);
    }

    // Distance text를 mapRoot 같은 이동 계층에서 분리하고, Canvas 기준 고정 HUD 위치에 둔다.
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

        if (mapRoot == null)
        {
            return null;
        }

        Canvas rootCanvas = mapRoot.GetComponentInParent<Canvas>();
        if (rootCanvas != null)
        {
            return rootCanvas.transform as RectTransform;
        }

        return mapRoot.root as RectTransform;
    }

    private void EnsureWaterVisual()
    {
        if (waterVisual == null)
        {
            Transform found = mapRoot.Find("WaterVisual");
            waterVisual = found as RectTransform;
        }

        if (waterVisual == null)
        {
            GameObject waterObject = new GameObject("WaterVisual", typeof(RectTransform), typeof(Image));
            waterVisual = waterObject.GetComponent<RectTransform>();
            waterVisual.SetParent(mapRoot, false);

            Image image = waterObject.GetComponent<Image>();
            image.color = generatedWaterColor;
            image.raycastTarget = false;
        }

        waterVisual.anchorMin = new Vector2(0.5f, 0.5f);
        waterVisual.anchorMax = new Vector2(0.5f, 0.5f);
        waterVisual.pivot = new Vector2(0.5f, 1f);
        waterVisual.localScale = Vector3.one;
        waterVisual.localRotation = Quaternion.identity;
    }
}
