// 런타임 표시용 기본 Sprite를 생성하고 캐싱한다.
// 원형 Sprite와 사각형 Sprite를 제공한다.
// SpriteRenderer 기반 월드 시각 요소에서 사용한다.
// 생성한 Texture와 Sprite는 씬 저장 대상이 아니다.
using UnityEngine;

public static class RuntimeSpriteFactory
{
    private static Sprite circleSprite;
    private static Sprite squareSprite;

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
        texture.wrapMode = TextureWrapMode.Clamp;

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

    public static Sprite GetSquareSprite()
    {
        if (squareSprite != null)
        {
            return squareSprite;
        }

        const int size = 16;
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color solid = Color.white;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, solid);
            }
        }

        texture.Apply();
        squareSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        squareSprite.hideFlags = HideFlags.HideAndDontSave;
        return squareSprite;
    }
}
