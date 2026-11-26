using System;
using System.Runtime.InteropServices;

namespace CryoUtils.Services
{
    internal static class DpapiService
    {
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] data)
        {
            if (data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            IntPtr input = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, input, data.Length);
                var inputBlob = new DataBlob { Size = data.Length, Data = input };
                if (!CryptProtectData(
                        ref inputBlob,
                        "CryoUtils clipboard history",
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out var outputBlob))
                {
                    throw new InvalidOperationException(
                        $"DPAPI protection failed ({Marshal.GetLastWin32Error()}).");
                }

                try
                {
                    byte[] output = new byte[outputBlob.Size];
                    Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
                    return output;
                }
                finally
                {
                    LocalFree(outputBlob.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input);
            }
        }

        public static byte[] Unprotect(byte[] data)
        {
            if (data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            IntPtr input = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, input, data.Length);
                var inputBlob = new DataBlob { Size = data.Length, Data = input };
                if (!CryptUnprotectData(
                        ref inputBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out var outputBlob))
                {
                    throw new InvalidOperationException(
                        $"DPAPI unprotection failed ({Marshal.GetLastWin32Error()}).");
                }

                try
                {
                    byte[] output = new byte[outputBlob.Size];
                    Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
                    return output;
                }
                finally
                {
                    LocalFree(outputBlob.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input);
            }
        }
    }
}
