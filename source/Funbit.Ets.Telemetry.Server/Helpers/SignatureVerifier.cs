using System;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Funbit.Ets.Telemetry.Server.Helpers
{
    /// <summary>
    /// Verifies an Authenticode-signed binary against pinned signer identity.
    ///
    /// Three layers, all must pass:
    ///   1. WinVerifyTrust — Authenticode signature is intact, chain is Windows-trusted, and
    ///      the RFC 3161 timestamp keeps the signature valid past per-signature cert expiry
    ///      (Azure Trusted Signing issues ~3-day certs).
    ///   2. Signer Subject must have an O= RDN exactly equal to the pinned organization.
    ///   3. Chain root cert must have a Thumbprint exactly equal to <see cref="ExpectedRootThumbprint"/>.
    ///
    /// The root thumbprint is hardcoded in this signed binary specifically to defeat the
    /// CurrentUser-trusted-root spoof: a same-user attacker can add a fake root to the
    /// CurrentUser store without admin (Windows permits this), but they cannot produce a
    /// cert chain whose root has Microsoft's actual thumbprint.
    ///
    /// The signer Org is read from App.config so a future organization rename (e.g. "Cocoa
    /// Sandwich LLC" → "Cocoa Sandwich Inc.") can ship as a config-only release. App.config
    /// is bundled inside the signed installer and installed into Program Files, so it is
    /// protected from medium-integrity tampering.
    /// </summary>
    public static class SignatureVerifier
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Microsoft Identity Verification Root Certificate Authority 2020.
        // Captured from release 1.3.1's signed installer chain. Stable for the life of the
        // root cert (typically decades). If Microsoft ever rotates this root, an emergency
        // release will need to ship a new constant — there is no in-band way to update it.
        const string ExpectedRootThumbprint = "F40042E2E5F7E8EF8189FED15519AECE42C3BFA2";

        static readonly string ExpectedSignerOrg =
            ConfigurationManager.AppSettings["UpdateSignerOrgPin"];

        public static SignatureVerificationResult Verify(string filePath)
        {
            if (string.IsNullOrEmpty(ExpectedSignerOrg))
            {
                return Fail("UpdateSignerOrgPin missing from App.config");
            }

            // Layer 1: Authenticode + chain + timestamp via WinVerifyTrust.
            uint trustStatus = WinVerifyTrustEmbeddedSignature(filePath);
            if (trustStatus != 0)
            {
                return Fail($"WinVerifyTrust failed: 0x{trustStatus:X8}");
            }

            // Layer 2: Extract signer cert, exact-match the Organization RDN.
            X509Certificate2 signerCert;
            try
            {
                var raw = X509Certificate.CreateFromSignedFile(filePath);
                signerCert = new X509Certificate2(raw);
            }
            catch (Exception ex)
            {
                return Fail($"Could not extract signer certificate: {ex.Message}");
            }

            using (signerCert)
            {
                string signerSubject = signerCert.Subject;

                if (!SubjectHasExactOrganization(signerCert.SubjectName, ExpectedSignerOrg))
                {
                    return Fail(
                        $"Signer organization mismatch. Expected O='{ExpectedSignerOrg}', got Subject '{signerSubject}'",
                        signerSubject);
                }

                // Layer 3: Chain must be Windows-trusted AND root thumbprint must match.
                // We deliberately do NOT set AllowUnknownCertificateAuthority — without that
                // flag, a self-signed attacker root makes Build return false. We DO ignore
                // time/revocation statuses because Layer 1 already vetted them via the
                // embedded RFC 3161 timestamp.
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags =
                        X509VerificationFlags.IgnoreNotTimeValid |
                        X509VerificationFlags.IgnoreNotTimeNested |
                        X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                        X509VerificationFlags.IgnoreEndRevocationUnknown |
                        X509VerificationFlags.IgnoreCtlNotTimeValid |
                        X509VerificationFlags.IgnoreCtlSignerRevocationUnknown |
                        X509VerificationFlags.IgnoreRootRevocationUnknown;

                    bool chainValid = chain.Build(signerCert);
                    if (!chainValid)
                    {
                        string statuses = string.Join(", ",
                            chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation?.Trim()}"));
                        return Fail($"Chain validation failed: {statuses}", signerSubject);
                    }

                    if (chain.ChainElements.Count == 0)
                    {
                        return Fail("Certificate chain is empty", signerSubject);
                    }

                    var rootCert = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                    if (!string.Equals(rootCert.Thumbprint, ExpectedRootThumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(
                            $"Chain root thumbprint mismatch. Expected '{ExpectedRootThumbprint}', got '{rootCert.Thumbprint}' (root Subject: '{rootCert.Subject}')",
                            signerSubject);
                    }
                }

                return new SignatureVerificationResult
                {
                    IsValid = true,
                    SignerSubject = signerSubject
                };
            }
        }

        /// <summary>
        /// Returns true iff the Subject has at least one O= RDN whose value exactly equals
        /// <paramref name="expectedOrg"/> (case-insensitive). Uses <see cref="X500DistinguishedName.Format(bool)"/>
        /// to render one RDN per line, avoiding the substring-spoofing risk of a raw
        /// Subject string search.
        /// </summary>
        static bool SubjectHasExactOrganization(X500DistinguishedName subject, string expectedOrg)
        {
            string formatted = subject.Format(true);
            // Format(true) yields one RDN per line, e.g.
            //   CN=Cocoa Sandwich LLC
            //   O=Cocoa Sandwich LLC
            //   L=Albuquerque
            //   ...
            foreach (var line in formatted.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring(2).Trim();
                    if (string.Equals(value, expectedOrg, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        static SignatureVerificationResult Fail(string reason, string signerSubject = null)
        {
            Log.WarnFormat("Signature verification failed: {0}{1}", reason,
                signerSubject != null ? $" (signer={signerSubject})" : "");
            return new SignatureVerificationResult
            {
                IsValid = false,
                FailureReason = reason,
                SignerSubject = signerSubject
            };
        }

        #region WinVerifyTrust P/Invoke

        // GUID for generic Authenticode policy: WINTRUST_ACTION_GENERIC_VERIFY_V2.
        static readonly Guid WintrustActionGenericVerifyV2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_WHOLECHAIN = 1;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        const uint WTD_REVOCATION_CHECK_CHAIN = 0x00000040;

        static uint WinVerifyTrustEmbeddedSignature(string filePath)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pInfoUnion = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = null,
                    dwProvFlags = WTD_REVOCATION_CHECK_CHAIN,
                    dwUIContext = 0
                };

                Guid actionGuid = WintrustActionGenericVerifyV2;
                try
                {
                    int result = WinVerifyTrust(IntPtr.Zero, ref actionGuid, ref trustData);
                    return (uint)result;
                }
                finally
                {
                    // Free WinVerifyTrust's internal state. Safe even after a failed verify
                    // or when hWVTStateData == IntPtr.Zero (no state was allocated).
                    trustData.dwStateAction = WTD_STATEACTION_CLOSE;
                    WinVerifyTrust(IntPtr.Zero, ref actionGuid, ref trustData);
                }
            }
            finally
            {
                // FreeHGlobal releases the buffer allocated by AllocHGlobal. DestroyStructure
                // is a no-op for LPWStr fields on .NET Framework (StructureToPtr writes a raw
                // pointer to the managed string's internal buffer, not a separately allocated
                // copy), but kept for symmetry / future-proofing if the struct ever gains a
                // BStr or similar field that does require explicit release.
                Marshal.DestroyStructure(fileInfoPtr, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pInfoUnion;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        #endregion
    }

    public class SignatureVerificationResult
    {
        public bool IsValid { get; set; }
        public string FailureReason { get; set; }
        public string SignerSubject { get; set; }
    }
}
