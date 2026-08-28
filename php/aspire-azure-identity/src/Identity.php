<?php

declare(strict_types=1);

namespace Meghuizen\AspireAzure;

/**
 * Microsoft Entra access tokens for a PHP application running with a managed identity.
 *
 * There is no library to delegate this to. Microsoft's own documentation says so plainly -- "for PHP, there's
 * not a plugin or library for passwordless connections ... the access token can be acquired using Azure REST
 * API" -- and the Azure SDK for PHP was retired in February 2021. So this calls the REST endpoint directly.
 *
 * The endpoint is local to the container. Azure Container Apps and App Service both inject IDENTITY_ENDPOINT
 * and IDENTITY_HEADER, and a GET with the right audience returns a token. Used as a database password, that
 * is what "passwordless" means in practice: the password is a token that lasts an hour at most.
 */
final class Identity
{
    /**
     * The audience a token for Azure Database for MySQL or PostgreSQL must be issued for.
     *
     * Microsoft has said that tokens for other audiences will stop being accepted, so this is not a knob.
     */
    public const DATABASE_AUDIENCE = 'https://ossrdbms-aad.database.windows.net';

    /** The API version the identity endpoint is documented against. */
    private const API_VERSION = '2019-08-01';

    /** Seconds to wait for the identity endpoint, which is on the local network. */
    private const TIMEOUT = 5;

    /**
     * Returns an access token for a resource.
     *
     * @param string      $resource The audience, for example self::DATABASE_AUDIENCE.
     * @param string|null $clientId The user-assigned identity's client ID. Omit for a system-assigned one.
     *
     * @throws IdentityException when no token can be obtained.
     */
    public static function token(string $resource, ?string $clientId = null): string
    {
        $key = $resource . '|' . ($clientId ?? '');

        if (($cached = TokenCache::get($key)) !== null) {
            return $cached;
        }

        // An escape hatch for local development, where there is no identity endpoint. Set it from
        // `az account get-access-token --resource <audience> --query accessToken -o tsv`.
        if (($supplied = getenv('AZURE_ACCESS_TOKEN')) !== false && $supplied !== '') {
            return $supplied;
        }

        [$token, $expiresOn] = self::fetch($resource, $clientId);

        TokenCache::put($key, $token, $expiresOn);

        return $token;
    }

    /**
     * Returns the token to use as the database password.
     *
     * Reads the client ID and audience the AppHost published, so the application does not carry either.
     *
     * @throws IdentityException when no token can be obtained.
     */
    public static function databasePassword(): string
    {
        $audience = self::env('AZURE_DATABASE_TOKEN_AUDIENCE') ?? self::DATABASE_AUDIENCE;

        // Either spelling, because Service Connector sets a different one per database service and an
        // application should not have to know which of the two it is running against.
        $clientId = self::env('AZURE_MYSQL_CLIENTID')
            ?? self::env('AZURE_POSTGRESQL_CLIENTID')
            ?? self::env('AZURE_CLIENT_ID');

        return self::token($audience, $clientId);
    }

    /**
     * Reads a secret from Key Vault using the same identity.
     *
     * @param string      $name     The secret's name.
     * @param string|null $vaultUri The vault URI. Defaults to AZURE_KEYVAULT_URI, which the AppHost publishes.
     *
     * @throws IdentityException when the secret cannot be read.
     */
    public static function secret(string $name, ?string $vaultUri = null): string
    {
        $vault = $vaultUri ?? self::env('AZURE_KEYVAULT_URI');

        if ($vault === null) {
            throw new IdentityException(
                'No Key Vault URI. Pass one, or set AZURE_KEYVAULT_URI -- WithKeyVaultReference in the AppHost '
                . 'sets it for you.'
            );
        }

        $token = self::token('https://vault.azure.net', self::env('AZURE_CLIENT_ID'));

        $url = rtrim($vault, '/') . '/secrets/' . rawurlencode($name) . '?api-version=7.4';

        $body = self::request($url, ['Authorization: Bearer ' . $token]);

        $decoded = self::decode($body, $url);

        if (!isset($decoded['value']) || !is_string($decoded['value'])) {
            throw new IdentityException(
                sprintf('Key Vault returned no value for secret "%s". Response: %s', $name, $body)
            );
        }

        return $decoded['value'];
    }

    /**
     * @return array{0: string, 1: int}
     *
     * @throws IdentityException
     */
    private static function fetch(string $resource, ?string $clientId): array
    {
        $endpoint = self::env('IDENTITY_ENDPOINT');
        $header = self::env('IDENTITY_HEADER');

        if ($endpoint === null || $header === null) {
            throw new IdentityException(
                'No managed identity is available: IDENTITY_ENDPOINT and IDENTITY_HEADER are not set. This code '
                . 'is running somewhere other than Azure Container Apps or App Service. For local development, '
                . 'set AZURE_ACCESS_TOKEN from `az account get-access-token --resource '
                . $resource . ' --query accessToken -o tsv`.'
            );
        }

        $query = [
            'api-version' => self::API_VERSION,
            'resource' => $resource,
        ];

        // A user-assigned identity must be named. Without this, a container with more than one assigned
        // identity cannot tell which to use and the request fails.
        if ($clientId !== null && $clientId !== '') {
            $query['client_id'] = $clientId;
        }

        $url = $endpoint . (str_contains($endpoint, '?') ? '&' : '?') . http_build_query($query);

        $body = self::request($url, [
            'X-IDENTITY-HEADER: ' . $header,
            'Metadata: true',
        ]);

        $decoded = self::decode($body, $url);

        $token = $decoded['access_token'] ?? null;

        if (!is_string($token) || $token === '') {
            throw new IdentityException(
                'The identity endpoint returned no access_token. Response: ' . $body
            );
        }

        // expires_on is documented as a Unix timestamp but has been seen as a string, and on some hosts as a
        // relative number of seconds. Anything that is not a plausible absolute timestamp is treated as
        // relative, which is the safe reading: it can only shorten the cached lifetime.
        $expiresOn = isset($decoded['expires_on']) ? (int) $decoded['expires_on'] : 0;

        if ($expiresOn <= time()) {
            $expiresOn = $expiresOn > 0 ? time() + $expiresOn : time() + 300;
        }

        return [$token, $expiresOn];
    }

    /**
     * @param list<string> $headers
     *
     * @throws IdentityException
     */
    private static function request(string $url, array $headers): string
    {
        if (\function_exists('curl_init')) {
            return self::curl($url, $headers);
        }

        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'header' => implode("\r\n", $headers),
                'timeout' => self::TIMEOUT,
                // So a non-2xx response can be reported with its body, which is where Azure explains itself.
                'ignore_errors' => true,
            ],
        ]);

        $body = @file_get_contents($url, false, $context);

        if ($body === false) {
            throw new IdentityException('Could not reach ' . self::redact($url) . '.');
        }

        // $http_response_header is set by the stream wrapper in the local scope.
        $status = isset($http_response_header[0]) ? self::statusFrom($http_response_header[0]) : 0;

        if ($status !== 0 && ($status < 200 || $status >= 300)) {
            throw new IdentityException(
                sprintf('%s returned HTTP %d: %s', self::redact($url), $status, $body)
            );
        }

        return $body;
    }

    /**
     * @param list<string> $headers
     *
     * @throws IdentityException
     */
    private static function curl(string $url, array $headers): string
    {
        $handle = curl_init($url);

        if ($handle === false) {
            throw new IdentityException('Could not initialise a HTTP request to ' . self::redact($url) . '.');
        }

        curl_setopt_array($handle, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER => $headers,
            CURLOPT_TIMEOUT => self::TIMEOUT,
            CURLOPT_CONNECTTIMEOUT => self::TIMEOUT,
        ]);

        $body = curl_exec($handle);
        $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
        $error = curl_error($handle);

        curl_close($handle);

        if ($body === false) {
            throw new IdentityException(
                sprintf('Could not reach %s: %s', self::redact($url), $error)
            );
        }

        if ($status < 200 || $status >= 300) {
            // The body is included because Azure's errors here are specific and useful -- an identity with no
            // role assignment says so, rather than failing anonymously.
            throw new IdentityException(
                sprintf('%s returned HTTP %d: %s', self::redact($url), $status, (string) $body)
            );
        }

        return (string) $body;
    }

    /**
     * @return array<string, mixed>
     *
     * @throws IdentityException
     */
    private static function decode(string $body, string $url): array
    {
        try {
            $decoded = json_decode($body, true, 512, JSON_THROW_ON_ERROR);
        } catch (\JsonException $exception) {
            throw new IdentityException(
                sprintf('%s returned something that is not JSON: %s', self::redact($url), $body),
                0,
                $exception
            );
        }

        if (!is_array($decoded)) {
            throw new IdentityException(
                sprintf('%s returned JSON that is not an object: %s', self::redact($url), $body)
            );
        }

        return $decoded;
    }

    /**
     * Removes the query string before a URL goes into an exception message.
     *
     * The query carries the client ID, and exception messages end up in logs.
     */
    private static function redact(string $url): string
    {
        $position = strpos($url, '?');

        return $position === false ? $url : substr($url, 0, $position);
    }

    private static function statusFrom(string $statusLine): int
    {
        return preg_match('#HTTP/\S+\s+(\d{3})#', $statusLine, $matches) === 1 ? (int) $matches[1] : 0;
    }

    private static function env(string $name): ?string
    {
        $value = getenv($name);

        return $value === false || $value === '' ? null : $value;
    }
}
