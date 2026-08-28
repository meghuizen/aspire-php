# Azure Container Apps support for PHP workloads

Status: design, not yet implemented
Date: 2026-08-28

## Summary

`aspire publish` already produces a container that Azure Container Apps will run. Aspire turns a
`PublishAsDockerFile` resource into a Container App without help from this package. So this work is not
"add Azure Container Apps". It is closing the gaps that a generic container platform cannot close for a
PHP application, and then adding the Azure-specific pieces on top.

The work splits in two, and the split matters because half of it is not about Azure at all:

- **Part A**, in the core package, fixes things that are wrong on every deployment target. Console
  resources that never publish, no awareness of running behind a reverse proxy, local-file sessions
  under a load balancer, health checks that never reach the target platform, and a telemetry collector
  that loses its reason to exist once the two containers are separated. Docker Compose and Kubernetes
  users get all of this.
- **Part B**, also in the core package but behind opt-in APIs, adds Azure: Container App Jobs, managed
  identity, Key Vault, Blob Storage, and the connection variable names Microsoft's own tooling uses.
- **Part C** is a separate composer package, in its own repository, that lets PHP obtain a Microsoft
  Entra access token. Nothing like it exists.

## Decisions already taken

| Decision | Choice | Reason |
|---|---|---|
| Scope | Full passwordless, including the database | Chosen deliberately over the smaller options |
| C# packaging | One NuGet package | Azure methods are opt-in; .NET does not load an assembly until a method touching it runs, so a Compose-only user never loads `Aspire.Hosting.Azure.*`. It is still downloaded. Splitting later is mechanical |
| PHP shim | Its own repository and Packagist package | Matches how Memcached was split out |
| Shim depth | Token provider only, with a documented one-line config change per framework | Owning Laravel service providers and Symfony bundles means owning framework integration this project has never tested end to end |
| Base image | Keep serversideup | See "Base images" below |

## Base images

We looked at whether Microsoft ships a PHP image better suited to Azure. It does not.

| Image | What it is | Why not |
|---|---|---|
| `mcr.microsoft.com/devcontainers/php:8.5` | A development container. Debian, Apache on 8080, ships the developer toolchain | Development images carry tooling deliberately; production images strip it. Adopting it makes our image larger and less hardened than the one we ship |
| `mcr.microsoft.com/appsvc/php:8.x` | App Service's internal platform runtime | Not offered as a base image. Its public source repository, `Azure-App-Service/nginx-fpm`, is still on PHP 7.0.27 from 2018 |
| `mcr.microsoft.com/appsvc/wordpress-debian-php` | Maintained, but WordPress-only, PHP 8.4 at most | Not a general base |

There is no "Azure compatible" container image, because Container Apps runs any OCI image. What makes a
container behave well there is conduct, not lineage: listen on the declared target port, run as a
non-root user, trust `X-Forwarded-*`, answer a probe path, and hold no state on local disk. The
serversideup images already give us the first two. The rest is Part A.

## Part A: fixes that apply to every target

### A1. Console resources must survive publish

`PhpHostingExtensions.Console.cs:144` returns early unless `ExecutionContext.IsRunMode`. Every console
resource is therefore absent from a published manifest. A deployed application today has no migrations,
no queue worker and no scheduler. The comment explains the original reasoning correctly: a command run
at publish time would run on the machine doing the publish. That argument rules out *executing* the
command, not *emitting a resource that the deployment will execute*.

The fix is to emit the resource in publish mode as a container built from the same image as its parent,
carrying the parent's environment, but never started by the AppHost.

`PhpConsoleCommandKind` gains a third member so the deployment can tell the three shapes apart:

| Kind | Meaning | Run mode | Publish |
|---|---|---|---|
| `OneShot` | Runs once and exits. Migrations, seeds | Parent waits for completion | A resource the deployment triggers once |
| `LongRunning` | Runs until stopped. Queue workers | Starts alongside the parent | A resource with no endpoint |
| `Scheduled` | Runs on a cron. `schedule:run` | Loops internally, once a minute | A resource carrying its cron expression |

`WithScheduler` gains an optional `cron` parameter defaulting to `* * * * *`, which is what Laravel's
scheduler is designed for. In run mode the behaviour is unchanged.

A `PhpConsoleKindAnnotation` records the kind and cron on the resource so Part B can read them without
re-deriving anything.

### A2. PHP must know it is behind a proxy

Nothing in the repository sets a trusted proxy or an HTTPS hint. Container Apps, Kubernetes ingress and
any reverse proxy terminate TLS at the edge and forward plain HTTP. PHP then reports `HTTPS` unset and
builds `http://` URLs, which produces mixed-content warnings, broken asset URLs, and redirect loops
where the framework redirects to HTTP and the platform redirects back to HTTPS.

This is the single most common way a PHP application breaks behind a load balancer, and it is entirely
within this package's remit.

A new `WithTrustedProxies(string? proxies = null)` writes the right thing per convention, defaulting to
trusting all proxies because the container is not reachable except through the platform's ingress:

| Convention | What is set |
|---|---|
| Laravel | `TRUSTED_PROXIES=*` |
| Symfony | `TRUSTED_PROXIES=REMOTE_ADDR`, `TRUSTED_HEADERS=x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-forwarded-host` |
| WordPress, Joomla, Drupal | No environment variable exists. These read `$_SERVER['HTTPS']` directly |

For the three that read `$_SERVER` directly, the generated image gets a small `auto_prepend_file` that
sets `$_SERVER['HTTPS']`, `$_SERVER['SERVER_PORT']` and `$_SERVER['HTTP_HOST']` from the forwarded
headers before any application code runs. This is written only when the resource has a convention that
needs it, and only in publish mode, because in run mode there is no proxy.

Applied automatically in publish mode for web resources. `WithTrustedProxies(proxies: "")` opts out.

### A3. Sessions must survive more than one replica

`Optimization.cs:129` touches `session.serialize_handler` and nothing else, so sessions are files on the
container's local disk. Any deployment with more than one replica and no sticky sessions logs users out
at random, and Container Apps scales by default.

`WithSessionStore(IResourceBuilder<IResourceWithConnectionString> cache)` sets
`session.save_handler=redis` and builds `session.save_path` from the cache's connection properties,
reusing `PhpConnectionMapper` rather than a second code path. It refuses politely, with a clear
exception, if the resource has no Redis-shaped connection.

Not applied automatically. Moving where sessions live is not a decision to make on someone's behalf, and
an application already using a database session driver would break. Instead, publishing a web resource
that has neither a session store nor an explicit opt-out logs a warning naming the risk. `WithDataVolume`
on a session path is a legitimate opt-out for a single-replica deployment.

### A4. Health checks must reach the platform

`Health.cs:52` calls `WithHttpHealthCheck`, which is the dashboard's run-mode check. Aspire has a
separate, experimental probe API — `WithHttpProbe(ProbeType.Startup | Readiness | Liveness, path)` —
that is translated to Container Apps probes, Kubernetes probes and Compose health checks. We emit the
first and not the second, so no deployment target learns anything.

`WithHealthCheck` will call both. This is a deliberate behaviour change: a health check that stops at the
dashboard is half-built.

Defaults, chosen for PHP rather than copied from Aspire's:

| Probe | Settings | Why |
|---|---|---|
| Startup | `initialDelaySeconds: 5`, `periodSeconds: 3`, `failureThreshold: 20` | An image with OPcache preloading and a large autoloader can take tens of seconds on a cold start. A tight startup probe kills the container before it finishes booting |
| Readiness | `periodSeconds: 5`, `timeoutSeconds: 3` | |
| Liveness | `periodSeconds: 30`, `failureThreshold: 3` | Deliberately slack. PHP-FPM under load can be slow to answer without being dead |

The probe APIs are marked experimental and raise `ASPIREPROBES001`, suppressed narrowly at the call site
rather than project-wide, so the day the API changes we get a compiler error rather than silence.

### A5. The collector has to stay local

`PhpTelemetryCollectorResource.cs` documents why the collector exists: PHP has no background thread, so
without a collector on the same host every request pays the export cost inline. Deployment platforms
give each resource its own network identity, which turns that local write into a network hop and removes
the entire justification.

Part A adds a `PhpCollectorColocationAnnotation` recording which application a collector serves. Part B
consumes it for Container Apps. Compose and Kubernetes get documentation for now, not code — the
annotation makes adding them later a small change rather than a redesign.

## Part B: Azure

All of Part B is opt-in. No existing AppHost changes behaviour by upgrading.

### B1. Console resources map to Container App Jobs

Aspire exposes `PublishAsAzureContainerAppJob((infra, job) => ...)` and
`PublishAsScheduledAzureContainerAppJob(cron)` for `ContainerResource` and `ExecutableResource`, which is
what the console resources are. The mapping follows from A1's kinds:

| Kind | Azure shape |
|---|---|
| `OneShot` | Job, `ContainerAppJobTriggerType.Manual`, `ReplicaRetryLimit = 3`, `ReplicaTimeout = 1800` |
| `Scheduled` | `PublishAsScheduledAzureContainerAppJob(cron)` from the annotation |
| `LongRunning` | Container App with no ingress and `minReplicas = 1`, so scale-to-zero does not stop a queue worker |

A single `WithAzureContainerApps()` on the PHP resource walks its console children and applies the right
shape to each, so the caller does not repeat per-child configuration that is already derivable.

Open question, to be settled by building it: whether `PublishAsDockerFile` and
`PublishAsAzureContainerAppJob` compose cleanly on the same resource, given that `PublishAsDockerFile`
substitutes a container resource for the executable. If they conflict, the console resources become
containers directly in publish mode rather than executables, which A1 already allows for.

### B2. An Azure connection convention

Service Connector and every Microsoft PHP tutorial use their own variable names. Adding them as a fourth
`PhpConnectionConvention` costs little and means an application written against Microsoft's
documentation runs under Aspire unchanged:

| Resource | Names |
|---|---|
| MySQL | `AZURE_MYSQL_HOST`, `AZURE_MYSQL_PORT`, `AZURE_MYSQL_DBNAME`, `AZURE_MYSQL_USERNAME`, `AZURE_MYSQL_PASSWORD` |
| PostgreSQL | `AZURE_POSTGRESQL_CONNECTIONSTRING`, plus the individual `AZURE_POSTGRESQL_*` parts |
| Redis | `AZURE_REDIS_HOST`, `AZURE_REDIS_PORT`, `AZURE_REDIS_PASSWORD`, `AZURE_REDIS_DATABASE`, `AZURE_REDIS_SSL` |

Two details Microsoft's tutorial makes explicit and we would otherwise miss. MySQL over TLS needs
`PDO::MYSQL_ATTR_SSL_CA` pointed at `/etc/ssl/certs/ca-certificates.crt`, so the mapper sets
`MYSQL_ATTR_SSL_CA`. Azure Managed Redis is TLS on port 10000, not 6379.

### B3. Managed identity

`WithAzureIdentity(IResourceBuilder<AzureUserAssignedIdentityResource> identity)` assigns a user-assigned
identity to the PHP resource and publishes its client ID into the container as the `*_CLIENTID` variables
Service Connector uses, which is what the Part C shim reads.

Role assignments use Aspire's existing `WithRoleAssignments`, not Service Connector. Service Connector
overlaps with what Aspire already models, and mixing the two means two things believe they own the
identity.

### B4. Key Vault

`WithKeyVaultReference(vault, secretName, environmentVariable)` grants the identity
`KeyVaultBuiltInRole.KeyVaultSecretsUser` and gives the container the vault URI and secret name. The
shim fetches at runtime.

This matters even under a full passwordless remit, because plenty of secrets have no Entra path at all:
third-party API keys, SMTP credentials, a Laravel `APP_KEY`. Microsoft's own flagship PHP tutorial uses
exactly this — Key Vault, not a token — for its database password.

### B5. Blob Storage

`WithBlobStorageReference(storage, containerName)` grants `Storage Blob Data Contributor` and sets the
account and container names. For uploads this is the right answer on Container Apps, and Drupal, Laravel
and WordPress all have well-trodden blob adapters. It is the better alternative to B6, and the
documentation should say so plainly.

### B6. Azure Files, and why it is second choice

Aspire translates both named volumes and bind mounts into Azure Files mounts. `WithDataVolume` therefore
already produces something, but Azure Files is SMB: a fixed uid and gid for the whole mount, no POSIX
ownership, and latency that makes it a poor place for anything read per request.

The uid must match the image's user — 82 for `www-data` on Alpine, 33 on Debian — or the application
cannot write. `WithDataVolume` will set the mount options accordingly, reading the uid from the resolved
base image rather than assuming.

Unverified. This needs a real deployment to confirm, and until it is confirmed the documentation will say
so rather than implying it works.

### B7. Mail

Container Apps blocks outbound port 25, so `WithMailReference` against MailPit has no production
counterpart. `WithSmtp` already covers a real server; the documentation gains an Azure section covering
Azure Communication Services' SMTP relay on port 587, which msmtp and every framework mailer can use
unchanged. No new API.

### B8. The collector as a second container

This is what A5's annotation exists for. `PublishAsAzureContainerApp` exposes `Template.Containers`, and
Container Apps allows several containers in one app sharing localhost. Where a PHP resource has a
colocated collector, the collector's image and configuration are added as a second container on the PHP
application's own Container App rather than published as an app of its own, and
`OTEL_EXPORTER_OTLP_ENDPOINT` points at `localhost`.

If this proves not to work — see the open question — the fallback is a separate Container App with the
endpoint rewritten accordingly, and documentation stating plainly that the export cost is then paid in
the request. That is a worse deployment, not a broken one, so it is an acceptable fallback.

### B9. Ingress

Mostly Aspire's job, but two constraints need to be enforced with a clear error rather than discovered in
a failed deployment: Container Apps allows exactly one external HTTP ingress per app, and HTTP and TCP
cannot share a target port. A PHP resource with two external HTTP endpoint groups fails validation at
publish time with a message naming both endpoints.

## Part C: the PHP shim

Repository `aspire-php-azure-identity`, Packagist `meghuizen/aspire-azure-identity`. MIT, matching this
repository.

Microsoft's Service Connector documentation states the position plainly:

> "For PHP, there's not a plugin or library for passwordless connections. You can get an access token for
> the managed identity or service principal and use it as the password to connect to the database. The
> access token can be acquired using Azure REST API."

The Azure SDK for PHP was retired in February 2021. The one community package,
`webonyx/azure-identity-php`, has eight commits and no managed identity support. There is nothing to
depend on.

### Surface

```php
namespace Meghuizen\AspireAzure;

final class Identity
{
    /** A token for any resource. Cached and refreshed. */
    public static function token(string $resource, ?string $clientId = null): string;

    /** The database password: a token for ossrdbms, using the client ID the AppHost published. */
    public static function databasePassword(): string;

    /** A Key Vault secret, fetched with the same identity. */
    public static function secret(string $vaultUri, string $name): string;
}
```

Usage is one line per application:

```php
// Laravel, config/database.php
'password' => \Meghuizen\AspireAzure\Identity::databasePassword(),

// WordPress, wp-config.php
define('DB_PASSWORD', \Meghuizen\AspireAzure\Identity::databasePassword());
```

Symfony is the awkward one, because `DATABASE_URL` is a single string in the environment. The
documentation will cover the two honest options: a small environment variable processor, or overriding
the Doctrine connection's `password` in `config/packages/doctrine.yaml`. We will not ship a bundle.

### The token request

Taken verbatim from Microsoft's documentation rather than inferred:

```
GET $IDENTITY_ENDPOINT?resource=https://ossrdbms-aad.database.windows.net&api-version=2019-08-01
    [&client_id=<user-assigned client id>]
Headers: X-IDENTITY-HEADER: $IDENTITY_HEADER
         Metadata: true
```

The audience must be exactly `https://ossrdbms-aad.database.windows.net`. Microsoft's documentation warns
that stricter audience validation is coming and other audiences will stop working.

### Caching

Tokens are valid for between 5 and 60 minutes, so fetching once at container start is not viable — it
looks correct and fails within the hour. The cache refreshes at 80% of the remaining lifetime.

APCu is the store, falling back to a file under `sys_get_temp_dir()` with 0600 permissions when APCu is
unavailable. This package already installs and enables APCu on every PHP resource, so the dependency is
already met.

Two requests can refresh at the same time. Both will succeed and one will overwrite the other with an
equally valid token, so this is left unlocked rather than adding lock handling for a harmless race. The
behaviour is documented rather than hidden.

### Error handling

The failure modes here are ones people will hit, so the messages carry the diagnosis:

- `IDENTITY_ENDPOINT` absent: throw naming the likely cause, that the code is not running on Container
  Apps or App Service, and pointing at the local development path.
- Non-2xx from the identity endpoint: include the status and the response body. Azure's errors here are
  genuinely informative and swallowing them wastes the reader's time.
- A token that arrives already expired, or with no `expires_on`: treat as a failure rather than caching
  something that cannot work.
- Local development: `AZURE_ACCESS_TOKEN` is read first if set, so `az account get-access-token` can
  drive a local run without the shim needing Azure CLI knowledge.

Nothing retries. A token fetch is a call to a local endpoint; if it fails the request should fail
visibly rather than hang.

## Testing

Part A is unit-testable against the existing `PhpTestBuilder` and Verify snapshots, in the style already
used by `PhpDockerfileGeneratorTests` and `PhpConnectionReferenceTests`:

- console resources appear in a published manifest with the right kind and cron
- the proxy variables and the prepend file appear per convention, and only in publish mode
- session variables are built from a cache reference, and the missing-store warning fires
- probes are registered with the intended defaults
- the Azure convention emits the documented names, including `MYSQL_ATTR_SSL_CA` and Redis on 10000
- two external HTTP endpoint groups fail validation with both endpoint names in the message

Part B's manifest shaping is testable the same way. What is not testable that way, and must be proven
against real Azure before any of it is claimed in the README:

1. A PHP application on Container Apps connecting to Azure Database for PostgreSQL with no password.
2. The same against MySQL. See the open question below.
3. Migrations running as a job and completing before the app takes traffic.
4. A scheduled job firing on its cron.
5. Azure Files mounted writable by `www-data`.
6. The collector as a second container in the same app, receiving spans over localhost.

The README's Status section has a standard — verified end to end against real containers — and this work
will be held to it. Nothing goes in that list until it has actually run.

## Open questions

**MySQL passwordless may not be possible from PDO.** Microsoft's driver compatibility table lists both
`mysqli` and `PDO_MYSQL` as supported for Entra tokens. PHP bug #78467, still open, states that PDO
cannot use the `mysql_clear_password` plugin that sending a token as a password requires. These
contradict each other and only a real connection settles it. PostgreSQL has no equivalent problem,
because it takes a plain password over TLS.

Consequence for sequencing: PostgreSQL passwordless is built and proven first. The MySQL claim is not
made until it is demonstrated, and if PDO cannot do it, the honest outcomes are to require `mysqli` for
that path or to document MySQL as Key Vault only.

**Whether the collector can be a sidecar.** Container Apps supports several containers in one app sharing
localhost, and `PublishAsAzureContainerApp` exposes `Template.Containers`. Whether Aspire will let a
second container be added there without fighting its own image-building is unverified.

## Out of scope

- Azure App Service and Azure Kubernetes Service. The Part A work benefits both; targeting them is
  separate.
- Framework service providers and bundles. Deliberately excluded, per the decision above.
- Service Connector. Aspire already models identity and role assignments; using both invites two owners
  for one identity.
- Memcached, which has no Aspire integration and its own repository.
- Azure Service Bus as a queue backend. Worth doing, but it is a queue driver question rather than a
  Container Apps question and should be designed on its own.

## Sources

- [Service Connector: Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/service-connector/how-to-integrate-postgres) — the PHP passwordless position and the token request contract
- [Microsoft Entra authentication for Azure Database for MySQL](https://learn.microsoft.com/en-us/azure/mysql/flexible-server/security-how-to-entra) — driver compatibility, token lifetime, audience
- [Tutorial: PHP app with MySQL and Redis on App Service](https://learn.microsoft.com/en-us/azure/app-service/tutorial-php-mysql-app) — the `AZURE_*` variable names, TLS settings, Key Vault approach
- Aspire docs, retrievable with `aspire docs get <slug>`: `azure-container-app-jobs` for the job APIs and
  trigger types, `deploy-to-azure-container-apps` for ingress rules and the Azure Files translation,
  `configure-azure-container-apps-environments` for `PublishAsAzureContainerApp`,
  `user-assigned-managed-identity` for identity and role assignments, and
  `compiler-warning-aspireprobes001` for the probe API and its defaults
- [PHP bug #78467](https://bugs.php.net/bug.php?id=78467) — PDO and the cleartext plugin

---

## Implementation notes

Added after the work was done, recording where reality differed from the design above. The sections above are
left as written so the difference is visible.

**B6 is not implementable as designed.** The plan was for `WithDataVolume` to set the uid and gid of the Azure
Files mount to match the image's user. There is no API to call: `ContainerAppAzureFileProperties` carries an
account name, key, share name and access mode, and no mount options at all. Container Apps simply does not
expose them, unlike AKS. The implementation therefore warns at publish time and points at
`WithBlobStorageReference`, which was already the recommended answer for uploads.

**A4 needed a different API than expected.** `WithHttpProbe` registers a dashboard health check internally,
keyed on endpoint, path and status code, so calling it three times for the same path collides on the second.
Readiness goes through `WithHttpProbe` — it is the probe that answers "can this take traffic", which is what a
`WaitFor` dependent wants — and startup and liveness are added as `EndpointProbeAnnotation` directly.

**A1's environment copy had to move.** The design said to replay the parent's environment callbacks. The
original code did this at `BeforeStart`, which publishing never raises, so a published migration would have
carried no database configuration. Replaying at resolution time works in both modes and keeps the original
property that references added after `WithMigrations` are still picked up. The same reasoning moved A3's
scale-out warning off a start event.

**One confirmation.** Alpine's `www-data` is UID 82, as assumed, verified by running the base image.

**Part C is staged in this repository** under `php/aspire-azure-identity` rather than extracted, so both sides
of the contract can change together until the shape settles.

**Testing status.** 183 C# tests and 10 PHP tests pass. The PHP tests run against PHP 8.5 in the serversideup
image this package publishes. Everything in the "must be proven against real Azure" list above remains
unproven, and the README says so rather than implying otherwise.
