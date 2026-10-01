using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KinokoTAS.Core;

namespace KinokoTAS.App.Services;

internal sealed class GamePreviewPresenter(Image image) : IDisposable
{
    private WriteableBitmap? _bitmap;

    public long Completed { get; private set; } = -1;

    public void Reset() => Completed = -1;

    public void Show(PreviewFrame? frame)
    {
        if (frame is null || frame.Completed == Completed)
            return;

        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            image.Source = _bitmap;
        }

        using (var buffer = _bitmap.Lock())
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * frame.Width * 4, buffer.Address + row * buffer.RowBytes,
                    frame.Width * 4);

        Completed = frame.Completed;
        image.InvalidateVisual();
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        image.Source = null;
        GC.SuppressFinalize(this);
    }
}
