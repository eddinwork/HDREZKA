using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HDREZKA.Core.Api;

/// <summary>
/// Проверка Authenticode-подписи файла через WinVerifyTrust: true только если
/// подпись есть, она валидна и цепочка сертификатов доверенная.
/// Нужна перед запуском скачанного установщика обновления.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AuthenticodeVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00000010;

    public static bool IsTrusted(string? filePath) => Verify(filePath) == 0;

    /// <summary>
    /// Возвращает HRESULT WinVerifyTrust (0 = подпись валидна и доверенная,
    /// иначе — код ошибки; -1 = файла нет). Код полезен в логах обновления.
    /// </summary>
    public static int Verify(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return -1;

        IntPtr fileInfoPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = 0,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WtdCacheOnlyUrlRetrieval,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero,
            };

            var action = WinTrustActionGenericVerifyV2;
            return unchecked((int)WinVerifyTrust(IntPtr.Zero, action, ref trustData));
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(fileInfoPtr); } catch { }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        ref WinTrustData pWVTData);
}
