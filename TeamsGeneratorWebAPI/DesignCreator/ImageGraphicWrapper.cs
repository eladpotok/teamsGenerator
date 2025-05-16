using SkiaSharp;

namespace TeamsGeneratorWebAPI.DesignCreator
{
    public class ImageGraphicWrapper
    {
        private SKSurface _surface;
        private SKBitmap _backgroundBitmap;

        public float Width { get; set; }
        public float Height { get; set; }

        public float HorizontalMiddle => Width /2;
        public float VerticalMiddle => Height /2;

        public ImageGraphicWrapper(FileStream templateFile)
        {
            _backgroundBitmap = SKBitmap.Decode(templateFile);
            _surface = SKSurface.Create(new SKImageInfo(_backgroundBitmap.Width, _backgroundBitmap.Height));
            Width = _backgroundBitmap.Width;
            Height = _backgroundBitmap.Height;
        }

        public void Draw(float x, float y, ImageGraphicObjectWrapper imageGraphicObjectWrapper, bool fromRightToLeftOrientation = false)
        {
            imageGraphicObjectWrapper.Draw(x, y, _surface.Canvas, fromRightToLeftOrientation);
        }

        internal void DrawCanvas()
        {
            _surface.Canvas.DrawBitmap(_backgroundBitmap, SKPoint.Empty);
        }

        public MemoryStream Save()
        {
            using (var image = _surface.Snapshot())
            using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var ms = new MemoryStream())
            {
                png.SaveTo(ms);
                return ms;
            }
        }
    }
}
