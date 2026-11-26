using System;
using System.Runtime.InteropServices;
using WinRT;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Copies a WGC BGRA surface into a CPU-readable staging texture. This is
    /// the fast path behind WgcCaptureEngine; SoftwareBitmap remains the
    /// compatibility fallback when the surface cannot be queried as D3D11.
    /// </summary>
    internal sealed class D3D11Readback : IDisposable
    {
        private static readonly Guid Texture2DGuid =
            new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private IntPtr _staging;
        private int _width;
        private int _height;
        private bool _disposed;

        private D3D11Readback(
            ID3D11Device device,
            ID3D11DeviceContext context,
            IntPtr staging,
            int width,
            int height)
        {
            _device = device;
            _context = context;
            _staging = staging;
            _width = width;
            _height = height;
        }

        public static D3D11Readback? TryCreate(
            IntPtr devicePointer,
            IntPtr contextPointer,
            int width,
            int height)
        {
            if (devicePointer == IntPtr.Zero || contextPointer == IntPtr.Zero ||
                width <= 0 || height <= 0)
            {
                return null;
            }

            ID3D11Device? device = null;
            ID3D11DeviceContext? context = null;
            try
            {
                device = (ID3D11Device)Marshal.GetObjectForIUnknown(devicePointer);
                context = (ID3D11DeviceContext)Marshal.GetObjectForIUnknown(contextPointer);
                var description = new D3D11Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                    SampleCount = 1,
                    SampleQuality = 0,
                    Usage = 3, // D3D11_USAGE_STAGING
                    BindFlags = 0,
                    CPUAccessFlags = 0x20000, // D3D11_CPU_ACCESS_READ
                    MiscFlags = 0
                };

                int hr = device.CreateTexture2D(ref description, IntPtr.Zero, out IntPtr staging);
                if (hr < 0 || staging == IntPtr.Zero)
                {
                    Marshal.ReleaseComObject(context);
                    Marshal.ReleaseComObject(device);
                    return null;
                }

                return new D3D11Readback(device, context, staging, width, height);
            }
            catch
            {
                if (context != null)
                {
                    try { Marshal.ReleaseComObject(context); } catch { }
                }
                if (device != null)
                {
                    try { Marshal.ReleaseComObject(device); } catch { }
                }
                return null;
            }
        }

        public bool TryRead(
            Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface,
            out byte[] pixels,
            out int width,
            out int height)
        {
            pixels = Array.Empty<byte>();
            width = _width;
            height = _height;
            if (_disposed || surface == null || _staging == IntPtr.Zero)
            {
                return false;
            }

            IntPtr source = IntPtr.Zero;
            bool mapped = false;
            try
            {
                var access = surface.As<IDirect3DDxgiInterfaceAccess>();
                Guid iid = Texture2DGuid;
                if (access.GetInterface(ref iid, out source) < 0 || source == IntPtr.Zero)
                {
                    return false;
                }

                _context.CopyResource(_staging, source);
                if (_context.Map(_staging, 0, 1, 0, out D3D11MappedSubresource mappedResource) < 0)
                {
                    return false;
                }

                mapped = true;
                int destinationStride = _width * 4;
                pixels = new byte[destinationStride * _height];
                int sourceStride = (int)mappedResource.RowPitch;
                for (int row = 0; row < _height; row++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mappedResource.Data, row * sourceStride),
                        pixels,
                        row * destinationStride,
                        destinationStride);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (mapped)
                {
                    try
                    {
                        _context.Unmap(_staging, 0);
                    }
                    catch
                    {
                    }
                }
                if (source != IntPtr.Zero)
                {
                    Marshal.Release(source);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_staging != IntPtr.Zero)
            {
                Marshal.Release(_staging);
                _staging = IntPtr.Zero;
            }
            try
            {
                Marshal.ReleaseComObject(_context);
            }
            catch
            {
            }
            try
            {
                Marshal.ReleaseComObject(_device);
            }
            catch
            {
            }
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            [PreserveSig]
            int GetInterface(ref Guid iid, out IntPtr value);
        }

        // ID3D11Device inherits only IUnknown. CreateTexture2D is vtable slot 5.
        [ComImport]
        [Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3D11Device
        {
            void CreateBuffer();
            void CreateTexture1D();
            [PreserveSig]
            int CreateTexture2D(
                ref D3D11Texture2DDescription description,
                IntPtr initialData,
                out IntPtr texture);
        }

        // ID3D11DeviceContext inherits ID3D11DeviceChild (four methods).
        // Map is vtable slot 14 and CopyResource is slot 47.
        [ComImport]
        [Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3D11DeviceContext
        {
            void GetDevice();
            void GetPrivateData();
            void SetPrivateData();
            void SetPrivateDataInterface();
            void VSSetConstantBuffers();
            void PSSetShaderResources();
            void PSSetShader();
            void PSSetSamplers();
            void VSSetShader();
            void DrawIndexed();
            void Draw();
            [PreserveSig]
            int Map(
                IntPtr resource,
                uint subresource,
                int mapType,
                uint flags,
                out D3D11MappedSubresource mappedResource);
            [PreserveSig]
            void Unmap(IntPtr resource, uint subresource);
            void PSSetConstantBuffers();
            void IASetInputLayout();
            void IASetVertexBuffers();
            void IASetIndexBuffer();
            void DrawIndexedInstanced();
            void DrawInstanced();
            void GSSetConstantBuffers();
            void GSSetShader();
            void IASetPrimitiveTopology();
            void VSSetShaderResources();
            void VSSetSamplers();
            void Begin();
            void End();
            void GetData();
            void SetPredication();
            void GSSetShaderResources();
            void GSSetSamplers();
            void OMSetRenderTargets();
            void OMSetRenderTargetsAndUnorderedAccessViews();
            void OMSetBlendState();
            void OMSetDepthStencilState();
            void SOSetTargets();
            void DrawAuto();
            void DrawIndexedInstancedIndirect();
            void DrawInstancedIndirect();
            void Dispatch();
            void DispatchIndirect();
            void RSSetState();
            void RSSetViewports();
            void RSSetScissorRects();
            void CopySubresourceRegion();
            [PreserveSig]
            void CopyResource(IntPtr destination, IntPtr source);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11Texture2DDescription
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public int Format;
            public uint SampleCount;
            public uint SampleQuality;
            public int Usage;
            public int BindFlags;
            public int CPUAccessFlags;
            public int MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11MappedSubresource
        {
            public IntPtr Data;
            public uint RowPitch;
            public uint DepthPitch;
        }
    }
}
