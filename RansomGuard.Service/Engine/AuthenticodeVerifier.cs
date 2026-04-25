using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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
        private static readonly Guid DRIVER_ACTION_VERIFY = new Guid("{F750E6C3-38EE-11d1-85E5-00C04FC295EE}");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, WinTrustData pWVTData);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminAcquireContext(out IntPtr phCatAdmin, IntPtr pgSubsystem, uint dwFlags);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminCalcHashFromFileHandle(IntPtr hFile, ref uint pcbHash, IntPtr pbHash, uint dwFlags);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, IntPtr pbHash, uint cbHash, uint dwFlags, ref IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CATALOG_INFO psCatInfo, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CATALOG_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszCatalogFile;
        }

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
                // Try embedded signature first
                using var cert = X509Certificate.CreateFromSignedFile(filePath);
                if (cert != null)
                {
                    var cert2 = new X509Certificate2(cert);
                    return ExtractCommonName(cert2.SubjectName.Name);
                }
            }
            catch
            {
                // Embedded signature failed, try catalog signature
            }

            // Try catalog signature
            try
            {
                string? catalogPublisher = GetCatalogPublisher(filePath);
                if (!string.IsNullOrEmpty(catalogPublisher))
                {
                    return catalogPublisher;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthenticodeVerifier] Catalog verification error for {filePath}: {ex.Message}");
            }

            // Fallback for catalog-signed files where we can't extract publisher
            return "System Signed (Catalog)";
        }

        /// <summary>
        /// Attempts to get the publisher from a catalog-signed file.
        /// Returns null if the file is not catalog-signed or if publisher cannot be determined.
        /// </summary>
        private string? GetCatalogPublisher(string filePath)
        {
            IntPtr hCatAdmin = IntPtr.Zero;
            IntPtr hFile = INVALID_HANDLE_VALUE;
            IntPtr hashPtr = IntPtr.Zero;
            IntPtr hCatInfo = IntPtr.Zero;

            try
            {
                // Acquire catalog admin context
                if (!CryptCATAdminAcquireContext(out hCatAdmin, IntPtr.Zero, 0))
                {
                    return null;
                }

                // Open the file
                hFile = CreateFile(filePath, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                if (hFile == INVALID_HANDLE_VALUE)
                {
                    return null;
                }

                // Calculate hash size
                uint hashSize = 0;
                if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref hashSize, IntPtr.Zero, 0))
                {
                    return null;
                }

                // Allocate hash buffer
                hashPtr = Marshal.AllocHGlobal((int)hashSize);
                if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref hashSize, hashPtr, 0))
                {
                    return null;
                }

                // Find catalog containing this file's hash
                IntPtr prevCatInfo = IntPtr.Zero;
                hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, hashPtr, hashSize, 0, ref prevCatInfo);
                
                if (hCatInfo == IntPtr.Zero)
                {
                    return null; // Not catalog-signed
                }

                // Get catalog file path
                CATALOG_INFO catInfo = new CATALOG_INFO();
                catInfo.cbStruct = (uint)Marshal.SizeOf(typeof(CATALOG_INFO));
                
                if (!CryptCATCatalogInfoFromContext(hCatInfo, ref catInfo, 0))
                {
                    return null;
                }

                // Extract publisher from catalog file
                if (!string.IsNullOrEmpty(catInfo.wszCatalogFile) && File.Exists(catInfo.wszCatalogFile))
                {
                    try
                    {
                        using var catalogCert = X509Certificate.CreateFromSignedFile(catInfo.wszCatalogFile);
                        if (catalogCert != null)
                        {
                            var cert2 = new X509Certificate2(catalogCert);
                            return ExtractCommonName(cert2.SubjectName.Name);
                        }
                    }
                    catch
                    {
                        // Failed to read catalog certificate
                    }
                }

                return "System Signed (Catalog)";
            }
            finally
            {
                // Cleanup resources
                if (hCatInfo != IntPtr.Zero && hCatAdmin != IntPtr.Zero)
                {
                    CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
                }
                if (hashPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(hashPtr);
                }
                if (hFile != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(hFile);
                }
                if (hCatAdmin != IntPtr.Zero)
                {
                    CryptCATAdminReleaseContext(hCatAdmin, 0);
                }
            }
        }

        /// <summary>
        /// Extracts the Common Name (CN) from a certificate subject name.
        /// Example: "CN=Microsoft Corporation, O=Microsoft Corporation, ..." -> "Microsoft Corporation"
        /// </summary>
        private string ExtractCommonName(string subjectName)
        {
            if (string.IsNullOrEmpty(subjectName))
                return subjectName;

            // Look for CN= in the subject name
            int cnIndex = subjectName.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (cnIndex == -1)
                return subjectName;

            int startIndex = cnIndex + 3; // Skip "CN="
            int endIndex = subjectName.IndexOf(',', startIndex);
            
            if (endIndex == -1)
                return subjectName.Substring(startIndex).Trim();
            
            return subjectName.Substring(startIndex, endIndex - startIndex).Trim();
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
