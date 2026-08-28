# meghuizen/aspire-azure-identity

Microsoft Entra access tokens for PHP running on Azure Container Apps or App Service. Use a token as a
database password, or read a Key Vault secret, with a managed identity and nothing secret in configuration.

> **Staged for extraction.** This lives in the `aspire-php` repository for now so both sides of the contract
> can be changed together. It is meant to become its own repository and Packagist package before release.
>
> **Not yet run against Azure.** The code is written against Microsoft's documented REST contract, but no
> deployment has exercised it. Do not treat it as proven.

## Why this exists

There is nothing to use instead. Microsoft's own Service Connector documentation says so:

> "For PHP, there's not a plugin or library for passwordless connections. You can get an access token for the
> managed identity or service principal and use it as the password to connect to the database. The access
> token can be acquired using Azure REST API."

The Azure SDK for PHP was retired in February 2021. So this calls the REST endpoint directly, and caches the
result properly, which is the part that is easy to get wrong.

## Install

```bash
composer require meghuizen/aspire-azure-identity
```

## Use

The AppHost publishes the client ID and audience, so nothing here is hard-coded in your application.

**Laravel** — `config/database.php`:

```php
'password' => \Meghuizen\AspireAzure\Identity::databasePassword(),
```

**WordPress** — `wp-config.php`:

```php
define('DB_PASSWORD', \Meghuizen\AspireAzure\Identity::databasePassword());
```

**Symfony** is the awkward one, because `DATABASE_URL` is a single string in the environment and there is
nowhere in it to call a function. Override the password on the connection instead, in
`config/packages/doctrine.yaml`:

```yaml
doctrine:
    dbal:
        host: '%env(AZURE_POSTGRESQL_HOST)%'
        dbname: '%env(AZURE_POSTGRESQL_DATABASE)%'
        user: '%env(AZURE_POSTGRESQL_USERNAME)%'
        password: '%env(azure_token:AZURE_DATABASE_TOKEN_AUDIENCE)%'
```

with a small env var processor calling `Identity::databasePassword()`. No bundle is shipped for this
deliberately: a bundle means owning Symfony integration, and this package is meant to stay small enough to
read in one sitting.

**A Key Vault secret**:

```php
$appKey = \Meghuizen\AspireAzure\Identity::secret('app-key');
```

## Caching

Tokens for Azure Database are valid for between five and sixty minutes. Fetching one when the container starts
looks correct and then fails within the hour, so the cache refreshes at four fifths of each token's life —
early enough that a token never expires between being read and being used.

APCu is the store when available, which is the case in any image this package's AppHost builds. A `0600` file
under the system temporary directory is the fallback.

Two requests can refresh at the same moment. Both tokens are valid and one overwrites the other, so this is
left unlocked on purpose: a lock would add a failure mode to avoid a harmless race.

## Local development

There is no identity endpoint outside Azure. Set a token yourself:

```bash
export AZURE_ACCESS_TOKEN=$(az account get-access-token \
  --resource https://ossrdbms-aad.database.windows.net --query accessToken -o tsv)
```

## Environment

Set by `WithAzureIdentity` and `WithKeyVaultReference` in the AppHost.

| Variable | Meaning |
|---|---|
| `IDENTITY_ENDPOINT`, `IDENTITY_HEADER` | Injected by Azure. The local token endpoint |
| `AZURE_CLIENT_ID` | The user-assigned identity to request a token for |
| `AZURE_MYSQL_CLIENTID`, `AZURE_POSTGRESQL_CLIENTID` | Service Connector's per-service spelling of the same thing |
| `AZURE_DATABASE_TOKEN_AUDIENCE` | The audience for a database token |
| `AZURE_KEYVAULT_URI` | The vault `secret()` reads from |
| `AZURE_ACCESS_TOKEN` | Local development override, checked first |

## Licence

MIT.
