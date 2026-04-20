using Microsoft.Identity.Client;

namespace UploadCli.Services;

/// <summary>
/// Acquires OAuth2 access tokens from Entra ID using the client-credentials
/// flow (application identity, no user interaction required).
///
/// Tokens are cached in the <see cref="IConfidentialClientApplication"/>
/// instance; MSAL will transparently refresh them before they expire.
/// </summary>
public sealed class BcAuthService
{
    private readonly IConfidentialClientApplication _app;
    private readonly string[] _scopes;

    public BcAuthService(string tenantId, string clientId, string clientSecret, string scope)
    {
        _scopes = [scope];
        _app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();
    }

    /// <summary>
    /// Returns a valid Bearer token, using the MSAL token cache where possible.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var result = await _app
            .AcquireTokenForClient(_scopes)
            .ExecuteAsync(ct)
            .ConfigureAwait(false);

        return result.AccessToken;
    }
}
