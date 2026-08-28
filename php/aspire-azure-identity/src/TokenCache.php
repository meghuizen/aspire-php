<?php

declare(strict_types=1);

namespace Meghuizen\AspireAzure;

/**
 * Keeps access tokens for as long as they are good for.
 *
 * Tokens for Azure Database live between five and sixty minutes, so fetching one when the container starts
 * looks correct and then fails within the hour. Everything here exists to make sure that does not happen.
 *
 * APCu is the store when it is available, because it is shared across requests in one PHP-FPM pool and across
 * requests in a FrankenPHP worker, which is where the saving is. A file under the system temporary directory
 * is the fallback, and is still better than fetching on every request.
 *
 * @internal
 */
final class TokenCache
{
    /**
     * Refresh once four fifths of the token's life has passed.
     *
     * Not at expiry. A token that expires between being read and being used produces an authentication
     * failure in the middle of a request, and the whole point is that this never surfaces to the application.
     */
    private const REFRESH_AT = 0.8;

    /** @var array<string, array{token: string, expires: int, fetched: int}> */
    private static array $memory = [];

    /**
     * Returns a cached token, or null when there is none worth using.
     */
    public static function get(string $key): ?string
    {
        $entry = self::$memory[$key] ?? self::read($key);

        if ($entry === null) {
            return null;
        }

        $lifetime = $entry['expires'] - $entry['fetched'];

        // A non-positive lifetime means the entry was written wrong. Treat it as a miss rather than trusting
        // arithmetic on it.
        if ($lifetime <= 0 || time() >= $entry['fetched'] + (int) ($lifetime * self::REFRESH_AT)) {
            return null;
        }

        self::$memory[$key] = $entry;

        return $entry['token'];
    }

    /**
     * Stores a token and the moment it expires.
     *
     * @param int|null $fetchedAt When the token was obtained. Defaults to now, and exists so the refresh
     *                            threshold -- the one piece of arithmetic here that can be wrong without
     *                            anything failing until an hour into production -- can be tested.
     */
    public static function put(string $key, string $token, int $expiresOn, ?int $fetchedAt = null): void
    {
        $entry = ['token' => $token, 'expires' => $expiresOn, 'fetched' => $fetchedAt ?? time()];

        self::$memory[$key] = $entry;

        // Two requests can refresh at the same moment. Both tokens are valid and one overwrites the other, so
        // this is left unlocked deliberately: a lock would add a failure mode to avoid a harmless race.
        if (\function_exists('apcu_store') && \ini_get('apc.enabled') !== '0') {
            \apcu_store(self::prefix($key), $entry, max(1, $expiresOn - time()));

            return;
        }

        $path = self::path($key);
        $encoded = json_encode($entry, JSON_THROW_ON_ERROR);

        // Written to a neighbouring file and renamed, so a concurrent reader never sees half a token.
        // 0600 because the file holds a bearer credential.
        $temporary = $path . '.' . getmypid();

        if (@file_put_contents($temporary, $encoded, LOCK_EX) !== false) {
            @chmod($temporary, 0600);
            @rename($temporary, $path);
        }
    }

    /**
     * Forgets everything. Used by the tests.
     */
    public static function clear(): void
    {
        self::$memory = [];
    }

    /**
     * @return array{token: string, expires: int, fetched: int}|null
     */
    private static function read(string $key): ?array
    {
        if (\function_exists('apcu_fetch') && \ini_get('apc.enabled') !== '0') {
            $entry = \apcu_fetch(self::prefix($key), $success);

            return $success && self::isWellFormed($entry) ? $entry : null;
        }

        $contents = @file_get_contents(self::path($key));

        if ($contents === false) {
            return null;
        }

        try {
            $entry = json_decode($contents, true, 512, JSON_THROW_ON_ERROR);
        } catch (\JsonException) {
            // A truncated or corrupt cache file is a cache miss, not an error. Fetching again is cheap and
            // always correct.
            return null;
        }

        return self::isWellFormed($entry) ? $entry : null;
    }

    private static function isWellFormed(mixed $entry): bool
    {
        return is_array($entry)
            && isset($entry['token'], $entry['expires'], $entry['fetched'])
            && is_string($entry['token'])
            && is_int($entry['expires'])
            && is_int($entry['fetched']);
    }

    private static function prefix(string $key): string
    {
        return 'meghuizen.aspire-azure.' . $key;
    }

    private static function path(string $key): string
    {
        // Hashed rather than used directly: the key contains a URL, and the resulting name has to be a legal
        // filename on every platform.
        return sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'aspire-azure-' . sha1($key) . '.json';
    }
}
