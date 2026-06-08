// 월드 좌표계에서 청크와 홀드를 생성한다.
// 현재 플레이어 위치와 수면 위치를 기준으로 활성 청크를 유지한다.
// 각 청크에는 시드 기반으로 8~12개의 홀드를 배치한다.
// 각 홀드 이미지와 성공 기준 원을 정해진 Sorting Layer에 표시한다.
// 캐릭터 손 중앙점이 성공 기준 원 안에 들어왔는지 검사한다.
using System.Collections.Generic;
using UnityEngine;

public sealed class WorldChunkManager : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("청크 안에 생성할 월드 홀드 프리펩.")]
    [SerializeField]
    private GameObject holdPrefab;

    [Header("Chunk Range")]
    [Tooltip("현재 청크 기준 좌우로 유지할 청크 개수.")]
    [SerializeField]
    private int horizontalChunkRadius = 3;

    [Tooltip("현재 화면 위쪽으로 미리 유지할 화면 단위 개수.")]
    [SerializeField]
    private int verticalScreenAhead = 3;

    [Header("Hold Placement")]
    [Tooltip("청크 하나에 배치할 최소 홀드 개수.")]
    [SerializeField]
    private int minHoldCount = 8;

    [Tooltip("청크 하나에 배치할 최대 홀드 개수.")]
    [SerializeField]
    private int maxHoldCount = 12;

    [Tooltip("뷰포트 가로 길이 대비 홀드 사이 최소 거리 비율.")]
    [SerializeField]
    private float minHoldDistanceViewportWidthRatio = 1f / 8f;

    [Tooltip("뷰포트 가로 길이 대비 청크 테두리 여백 비율.")]
    [SerializeField]
    private float edgeMarginViewportWidthRatio = 1f / 32f;

    [Tooltip("홀드 하나를 배치할 때 허용하는 최대 재시도 횟수.")]
    [SerializeField]
    private int maxPlacementAttemptsPerHold = 30;

    [Header("Hold Visual")]
    [Tooltip("HoldImage의 월드 표시 크기. Sprite의 긴 축이 이 값에 맞춰진다.")]
    [SerializeField]
    private float holdVisualSize = 0.75f;

    [Header("Hold Success Circle")]
    [Tooltip("홀드 성공 기준 원의 반지름. 캐릭터 손 중앙점이 이 원 안에 들어오면 홀드 성공.")]
    [SerializeField]
    private float holdSuccessCircleRadius = 0.9f;

    [Tooltip("홀드 성공 기준 원의 표시 색상.")]
    [SerializeField]
    private Color holdSuccessCircleColor = new Color(0.2f, 1f, 0.25f, 0.22f);

    private readonly Dictionary<Vector2Int, ChunkInstance> activeChunks = new Dictionary<Vector2Int, ChunkInstance>();
    private readonly List<HoldInstance> activeHolds = new List<HoldInstance>();
    private readonly List<Vector2Int> removalBuffer = new List<Vector2Int>();

    private GameRandomManager randomManager;
    private Transform mapRoot;
    private Transform generatedChunksRoot;
    private Vector2 viewportWorldSize;
    private float minHoldDistance;
    private float edgeMargin;
    private bool initialized;

    private const string GeneratedChunksSortingLayer = "GeneratedChunks";
    private const int HoldSortingOrder = 0;
    private const int HoldSuccessCircleSortingOrder = 10;
    private float appliedHoldVisualSize = -1f;
    private float appliedHoldSuccessCircleRadius = -1f;
    private Color appliedHoldSuccessCircleColor = Color.clear;

    public Vector2 ViewportWorldSize => viewportWorldSize;

    public void RefreshVisualTuning()
    {
        if (!initialized)
        {
            return;
        }

        RefreshHoldVisualTuningIfNeeded();
    }

    public void Initialize(GameRandomManager randomManager, Transform mapRoot, Vector2 viewportWorldSize)
    {
        this.randomManager = randomManager;
        this.mapRoot = mapRoot;
        this.viewportWorldSize = viewportWorldSize;
        minHoldDistance = viewportWorldSize.x * minHoldDistanceViewportWidthRatio;
        edgeMargin = viewportWorldSize.x * edgeMarginViewportWidthRatio;

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

        int currentChunkX = WorldToChunkIndex(playerWorldPosition.x, viewportWorldSize.x);
        int bottomChunkY = WorldToChunkIndex(waterSurfaceY, viewportWorldSize.y);
        int topChunkY = WorldToChunkIndex(playerWorldPosition.y + viewportWorldSize.y * (0.5f + verticalScreenAhead), viewportWorldSize.y);

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
            mapRoot.localPosition = new Vector3(-playerWorldPosition.x, -playerWorldPosition.y, 0f);
        }
    }

    public bool TryGetNearestHoldContainingPoint(Vector2 worldPoint, out Vector2 nearestHoldWorldPosition, out Transform nearestHoldRoot)
    {
        float radius = Mathf.Max(0f, holdSuccessCircleRadius);
        float radiusSquared = radius * radius;
        float bestDistanceSquared = float.PositiveInfinity;
        nearestHoldWorldPosition = Vector2.zero;
        nearestHoldRoot = null;

        for (int i = 0; i < activeHolds.Count; i++)
        {
            HoldInstance hold = activeHolds[i];
            float distanceSquared = (hold.SuccessWorldPosition - worldPoint).sqrMagnitude;
            if (distanceSquared <= radiusSquared && distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearestHoldWorldPosition = hold.SuccessWorldPosition;
                nearestHoldRoot = hold.Root;
            }
        }

        return nearestHoldRoot != null;
    }

    private Transform EnsureGeneratedChunksRoot()
    {
        Transform root = mapRoot.Find("GeneratedChunks");
        if (root == null)
        {
            GameObject rootObject = new GameObject("GeneratedChunks");
            root = rootObject.transform;
            root.SetParent(mapRoot, false);
        }

        root.localPosition = Vector3.zero;
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
        GameObject chunkObject = new GameObject($"Chunk_{coord.x}_{coord.y}");
        Transform chunkRoot = chunkObject.transform;
        chunkRoot.SetParent(generatedChunksRoot, false);
        chunkRoot.localPosition = new Vector3(coord.x * viewportWorldSize.x, coord.y * viewportWorldSize.y, 0f);
        chunkRoot.localRotation = Quaternion.identity;
        chunkRoot.localScale = Vector3.one;

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
            for (int attempt = 0; attempt < maxPlacementAttemptsPerHold; attempt++)
            {
                Vector2 localPosition = GetRandomLocalPosition(rng);
                if (!IsFarEnoughFromExisting(localPosition, placedLocalPositions))
                {
                    continue;
                }

                CreateHold(chunk, localPosition);
                placedLocalPositions.Add(localPosition);
                break;
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
        Transform holdRoot = holdObject.transform;
        holdRoot.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        holdRoot.localRotation = Quaternion.identity;
        holdRoot.localScale = Vector3.one;

        Transform holdImage = FindChildByName(holdRoot, "HoldImage");
        if (holdImage == null)
        {
            Debug.LogError("HoldPrefab requires child Transform named HoldImage.");
        }

        SpriteRenderer holdRenderer = holdImage != null ? holdImage.GetComponent<SpriteRenderer>() : holdRoot.GetComponent<SpriteRenderer>();
        ApplyHoldVisualScale(holdImage, holdRenderer);

        Transform successCircle = CreateHoldSuccessCircle(holdRoot, holdImage);
        ApplyHoldSuccessCircleVisual(successCircle, holdImage);

        Vector2 successCenterOffset = holdImage != null ? new Vector2(holdImage.localPosition.x, holdImage.localPosition.y) : Vector2.zero;
        Vector2 successWorldPosition = new Vector2(
            chunk.Coord.x * viewportWorldSize.x + localPosition.x + successCenterOffset.x,
            chunk.Coord.y * viewportWorldSize.y + localPosition.y + successCenterOffset.y
        );

        HoldInstance hold = new HoldInstance(chunk.Coord, holdRoot, holdImage, successCircle, successWorldPosition);
        chunk.Holds.Add(hold);
        activeHolds.Add(hold);
    }

    private void RefreshHoldVisualTuningIfNeeded()
    {
        float safeVisualSize = Mathf.Max(0.01f, holdVisualSize);
        float safeSuccessRadius = Mathf.Max(0f, holdSuccessCircleRadius);
        bool visualSizeChanged = !Mathf.Approximately(appliedHoldVisualSize, safeVisualSize);
        bool successCircleChanged = !Mathf.Approximately(appliedHoldSuccessCircleRadius, safeSuccessRadius)
            || appliedHoldSuccessCircleColor != holdSuccessCircleColor;

        if (!visualSizeChanged && !successCircleChanged)
        {
            return;
        }

        appliedHoldVisualSize = safeVisualSize;
        appliedHoldSuccessCircleRadius = safeSuccessRadius;
        appliedHoldSuccessCircleColor = holdSuccessCircleColor;

        for (int i = 0; i < activeHolds.Count; i++)
        {
            Transform image = activeHolds[i].Image;
            SpriteRenderer renderer = image != null ? image.GetComponent<SpriteRenderer>() : null;
            ApplyHoldVisualScale(image, renderer);
            ApplyHoldSuccessCircleVisual(activeHolds[i].SuccessCircle, image);
        }
    }

    private Transform CreateHoldSuccessCircle(Transform holdRoot, Transform holdImage)
    {
        if (holdRoot == null)
        {
            return null;
        }

        GameObject circleObject = new GameObject("HoldSuccessCircle", typeof(SpriteRenderer));
        Transform circleTransform = circleObject.transform;
        circleTransform.SetParent(holdRoot, false);
        circleTransform.localPosition = holdImage != null ? holdImage.localPosition : Vector3.zero;
        circleTransform.localRotation = Quaternion.identity;

        SpriteRenderer renderer = circleObject.GetComponent<SpriteRenderer>();
        renderer.sprite = RuntimeSpriteFactory.GetCircleSprite();
        renderer.color = holdSuccessCircleColor;
        ApplySpriteSorting(renderer, GeneratedChunksSortingLayer, HoldSuccessCircleSortingOrder);
        return circleTransform;
    }

    private void ApplyHoldVisualScale(Transform holdImage, SpriteRenderer renderer)
    {
        if (holdImage == null)
        {
            return;
        }

        holdImage.localRotation = Quaternion.identity;
        if (renderer != null)
        {
            ApplySpriteSorting(renderer, GeneratedChunksSortingLayer, HoldSortingOrder);
            ScaleSpriteRendererToWorldSize(renderer, Mathf.Max(0.01f, holdVisualSize));
            return;
        }

        holdImage.localScale = Vector3.one * Mathf.Max(0.01f, holdVisualSize);
    }

    private void ApplyHoldSuccessCircleVisual(Transform successCircle, Transform holdImage)
    {
        if (successCircle == null)
        {
            return;
        }

        float safeRadius = Mathf.Max(0f, holdSuccessCircleRadius);
        successCircle.localPosition = holdImage != null ? holdImage.localPosition : Vector3.zero;
        successCircle.localRotation = Quaternion.identity;
        successCircle.localScale = Vector3.one * (safeRadius * 2f);

        SpriteRenderer renderer = successCircle.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            renderer.color = holdSuccessCircleColor;
            ApplySpriteSorting(renderer, GeneratedChunksSortingLayer, HoldSuccessCircleSortingOrder);
        }
    }

    private Vector2 GetRandomLocalPosition(System.Random rng)
    {
        float minX = -viewportWorldSize.x * 0.5f + edgeMargin;
        float maxX = viewportWorldSize.x * 0.5f - edgeMargin;
        float minY = -viewportWorldSize.y * 0.5f + edgeMargin;
        float maxY = viewportWorldSize.y * 0.5f - edgeMargin;

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
        return chunkY * viewportWorldSize.y + viewportWorldSize.y * 0.5f;
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

    private static void ApplySpriteSorting(SpriteRenderer renderer, string sortingLayerName, int sortingOrder)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
    }

    private static void ScaleSpriteRendererToWorldSize(SpriteRenderer renderer, float targetLongAxisSize)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        Vector2 spriteSize = renderer.sprite.bounds.size;
        float longAxis = Mathf.Max(spriteSize.x, spriteSize.y);
        if (longAxis <= 0f)
        {
            renderer.transform.localScale = Vector3.one;
            return;
        }

        float scale = targetLongAxisSize / longAxis;
        renderer.transform.localScale = Vector3.one * scale;
    }

    private sealed class ChunkInstance
    {
        public readonly Vector2Int Coord;
        public readonly Transform Root;
        public readonly List<HoldInstance> Holds = new List<HoldInstance>();

        public ChunkInstance(Vector2Int coord, Transform root)
        {
            Coord = coord;
            Root = root;
        }
    }

    private sealed class HoldInstance
    {
        public readonly Vector2Int ChunkCoord;
        public readonly Transform Root;
        public readonly Transform Image;
        public readonly Transform SuccessCircle;
        public readonly Vector2 SuccessWorldPosition;

        public HoldInstance(
            Vector2Int chunkCoord,
            Transform root,
            Transform image,
            Transform successCircle,
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
