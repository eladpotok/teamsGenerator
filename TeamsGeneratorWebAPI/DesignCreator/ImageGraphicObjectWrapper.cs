using SkiaSharp;

namespace TeamsGeneratorWebAPI.DesignCreator
{
    public class ImageGraphicObjectWrapper
    {
        private bool _isRtl;
        private string _text;
        private SKPaint _paint;

        public ImageGraphicObjectWrapper(string text, SKPaint paint)
        {
            _paint = paint;
            _text = Helpers.ReverseIfNeeded(text);
            _isRtl = Helpers.IsRightToLeft(text);
        }

        public void Draw(float x, float y, SKCanvas canvas, bool fromRightToLeftOrientation = false)
        {
            float xOffset = x;
            if(fromRightToLeftOrientation)
            {
                var textWidth = _paint.MeasureText(_text);
                xOffset = xOffset - textWidth;
            }

            canvas.DrawText(_text, xOffset, y, _paint);
        }

    }
}
