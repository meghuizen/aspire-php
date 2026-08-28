<?php

declare(strict_types=1);

namespace Meghuizen\AspireAzure\Tests;

use Meghuizen\AspireAzure\Identity;
use Meghuizen\AspireAzure\IdentityException;
use Meghuizen\AspireAzure\TokenCache;
use PHPUnit\Framework\TestCase;

/**
 * These cover the parts that can be checked without Azure: the cache's expiry arithmetic, and the failure
 * messages. Whether the token actually authenticates against a database can only be answered by a real
 * deployment, and is not claimed here.
 */
final class IdentityTest extends TestCase
{
    protected function setUp(): void
    {
        TokenCache::clear();

        putenv('IDENTITY_ENDPOINT');
        putenv('IDENTITY_HEADER');
        putenv('AZURE_ACCESS_TOKEN');
        putenv('AZURE_KEYVAULT_URI');
    }

    public function testExplainsItselfWhenThereIsNoManagedIdentity(): void
    {
        // The two common failures -- running outside Azure, and an identity with no role assignment -- look
        // the same from the application's side unless the message says which it is.
        $this->expectException(IdentityException::class);
        $this->expectExceptionMessageMatches('/IDENTITY_ENDPOINT/');

        Identity::token(Identity::DATABASE_AUDIENCE);
    }

    public function testNamesTheLocalDevelopmentEscapeHatch(): void
    {
        $this->expectException(IdentityException::class);
        $this->expectExceptionMessageMatches('/AZURE_ACCESS_TOKEN/');

        Identity::token(Identity::DATABASE_AUDIENCE);
    }

    public function testUsesASuppliedTokenWithoutCallingAzure(): void
    {
        putenv('AZURE_ACCESS_TOKEN=local-development-token');

        self::assertSame('local-development-token', Identity::token(Identity::DATABASE_AUDIENCE));
    }

    public function testSecretRefusesWithoutAVault(): void
    {
        $this->expectException(IdentityException::class);
        $this->expectExceptionMessageMatches('/AZURE_KEYVAULT_URI/');

        Identity::secret('app-key');
    }

    public function testCacheReturnsATokenThatIsStillFresh(): void
    {
        TokenCache::put('key', 'token', time() + 3600);

        self::assertSame('token', TokenCache::get('key'));
    }

    public function testCacheRefreshesBeforeExpiry(): void
    {
        // Fetched 50 minutes ago with a 60 minute life: past four fifths, so it must be refetched rather than
        // handed out to expire in the middle of a request.
        $now = time();
        TokenCache::put('key', 'token', $now + 600, $now - 3000);

        self::assertNull(TokenCache::get('key'), 'a token inside its last fifth should be refetched');
    }

    public function testCacheKeepsATokenThatIsNotYetInItsLastFifth(): void
    {
        // Fetched 30 minutes ago with a 60 minute life: half used, so still good.
        $now = time();
        TokenCache::put('key', 'token', $now + 1800, $now - 1800);

        self::assertSame('token', TokenCache::get('key'));
    }

    public function testCacheTreatsAnAlreadyExpiredTokenAsAMiss(): void
    {
        TokenCache::put('key', 'token', time() - 1);

        self::assertNull(TokenCache::get('key'));
    }

    public function testCacheMissIsNull(): void
    {
        self::assertNull(TokenCache::get('never-stored'));
    }

    public function testTheDatabaseAudienceIsTheOneAzureRequires(): void
    {
        // Microsoft has said tokens for other audiences will stop being accepted.
        self::assertSame('https://ossrdbms-aad.database.windows.net', Identity::DATABASE_AUDIENCE);
    }
}
