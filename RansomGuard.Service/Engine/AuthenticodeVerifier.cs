using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Provides methods to verify the Authenticode digital signatures of files using WinVerifyTrust.
    /// This handles both embedded signatures and catalog-signed Windows system files.
    /// </summary>
    internal class AuthenticodeVerifier : IAuthenticodeVerifier
    {
        #region Win32 P/Invoke Definitions

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, WinTrustData pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WinTrustData
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public uint dwUIChoice = 2; // WTD_UI_NONE
            public uint fdwRevocationChecks = 0; // WTD_REVOCATION_NONE
            public uint dwUnionChoice = 1; // WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction = 0; // WTD_STATEACTION_IGNORE
            public IntPtr hWVTStateData = IntPtr.Zero;
            public string? pwszURLReference = null;
            public uint dwProvFlags = 0x00000040; // WTD_CACHE_ONLY_URL_RETRIEVAL
            public uint dwUIContext = 0;
            public IntPtr pSignatureSettings = IntPtr.Zero;

            public WinTrustData(WinTrustFileInfo fileInfo)
            {
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, pFile, false);
            }

            ~WinTrustData()
            {
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;

            public WinTrustFileInfo(string filePath)
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                pcwszFilePath = filePath;
                hFile = IntPtr.Zero;
                pgKnownSubject = IntPtr.Zero;
            }
        }

        #endregion

        public bool IsSigned(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                var fileInfo = new WinTrustFileInfo(filePath);
                var data = new WinTrustData(fileInfo);
                uint result = WinVerifyTrust(new IntPtr(-1), WINTRUST_ACTION_GENERIC_VERIFY_V2, data);
                return result == 0; // ERROR_SUCCESS
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthenticodeVerifier] Error verifying {filePath}: {ex.Message}");
                return false;
            }
        }

        public string GetPublisher(string filePath)
        {
            if (!IsSigned(filePath)) return "Unsigned";

            try
            {
                // Note: X509Certificate.CreateFromSignedFile only works for embedded signatures.
                // For catalog-signed files, this may return null or throw.
                using var cert = X509Certificate.CreateFromSignedFile(filePath);
                if (cert == null) return "Unknown (Catalog Signed)";
                
                var cert2 = new X509Certificate2(cert);
                return cert2.SubjectName.Name;
            }
            catch
            {
                // Fallback for catalog-signed files or read errors
                return "System Signed";
            }
        }

        public bool IsMicrosoftSigned(string filePath)
        {
            if (!IsSigned(filePath)) return false;

            string publisher = GetPublisher(filePath).ToLowerInvariant();
            
            // Check for standard Microsoft publisher strings
            return publisher.Contains("microsoft corporation") || 
                   publisher.Contains("microsoft windows") || 
                   publisher.Contains("microsoft official") ||
                   publisher == "system signed"; // Assume catalog-signed system files are Microsoft if WinVerifyTrust says OK
        }
    }
}
