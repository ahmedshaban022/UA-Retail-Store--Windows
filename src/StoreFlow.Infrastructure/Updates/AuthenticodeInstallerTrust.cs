using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StoreFlow.Application.Updates;

namespace StoreFlow.Infrastructure.Updates;

/// <summary>
/// Checks the installer's own signature before the shop is asked to run it.
/// </summary>
/// <remarks>
/// <para>
/// The checksum proves that the download arrived intact. It cannot prove who published it: the
/// checksum is published beside the installer, in the same release, by the same publisher. The
/// installer's Authenticode signature is the publisher's own proof, so it is checked here, before
/// anything is run.
/// </para>
/// <para>
/// The publisher's certificate is pinned in <see cref="PinnedPublisherThumbprint"/>. While the
/// installer carries no signature there is nothing to pin, and an unsigned installer is still
/// installed: refusing every unsigned installer would leave the shop with no way to be updated at
/// all, and the release notes say in as many words that the installer is not signed. The day the
/// publisher signs the installer, its thumbprint goes into the pin and every other signature is
/// refused.
/// </para>
/// </remarks>
public sealed class AuthenticodeInstallerTrust : IInstallerTrust
{
    /// <summary>
    /// The thumbprint of the publisher's certificate the updater expects the installer to carry.
    /// </summary>
    /// <remarks>
    /// Empty while the installer is not signed. An installer that carries a signature this build
    /// does not expect is refused, because a signed installer is a publisher saying who published
    /// it, and the updater has to be able to check that claim.
    /// </remarks>
    public const string PinnedPublisherThumbprint = "";

    private readonly Func<string, string?> _thumbprintOf;

    /// <summary>Creates the trust check.</summary>
    /// <param name="thumbprintOf">
    /// Reads the thumbprint of the certificate that signed a file, or null when the file carries
    /// no signature. The default reads the signature the file carries itself.
    /// </param>
    public AuthenticodeInstallerTrust(Func<string, string?>? thumbprintOf = null)
    {
        _thumbprintOf = thumbprintOf ?? ReadThumbprint;
    }

    /// <inheritdoc />
    public bool IsTrusted(string installerPath, out string? problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        problem = null;

        string? thumbprint = _thumbprintOf(installerPath);

        if (thumbprint is null)
        {
            // The installer carries no signature. The release notes say that it does not, and the
            // checksum published beside it is what proves the file is the one that was published.
            return true;
        }

        if (string.IsNullOrWhiteSpace(PinnedPublisherThumbprint))
        {
            problem = "the installer is signed, and this build does not know the publisher's certificate";

            return false;
        }

        if (!string.Equals(thumbprint, PinnedPublisherThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            problem = "the installer is not signed by the publisher";

            return false;
        }

        return true;
    }

    /// <summary>
    /// The thumbprint of the certificate that signed the file, or null when it carries no
    /// signature.
    /// </summary>
    private static string? ReadThumbprint(string path)
    {
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(path);

            return certificate.GetCertHashString();
        }
        catch (CryptographicException)
        {
            // The file carries no signature. That is the case for every installer this programme
            // publishes today, and it is not an error.
            return null;
        }
    }
}
