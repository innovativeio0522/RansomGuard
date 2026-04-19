using System;

namespace RansomGuard.Service.Engine
{
    public interface IAuthenticodeVerifier
    {
        /// <summary>
        /// Checks if the specified file has a valid digital signature.
        /// Handles both embedded signatures and catalog-signed system files.
        /// </summary>
        bool IsSigned(string filePath);

        /// <summary>
        /// Retrieves the common name (Subject) of the signing certificate.
        /// </summary>
        string GetPublisher(string filePath);

        /// <summary>
        /// Returns true if the file is signed by a trusted Microsoft authority.
        /// </summary>
        bool IsMicrosoftSigned(string filePath);
    }
}
