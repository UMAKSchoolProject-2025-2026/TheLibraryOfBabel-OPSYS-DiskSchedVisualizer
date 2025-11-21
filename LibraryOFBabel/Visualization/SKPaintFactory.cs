using SkiaSharp;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Central factory for creating SKPaint instances with consistent settings (antialiasing on).
    /// Callers are responsible for disposing the returned SKPaint when no longer needed.
    /// </summary>
    public static class SKPaintFactory
    {
        public static SKPaint CreateFill(SKColor color)
            => new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

        public static SKPaint CreateStroke(SKColor color, float strokeWidth)
            => new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };

        public static SKPaint CreateStroke(SKColor color, float strokeWidth, SKStrokeCap cap)
            => new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, StrokeCap = cap };

        public static SKPaint CreateFillWithAlpha(SKColor color, byte alpha)
            => new SKPaint { Color = color.WithAlpha(alpha), IsAntialias = true, Style = SKPaintStyle.Fill };
    }
}
