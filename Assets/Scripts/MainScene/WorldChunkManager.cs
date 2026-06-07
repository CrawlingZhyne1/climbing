// Canvas 좌표계에서 청크와 홀드를 생성한다.
// 현재 플레이어 위치와 수면 위치를 기준으로 활성 청크를 유지한다.
// 각 청크에는 시드 기반으로 8~12개의 홀드를 배치한다.
// 각 홀드 이미지 위에 성공 기준 원을 표시한다.
// 캐릭터 손 중앙점이 성공 기준 원 안에 들어왔는지 검사한다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorldChunkManager : MonoBehaviour
{
    [Header("Prefab")]
    // 청크 안에 생성할 홀드 UI 프리펩이다.
    [Tooltip("청크 안에 생성할 홀드 UI 프리펩.")]
    [SerializeField]
    private GameObject holdPrefab;

    [Header("Chunk Range")]
    // 현재 청크 기준 좌우로 유지할 청크 개수다.
    [Tooltip("현재 청크 기준 좌우로 유지할 청크 개수.")]
    [SerializeField]
    private int horizontalChunkRadius = 3;

    // 현재 화면 위쪽으로 미리 유지할 화면 단위 개수다.
    [Tooltip("현재 화면 위쪽으로 미리 유지할 화면 단위 개수.")]
    [SerializeField]
    private int verticalScreenAhead = 3;

    [Header("Hold Placement")]
    // 청크 하나에 배치할 최소 홀드 개수다.
    [Tooltip("청크 하나에 배치할 최소 홀드 개수.")]
    [SerializeField]
    private int minHoldCount = 8;

    // 청크 하나에 배치할 최대 홀드 개수다.
    [Tooltip("청크 하나에 배치할 최대 홀드 개수.")]
    [SerializeField]
    private int maxHoldCount = 12;

    // 화면 가로 길이 대비 홀드 사이 최소 거리 비율이다.
    [Tooltip("화면 가로 길이 대비 홀드 사이 최소 거리 비율.")]
    [SerializeField]
    private float minHoldDistanceScreenWidthRatio = 1f / 8f;

    // 화면 가로 길이 대비 청크 테두리 여백 비율이다.
    [Tooltip("화면 가로 길이 대비 청크 테두리 여백 비율.")]
    [SerializeField]
    private float edgeMarginScreenWidthRatio = 1f / 32f;

    // 홀드 하나를 배치할 때 허용하는 최대 재시도 횟수다.
    [Tooltip("홀드 하나를 배치할 때 허용하는 최대 재시도 횟수.")]
    [SerializeField]
    private int maxPlacementAttemptsPerHold = 30;

    [Header("Hold Visual")]
    // HoldImage의 표시 크기 배율이다. 1이면 프리펩 원본 크기다.
    [Tooltip("HoldImage 표시 크기 배율. 1이면 프리펩 원본 크기.")]
    [SerializeField]
    private float holdVisualSizeMultiplier = 1f;

    [Header("Hold Success Circle")]
    // 홀드 성공 기준 원의 반지름이다. 캐릭터 손 중앙점이 이 원 안에 들어오면 홀드 성공이다.
    [Tooltip("홀드 성공 기준 원의 반지름. 캐릭터 손 중앙점이 이 원 안에 들어오면 홀드 성공.")]
    [SerializeField]
    private float holdSuccessCircleRadius = 80f;

    // 홀드 성공 기준 원의 표시 색상이다.
    [Tooltip("홀드 성공 기준 원의 표시 색상.")]
    [SerializeField]
    private Color holdSuccessCircleColor = new Color(0.2f, 1f, 0.25f, 0.22f);

    private readonly Dictionary<Vector2Int, ChunkInstance> activeChunks = new Dictionary<Vector2Int, ChunkInstance>();
    private readonly List<HoldInstance> activeHolds = new List<HoldInstance>();
    private readonly List<Vector2Int> removalBuffer = new List<Vector2Int>();

    private RunManager runManager;
    private GameRandomManager randomManager;
    private RectTransform mapRoot;
    private RectTransform generatedChunksRoot;
    private Vector2 viewportSize;
    private float minHoldDistance;
    private float edgeMargin;
    private bool initialized;
    private float appliedHoldVisualSizeMultiplier = -1f;
    private float appliedHoldSuccessCircleRadius = -1f;
    private Color appliedHoldSuccessCircleColor = Color.clear;

    public Vector2 ViewportSize => viewportSize;

    public void RefreshVisualTuning()
    {
        if (!initialized)
        {
            return;
        }

        RefreshHoldVisualTuningIfNeeded();
    }

    public void Initialize(RunManager runManager, GameRandomManager randomManager, RectTransform mapRoot, Vector2 viewportSize)
    {
        this.runManager = runManager;
        this.randomManager = randomManager;
        this.mapRoot = mapRoot;
        this.viewportSize = viewportSize;
        minHoldDistance = viewportSize.x * minHoldDistanceScreenWidthRatio;
        edgeMargin = viewportSize.x * edgeMarginScreenWidthRatio;

        generatedChunksRoot = EnsureGeneratedChunksRoot();
        ClearGeneratedChunks();
        activeChunks.Clear();
        activeHolds.Clear();
        initialized = true;
    }

    public void UpdateChunks(Vector2 playerWorldPosition, float waterSurfaceY)
    {
        if (!initialized || holdPrefab == null)
        {
            return;
        }

        SetMapOffset(playerWorldPosition);

        int currentChunkX = WorldToChunkIndex(playerWorldPosition.x, viewportSize.x);
        int bottomChunkY = WorldToChunkIndex(waterSurfaceY, viewportSize.y);
        int topChunkY = WorldToChunkIndex(playerWorldPosition.y + viewportSize.y * (0.5f + verticalScreenAhead), viewportSize.y);

        int minChunkX = currentChunkX - horizontalChunkRadius;
        int maxChunkX = currentChunkX + horizontalChunkRadius;

        for (int y = bottomChunkY; y <= topChunkY; y++)
        {
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                Vector2Int coord = new Vector2Int(x, y);
                if (!activeChunks.ContainsKey(coord))
                {
                    CreateChunk(coord);
                }
            }
        }

        removalBuffer.Clear();
        foreach (KeyValuePair<Vector2Int, ChunkInstance> pair in activeChunks)
        {
            Vector2Int coord = pair.Key;
            bool outsideHorizontalRange = coord.x < minChunkX || coord.x > maxChunkX;
            bool aboveNeededRange = coord.y > topChunkY;
            bool fullyBelowWater = GetChunkTopY(coord.y) < waterSurfaceY;

            if (outsideHorizontalRange || aboveNeededRange || fullyBelowWater)
            {
                removalBuffer.Add(coord);
            }
        }

        for (int i = 0; i < removalBuffer.Count; i++)
        {
            RemoveChunk(removalBuffer[i]);
        }

        RefreshHoldVisualTuningIfNeeded();
    }

    public void SetMapOffset(Vector2 playerWorldPosition)
    {
        if (mapRoot != null)
        {
            mapRoot.anchoredPosition = -playerWorldPosition;
        }
    }

    public bool TryGetNearestHoldContainingPoint(Vector2 worldPoint, out Vector2 nearestHoldWorldPosition, out RectTransform nearestHoldRect)
    {
        float radius = Mathf.Max(0f, holdSuccessCircleRadius);
        float radiusSquared = radius * radius;
        float bestDistanceSquared = float.PositiveInfinity;
        nearestHoldWorldPosition = Vector2.zero;
        nearestHoldRect = null;

        for (int i = 0; i < activeHolds.Count; i++)
        {
            HoldInstance hold = activeHolds[i];
            float distanceSquared = (hold.SuccessWorldPosition - worldPoint).sqrMagnitude;
            if (distanceSquared <= radiusSquared && distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearestHoldWorldPosition = hold.SuccessWorldPosition;
                nearestHoldRect = hold.Root;
            }
        }

        return nearestHoldRect != null;
    }

    private RectTransform EnsureGeneratedChunksRoot()
    {
        Transform found = mapRoot.Find("GeneratedChunks");
        RectTransform root = found as RectTransform;
        if (root == null)
        {
            GameObject rootObject = new GameObject("GeneratedChunks", typeof(RectTransform));
            root = rootObject.GetComponent<RectTransform>();
            root.SetParent(mapRoot, false);
        }

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = viewportSize;
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;
        return root;
    }

    private void ClearGeneratedChunks()
    {
        if (generatedChunksRoot == null)
        {
            return;
        }

        for (int i = generatedChunksRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(generatedChunksRoot.GetChild(i).gameObject);
        }
    }

    private void CreateChunk(Vector2Int coord)
    {
        GameObject chunkObject = new GameObject($"Chunk_{coord.x}_{coord.y}", typeof(RectTransform));
        RectTransform chunkRoot = chunkObject.GetComponent<RectTransform>();
        chunkRoot.SetParent(generatedChunksRoot, false);
        PrepareRectTransform(chunkRoot);
        chunkRoot.sizeDelta = viewportSize;
        chunkRoot.anchoredPosition = new Vector2(coord.x * viewportSize.x, coord.y * viewportSize.y);

        ChunkInstance chunk = new ChunkInstance(coord, chunkRoot);
        activeChunks.Add(coord, chunk);

        System.Random rng = randomManager.CreateChunkRandom(coord.x, coord.y);
        int holdCount = rng.Next(minHoldCount, maxHoldCount + 1);
        List<Vector2> placedLocalPositions = new List<Vector2>(holdCount);

        if (coord == Vector2Int.zero)
        {
            CreateHold(chunk, Vector2.zero);
            placedLocalPositions.Add(Vector2.zero);
            holdCount -= 1;
        }

        for (int i = 0; i < holdCount; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < maxPlacementAttemptsPerHold; attempt++)
            {
                Vector2 localPosition = GetRandomLocalPosition(rng);
                if (!IsFarEnoughFromExisting(localPosition, placedLocalPositions))
                {
                    continue;
                }

                CreateHold(chunk, localPosition);
                placedLocalPositions.Add(localPosition);
                placed = true;
                break;
            }

            if (!placed)
            {
                continue;
            }
        }
    }

    private void RemoveChunk(Vector2Int coord)
    {
        if (!activeChunks.TryGetValue(coord, out ChunkInstance chunk))
        {
            return;
        }

        for (int i = activeHolds.Count - 1; i >= 0; i--)
        {
            if (activeHolds[i].ChunkCoord == coord)
            {
                activeHolds.RemoveAt(i);
            }
        }

        if (chunk.Root != null)
        {
            Destroy(chunk.Root.gameObject);
        }

        activeChunks.Remove(coord);
    }

    private void CreateHold(ChunkInstance chunk, Vector2 localPosition)
    {
        GameObject holdObject = Instantiate(holdPrefab, chunk.Root);
        RectTransform holdRoot = holdObject.GetComponent<RectTransform>();
        if (holdRoot == null)
        {
            Debug.LogError("HoldPrefab root requires RectTransform.");
            Destroy(holdObject);
            return;
        }

        PrepareRectTransform(holdRoot);
        holdRoot.anchoredPosition = localPosition;

        RectTransform holdImage = FindChildByName(holdRoot, "HoldImage");
        if (holdImage == null)
        {
            Debug.LogError("HoldPrefab requires child RectTransform named HoldImage.");
        }

        RectTransform successCircle = CreateHoldSuccessCircle(holdRoot, holdImage);
        ApplyHoldVisualScale(holdImage);
        ApplyHoldSuccessCircleVisual(successCircle, holdImage);

        Vector2 successCenterOffset = holdImage != null ? holdImage.anchoredPosition : Vector2.zero;
        Vector2 successWorldPosition = new Vector2(
            chunk.Coord.x * viewportSize.x + localPosition.x + successCenterOffset.x,
            chunk.Coord.y * viewportSize.y + localPosition.y + successCenterOffset.y
        );

        HoldInstance hold = new HoldInstance(chunk.Coord, holdRoot, holdImage, successCircle, successWorldPosition);
        chunk.Holds.Add(hold);
        activeHolds.Add(hold);
    }

    private void RefreshHoldVisualTuningIfNeeded()
    {
        float safeVisualMultiplier = Mathf.Max(0.01f, holdVisualSizeMultiplier);
        float safeSuccessRadius = Mathf.Max(0f, holdSuccessCircleRadius);
        bool visualScaleChanged = !Mathf.Approximately(appliedHoldVisualSizeMultiplier, safeVisualMultiplier);
        bool successCircleChanged = !Mathf.Approximately(appliedHoldSuccessCircleRadius, safeSuccessRadius)
            || appliedHoldSuccessCircleColor != holdSuccessCircleColor;

        if (!visualScaleChanged && !successCircleChanged)
        {
            return;
        }

        appliedHoldVisualSizeMultiplier = safeVisualMultiplier;
        appliedHoldSuccessCircleRadius = safeSuccessRadius;
        appliedHoldSuccessCircleColor = holdSuccessCircleColor;

        for (int i = 0; i < activeHolds.Count; i++)
        {
            ApplyHoldVisualScale(activeHolds[i].Image);
            ApplyHoldSuccessCircleVisual(activeHolds[i].SuccessCircle, activeHolds[i].Image);
        }
    }

    private RectTransform CreateHoldSuccessCircle(RectTransform holdRoot, RectTransform holdImage)
    {
        if (holdRoot == null)
        {
            return null;
        }

        GameObject circleObject = new GameObject("HoldSuccessCircle", typeof(RectTransform), typeof(Image));
        RectTransform circleRect = circleObject.GetComponent<RectTransform>();
        circleRect.SetParent(holdRoot, false);
        PrepareRectTransform(circleRect);
        if (holdImage != null)
        {
            circleRect.anchoredPosition = holdImage.anchoredPosition;
        }
        circleRect.SetAsLastSibling();

        Image circleImage = circleObject.GetComponent<Image>();
        circleImage.sprite = CircleSpriteProvider.GetCircleSprite();
        circleImage.raycastTarget = false;
        return circleRect;
    }

    private void ApplyHoldVisualScale(RectTransform holdImage)
    {
        if (holdImage == null)
        {
            return;
        }

        float safeMultiplier = Mathf.Max(0.01f, holdVisualSizeMultiplier);
        holdImage.localScale = Vector3.one * safeMultiplier;
    }

    private void ApplyHoldSuccessCircleVisual(RectTransform successCircle, RectTransform holdImage)
    {
        if (successCircle == null)
        {
            return;
        }

        float safeRadius = Mathf.Max(0f, holdSuccessCircleRadius);
        successCircle.sizeDelta = new Vector2(safeRadius * 2f, safeRadius * 2f);
        successCircle.anchoredPosition = holdImage != null ? holdImage.anchoredPosition : Vector2.zero;
        successCircle.SetAsLastSibling();

        Image circleImage = successCircle.GetComponent<Image>();
        if (circleImage != null)
        {
            circleImage.color = holdSuccessCircleColor;
        }
    }

    private Vector2 GetRandomLocalPosition(System.Random rng)
    {
        float minX = -viewportSize.x * 0.5f + edgeMargin;
        float maxX = viewportSize.x * 0.5f - edgeMargin;
        float minY = -viewportSize.y * 0.5f + edgeMargin;
        float maxY = viewportSize.y * 0.5f - edgeMargin;

        return new Vector2(
            Mathf.Lerp(minX, maxX, (float)rng.NextDouble()),
            Mathf.Lerp(minY, maxY, (float)rng.NextDouble())
        );
    }

    private bool IsFarEnoughFromExisting(Vector2 candidate, List<Vector2> existing)
    {
        float minDistanceSquared = minHoldDistance * minHoldDistance;
        for (int i = 0; i < existing.Count; i++)
        {
            if ((existing[i] - candidate).sqrMagnitude < minDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    private int WorldToChunkIndex(float value, float chunkSize)
    {
        return Mathf.FloorToInt((value + chunkSize * 0.5f) / chunkSize);
    }

    private float GetChunkTopY(int chunkY)
    {
        return chunkY * viewportSize.y + viewportSize.y * 0.5f;
    }

    private static void PrepareRectTransform(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private static RectTransform FindChildByName(RectTransform root, string childName)
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

    private static class CircleSpriteProvider
    {
        private static Sprite circleSprite;

        public static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
            {
                return circleSprite;
            }

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;

            Color clear = new Color(1f, 1f, 1f, 0f);
            Color solid = Color.white;
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = (size - 1) * 0.5f;
            float radiusSquared = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    texture.SetPixel(x, y, (point - center).sqrMagnitude <= radiusSquared ? solid : clear);
                }
            }

            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return circleSprite;
        }
    }

    private sealed class ChunkInstance
    {
        public readonly Vector2Int Coord;
        public readonly RectTransform Root;
        public readonly List<HoldInstance> Holds = new List<HoldInstance>();

        public ChunkInstance(Vector2Int coord, RectTransform root)
        {
            Coord = coord;
            Root = root;
        }
    }

    private sealed class HoldInstance
    {
        public readonly Vector2Int ChunkCoord;
        public readonly RectTransform Root;
        public readonly RectTransform Image;
        public readonly RectTransform SuccessCircle;
        public readonly Vector2 SuccessWorldPosition;

        public HoldInstance(
            Vector2Int chunkCoord,
            RectTransform root,
            RectTransform image,
            RectTransform successCircle,
            Vector2 successWorldPosition
        )
        {
            ChunkCoord = chunkCoord;
            Root = root;
            Image = image;
            SuccessCircle = successCircle;
            SuccessWorldPosition = successWorldPosition;
        }
    }
}
