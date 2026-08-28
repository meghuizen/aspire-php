# Aspire.Hosting.PHP

Run PHP applications as first-class [Aspire](https://aspire.dev) resources — dashboard, logs, traces, service
discovery and endpoints — and turn them into a hardened non-root container with `aspire publish`.

Requires **Aspire 13.5.x**. Targets **PHP 8.5.x**.

> Early preview. The core works and is verified end to end (see [Status](#status)), but the API may still change.

## Install

```bash
dotnet add package Meghuizen.Aspire.Hosting.Php
```

The package ID carries a vendor prefix because `Aspire.*` is an
[ID prefix reserved](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation) by Microsoft on
nuget.org. The assembly and namespace are still `Aspire.Hosting.PHP`, and the extension methods live in the
`Aspire.Hosting` namespace, so your AppHost code needs no `using`.

## Use

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// A worker: runs `php worker.php`
builder.AddPhpApp("worker", "../php-worker", "worker.php")
       .WithComposer();

// A web application, served by FrankenPHP
var shop = builder.AddPhpWebApp("shop", "../shop")
       .WithComposer()
       .WithPhpExtension("pdo_pgsql", "redis")
       .WithOpenTelemetry()
       .WithExternalHttpEndpoints();

builder.AddProject<Projects.Api>("api").WithReference(shop);

builder.Build().Run();
```

## You do not need PHP installed

If `php` is on your PATH it is used directly. If it is not, the application runs in a container with your source
bind-mounted — edits are still live, and `vendor/` is still written back to your working copy. Composer runs in
that same container, so it does not have to be installed locally either.

Which one was chosen is logged on the resource, never left for you to guess. Force it with `PhpRunMode`:

```csharp
builder.AddPhpApp("worker", "../php-worker", "worker.php", PhpRunMode.Container);
```

`PhpRunMode` is an argument rather than a fluent call because it decides whether the resource is an executable or
a container, which has to be settled when the resource is created.

`aspire publish` always produces a container, whichever way you ran it locally.

## API

| Method | Effect |
|---|---|
| `AddPhpApp(name, appDirectory, scriptPath, runMode?)` | Runs `php <scriptPath>`. Publishes on `serversideup/php:8.5-cli-alpine` |
| `AddPhpWebApp(name, appDirectory, documentRoot?, runMode?)` | FrankenPHP, one HTTP endpoint. `documentRoot` defaults to `public` |
| `WithComposer(install?, installArgs?)` | Child resource running `composer install`; the app waits for it |
| `WithPhpExtension(params names)` | `install-php-extensions <names>` in the image. Accumulates across calls |
| `WithOpenTelemetry()` | The `opentelemetry` extension plus `OTEL_PHP_AUTOLOAD_ENABLED` |
| `WithPhpIniSetting(key, value)` | `-d key=value` locally, a generated ini file in the image |
| `WithXdebug(port?)` | Xdebug environment; installs the extension in container mode |
| `WithWorkerMode(workerScript?)` | FrankenPHP worker mode. Web apps only |
| `WithPhpVersion(version)` | Pins the image tag and the version required of a local PHP |
| `WithDockerfileBaseImage(runtimeImage:)` | Aspire built-in. Overrides the base image |

## Choosing a PHP version

8.5 is the default. **8.4 is fully supported** — switch with any one of these, in order of precedence:

```csharp
// 1. Explicit, and wins over everything else
builder.AddPhpWebApp("shop", "../shop").WithPhpVersion("8.4");
```

```
# 2. A .php-version file in the application directory
8.4
```

```jsonc
// 3. composer.json — config.platform.php wins over require.php, because it is what
//    Composer actually resolved your vendor directory against
{ "require": { "php": "^8.4" } }
```

Constraint syntax is understood, so `^8.4`, `>=8.4`, `8.4.*`, `~8.4.0` and `8.4.24` all select 8.4. A range such
as `>=8.4 <8.6` resolves to its lower bound, since that is the version the application is guaranteed to run on.

The version selects the image tag for both the published container and the run-mode container:

| Version | Worker image | Web image |
|---|---|---|
| 8.5 (default) | `serversideup/php:8.5-cli-alpine` | `serversideup/php:8.5-frankenphp-alpine` |
| 8.4 | `serversideup/php:8.4-cli-alpine` | `serversideup/php:8.4-frankenphp-alpine` |

Both are verified to exist and run — 8.5.9 and 8.4.24 respectively.

`WithDockerfileBaseImage(runtimeImage: "...")` still overrides the whole thing if you want an image from
somewhere else entirely.

One thing to watch: when you run against a **local** PHP, you get whichever version is installed on your machine,
not the one you targeted. If those differ the resource logs a warning at startup naming both, because otherwise
you would develop on one version and deploy another with nothing to indicate it. Pass `PhpRunMode.Container` to
develop on exactly the version you deploy.

## Frameworks and CMSes

| Method | Document root | Composer | Connection names |
|---|---|---|---|
| `AddLaravelApp(name, dir)` | `public` | yes | `DB_*`, `REDIS_*` |
| `AddSymfonyApp(name, dir)` | `public` | yes | `DATABASE_URL`, `REDIS_URL` |
| `AddWordPressApp(name, dir)` | `.` | no | `WORDPRESS_DB_*` |
| `AddDrupalApp(name, dir, docRoot?)` | `web` | yes | `DRUPAL_DATABASE_*` |
| `AddJoomlaApp(name, dir)` | `.` | no | `JOOMLA_DB_*` |

Each is `AddPhpWebApp` with that application's document root, PHP extensions and naming already set, so
everything on a plain PHP resource still applies. WordPress and Joomla serve from their own root rather than a
`public/` subdirectory, which is why their document root is `.`.

## Databases and caches

Aspire's own `WithReference` injects an ADO.NET connection string, which no PHP application reads.
`WithDatabaseReference` and `WithCacheReference` translate the same reference into the variables the target
actually reads, and install the matching PHP extension:

```csharp
var db = builder.AddMySql("mysql").AddDatabase("shopdb");
var cache = builder.AddRedis("cache");

builder.AddLaravelApp("shop", "../shop")
       .WithDatabaseReference(db)     // DB_CONNECTION=mysql, DB_HOST, DB_PORT, DB_DATABASE, ...
       .WithCacheReference(cache)     // REDIS_HOST, REDIS_PORT, REDIS_CLIENT=phpredis, REDIS_URL
       .WaitFor(db)
       .WaitFor(cache);
```

MySQL, PostgreSQL, SQL Server and SQLite are recognised, and the driver name is spelled the way each target
spells it — Laravel wants `pgsql`, Joomla wants `mysqli`. Pass `prefix:` for a second database
(`prefix: "DB_REPORTING"` yields `DB_REPORTING_HOST`), or `convention:` to override the naming.

Values are passed as Aspire expressions rather than resolved strings, so publishing leaves them as placeholders
in the generated compose file instead of baking passwords into it.

### Redis is TLS by default

Aspire turns Redis TLS on while running, so the scheme is `rediss://` rather than `redis://`. A client given
only a host and port connects in plaintext and fails with `read error on connection`, which says nothing about
TLS. `REDIS_URL` is therefore always set, because it is the only value carrying the scheme — read it and prefix
the host with `tls://`:

```php
$url = parse_url(getenv('REDIS_URL'));
$secure = ($url['scheme'] ?? 'redis') === 'rediss';

$redis = new Redis();
$redis->connect(($secure ? 'tls://' : '') . $url['host'], (int) $url['port']);
```

Aspire exports `SSL_CERT_DIR` including its own certificate authority, so verification works without extra
configuration. See `playground/php-db` for the complete sample.

### Drupal and Joomla read files, not the environment

Drupal's database configuration lives in `settings.php`. The variables are set, but the site has to read them:

```php
$databases['default']['default'] = [
  'driver'   => getenv('DRUPAL_DATABASE_DRIVER') ?: 'mysql',
  'host'     => getenv('DRUPAL_DATABASE_HOST'),
  'port'     => getenv('DRUPAL_DATABASE_PORT'),
  'database' => getenv('DRUPAL_DATABASE_NAME'),
  'username' => getenv('DRUPAL_DATABASE_USERNAME'),
  'password' => getenv('DRUPAL_DATABASE_PASSWORD'),
];
```

Joomla keeps its settings in `configuration.php`, which is PHP source rather than configuration the environment
can override. The `JOOMLA_DB_*` variables are set for the installer and for images that read them, but a site
that is already installed keeps using its own file.

### Persistent content

WordPress uploads, Drupal files and Joomla images live on disk. Running locally that is your working copy, but a
published container starts empty each time. `WithDataVolume` keeps a directory across restarts:

```csharp
builder.AddWordPressApp("blog", "../blog")
       .WithDataVolume("wp-content/uploads");
```

## Telemetry

`WithOpenTelemetry()` handles the Aspire side. Your application still needs the SDK:

```bash
composer require open-telemetry/sdk open-telemetry/exporter-otlp
```

Aspire supplies `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` and the rest as standard environment
variables, which is exactly what the PHP SDK reads — no glue code.

One caveat worth knowing: **PHP has no background thread**, so outside FrankenPHP worker mode every request pays
the span export cost inline. For a web application under load, pair `WithOpenTelemetry()` with `WithWorkerMode()`,
which keeps the process alive between requests so the batching exporter can do its job.

## Why FrankenPHP

In Node and Python the app *is* the web server: `node server.js` binds a port and Aspire points an endpoint at
it. Classic PHP-FPM cannot do that — it speaks FastCGI on port 9000 and needs a web server in front of it.

FrankenPHP is Caddy with PHP compiled in: one long-running process that binds a port. That is what lets a PHP
application map onto a single Aspire HTTP endpoint with no sidecar.

Running against a local PHP uses PHP's built-in development server instead, which is single-threaded and
unsuitable for production. Container run mode uses FrankenPHP and therefore matches what you deploy.

## Base images

Defaults are the [serversideup](https://serversideup.net/open-source/docker-php/) PHP images, chosen because they
run as an unprivileged user out of the box, pin real 8.5.x releases, and ship both `composer` and
`install-php-extensions`.

| Resource | Default image |
|---|---|
| `AddPhpApp` | `docker.io/serversideup/php:8.5-cli-alpine` |
| `AddPhpWebApp` | `docker.io/serversideup/php:8.5-frankenphp-alpine` |

Override either with `WithDockerfileBaseImage(runtimeImage: "...")`. Generated PHP Dockerfiles are single stage —
PHP has nothing to compile away and the base images already contain Composer — so setting a *different* build and
runtime image is rejected with an explanatory error rather than silently ignored.

If the application directory already contains a `Dockerfile`, it is used as-is and nothing is generated.

## Debugging

`WithXdebug()` sets the Xdebug environment and installs the extension in container mode. Xdebug connects out to
your editor, so start the listener first — a "Listen for Xdebug" configuration in VS Code.

The Aspire dashboard's debug button cannot drive this: the Aspire VS Code extension resolves debuggers from a
fixed set of languages and PHP is not one of them. F5 on your own listener configuration is the route.

In container mode the launch configuration also needs a `pathMappings` entry from `/var/www/html` to your
application directory, or breakpoints will never bind.

## Status

Verified end to end against real containers:

- `aspire run` with no PHP installed — container fallback, bind mount, Composer in a container, FrankenPHP
  serving, and every `OTEL_*` variable reaching the PHP process
- `aspire publish` — both generated Dockerfiles build and run: PHP 8.5.9, FrankenPHP SAPI, non-root `www-data`
- **MySQL 9.7.2** and **PostgreSQL 18.3** — a PHP app connecting through the translated variables, writing a row
  and reading it back
- **Redis over TLS** — connected, wrote and read back
- PHP 8.5 and 8.4 both selected and running
- Cross-platform CI on Linux, Windows and macOS

Honest about what is *not* proven: the framework and CMS helpers set the right document root, extensions and
variable names, and that is unit-tested, but no full WordPress, Laravel, Symfony, Drupal or Joomla installation
has been stood up against them yet. Memcached is not covered — it has no Aspire integration and gets its own
repository.

## Building

```bash
dotnet build
dotnet test
```

The playground under `playground/` has a worker and a web sample wired into an AppHost:

```bash
cd playground/PhpPlayground.AppHost
aspire run
```

## Licence

MIT
