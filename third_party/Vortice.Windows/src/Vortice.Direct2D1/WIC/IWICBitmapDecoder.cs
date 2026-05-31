// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using SharpGen.Runtime.Win32;

namespace Vortice.WIC;

public partial class IWICBitmapDecoder
{
    internal IWICStream? _wicStream;

    protected override void DisposeCore(IntPtr nativePointer, bool disposing)
    {
        base.DisposeCore(nativePointer, disposing);

        DisposeWICStreamProxy();
    }

    private void DisposeWICStreamProxy()
    {
        if (_wicStream != null)
        {
            _wicStream.Dispose();
            _wicStream = null;
        }
    }

    /// <summary>
    /// Initializes the decoder with the provided stream.
    /// </summary>
    /// <param name="stream">The stream to use for initialization.</param>
    /// <param name="cacheOptions">The cache options.</param>
    /// <returns>If the method succeeds, it returns <see cref="Result.Ok"/>. Otherwise, it throws an exception.</returns>
    /// <unmanaged>HRESULT IWICBitmapDecoder::Initialize([In, Optional] IStream* pIStream,[In] WICDecodeOptions cacheOptions)</unmanaged>
    public void Initialize(IStream stream, DecodeOptions cacheOptions)
    {
        if (this._wicStream != null)
            throw new InvalidOperationException("This instance is already initialized with an existing stream");

        Initialize_(stream, cacheOptions);
    }

    /// <summary>
    /// Get the <see cref="IWICColorContext"/> of the image (if any)
    /// </summary>
    /// <param name="imagingFactory">The factory for creating new color contexts</param>
    /// <param name="colorContexts">The color context array, or null</param>
    /// <remarks>
    /// When the image format does not support color contexts,
    /// <see cref="ResultCode.UnsupportedOperation"/> is returned.
    /// </remarks>
    /// <unmanaged>HRESULT IWICBitmapDecoder::GetColorContexts([In] unsigned int cCount,[Out, Buffer, Optional] IWICColorContext** ppIColorContexts,[Out] unsigned int* pcActualCount)</unmanaged>	
    public Result TryGetColorContexts(IWICImagingFactory imagingFactory, out IWICColorContext[]? colorContexts)
    {
        return ColorContextsHelper.TryGetColorContexts(GetColorContexts, imagingFactory, out colorContexts);
    }

    /// <summary>
    /// Get the <see cref="IWICColorContext"/> of the image (if any)
    /// </summary>
    /// <returns>
    /// null if the decoder does not support color contexts;
    /// otherwise an array of zero or more ColorContext objects
    /// </returns>
    /// <unmanaged>HRESULT IWICBitmapDecoder::GetColorContexts([In] unsigned int cCount,[Out, Buffer, Optional] IWICColorContext** ppIColorContexts,[Out] </unmanaged>
    public IWICColorContext[] TryGetColorContexts(IWICImagingFactory imagingFactory)
    {
        return ColorContextsHelper.TryGetColorContexts(GetColorContexts, imagingFactory);
    }
}
