using System.Collections.Generic;
using UnityEngine;

namespace YourFinalOrder.Core
{
    /// <summary>Процедурные текстуры прицелов (общие для меню и HUD).</summary>
    public static class CrosshairTextures
    {
        public const int Size = 48;
        static readonly Dictionary<CrosshairStyle, Texture2D> cache = new();

        public static Texture2D Get(CrosshairStyle style)
        {
            if (cache.TryGetValue(style, out var tex) && tex != null) return tex;
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[Size * Size];
            float c = (Size - 1) * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = style switch
                {
                    CrosshairStyle.Dot => Mathf.Clamp01(2.6f - r),
                    CrosshairStyle.Cross => Cross(dx, dy),
                    CrosshairStyle.Circle => Mathf.Clamp01(1.4f - Mathf.Abs(r - 9f)) + Mathf.Clamp01(1.6f - r),
                    CrosshairStyle.Brackets => Brackets(dx, dy),
                    _ => 0f,
                };
                // тёмная обводка для читаемости на светлом
                px[y * Size + x] = new Color(0.93f, 0.9f, 0.84f, Mathf.Clamp01(a) * 0.9f);
            }
            tex.SetPixels(px);
            tex.Apply();
            cache[style] = tex;
            return tex;
        }

        static float Cross(float dx, float dy)
        {
            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            float h = ay < 1.1f && ax > 3f && ax < 11f ? 1f : 0f;
            float v = ax < 1.1f && ay > 3f && ay < 11f ? 1f : 0f;
            return Mathf.Max(h, v);
        }

        static float Brackets(float dx, float dy)
        {
            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            bool vertical = ax > 10f && ax < 12f && ay < 7f;
            bool top = ay > 5.5f && ay < 7.5f && ax > 7f && ax < 12f;
            bool dot = ax < 1.2f && ay < 1.2f;
            return vertical || top || dot ? 1f : 0f;
        }
    }
}
