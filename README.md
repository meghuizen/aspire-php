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
| `AddPhpWebApp(name, appDirectory, documentRoot?, webServer?, runMode?)` | A web app. `webServer` picks FrankenPHP, nginx or Apache |
| `AddPhpNginxApp(name, appDirectory, documentRoot?)` | Shorthand for `PhpWebServer.FpmNginx` |
| `AddPhpApacheApp(name, appDirectory, documentRoot?)` | Shorthand for `PhpWebServer.Apache`. The only one reading `.htaccess` |
| `WithComposer(install?, installArgs?)` | Child resource running `composer install`; the app waits for it |
| `WithPhpExtension(params names)` | `install-php-extensions <names>` in the image. Accumulates across calls |
| `WithOpenTelemetry()` | The `opentelemetry` extension plus `OTEL_PHP_AUTOLOAD_ENABLED` |
| `WithPhpIniSetting(key, value)` | `-d key=value` locally, a generated ini file in the image |
| `WithXdebug(port?)` | Xdebug environment; installs the extension in container mode |
| `WithWorkerMode(workerScript?)` | FrankenPHP worker mode. Web apps only |
| `WithPhpVersion(version)` | Pins the image tag and the version required of a local PHP |
| `WithMigrations(args?)` | Runs migrations once before the app starts |
| `WithQueueWorker(name?, args?)` | Long-running queue consumer |
| `WithScheduler(args?)` | Long-running scheduler |
| `WithPhpConsoleCommand(name, kind, args)` | Any console command, one-shot or long-running |
| `WithHealthCheck(path?, statusCode?)` | HTTP health check and startup, readiness and liveness probes. Web apps only |
| `WithTrustedProxies(proxies?)` | Tells the app it is behind a TLS-terminating proxy. Automatic when publishing |
| `WithSessionStore(cache)` | Sessions in Redis rather than on local disk, so replicas share them |
| `WithoutSharedSessions()` | Says sessions are handled elsewhere, silencing the scale-out warning |
| `WithPhpOptimizations(configure?)` | OPcache, igbinary, APCu, realpath cache and Composer autoloader tuning |
| `WithDatabaseReference(db, ...)` | Translates a database reference into the names the app reads |
| `WithCacheReference(cache, ...)` | Translates a cache reference; always sets `REDIS_URL` |
| `WithMailReference(smtp, from?, ...)` | Points mail at any resource with an SMTP endpoint |
| `WithSmtp(host, port, ...)` | Full SMTP configuration for an external server |
| `WithDataVolume(path, name?)` | Persists uploads across container restarts |
| `WithDockerfileBaseImage(runtimeImage:)` | Aspire built-in. Overrides the base image |

### Azure

Opt-in. Nothing here runs unless you call it, and .NET does not load an assembly until a method touching it
runs — so an AppHost publishing to Docker Compose or Kubernetes never loads the Azure ones.

| Method | Effect |
|---|---|
| `WithAzureContainerApps()` | Shapes console commands: jobs for one-shot and scheduled, no scale-to-zero for workers |
| `WithAzureIdentity(identity)` | Assigns a user-assigned identity and publishes its client ID for the PHP side |
| `WithKeyVaultReference(vault, secret, variable)` | Grants the identity read access; publishes the secret's name, not the secret |
| `WithBlobStorageReference(storage, container)` | Grants blob write access. The better answer than a volume for uploads |
| `PhpConnectionConvention.Azure` | The `AZURE_MYSQL_*` names Service Connector and Microsoft's tutorials use |

## Extensions

`WithPhpExtension` takes any name the extension installer understands. `PhpExtensions` provides constants so
you do not have to guess between spellings that only fail at image build time:

```csharp
builder.AddPhpWebApp("app", "../app")
       .WithPhpExtension(PhpExtensions.DataStructures, PhpExtensions.SimdJson, PhpExtensions.PdoSqlServer);
```

### Already in the base image

41 extensions ship in 8.5, 39 in 8.4 (the difference is `lexbor` and `uri`, both new in PHP 8.5 core). The CLI
and FrankenPHP variants are identical.

```
Core  ctype  curl  date  dom  fileinfo  filter  hash  iconv  json  lexbor†  libxml
mbstring  mysqlnd  openssl  pcntl  pcre  PDO  pdo_mysql  pdo_pgsql  pdo_sqlite
Phar  posix  random  readline  redis  Reflection  session  SimpleXML  sodium  SPL
sqlite3  standard  tokenizer  uri†  xml  xmlreader  xmlwriter  OPcache  zip  zlib
                                                                     († 8.5 only)
```

Requesting one of these is harmless but does nothing — **the installer will not replace an extension that is
already installed**. That is why `pdo_mysql` and `pdo_pgsql` are effectively free, and why Redis needs the
explicit rebuild described above to gain igbinary support.

### Commonly added

| Constant | Name | Size cost | Notes |
|---|---|---|---|
| `PhpExtensions.Gd` | `gd` | small | Image processing. Every CMS here needs it |
| `PhpExtensions.Intl` | `intl` | small | Collation and formatting |
| `PhpExtensions.MySqli` | `mysqli` | small | WordPress and Joomla call this directly, not PDO |
| `PhpExtensions.Igbinary` | `igbinary` | small | Halves what a cache round-trip serializes |
| `PhpExtensions.Apcu` | `apcu` | small | Per-process in-memory cache |
| `PhpExtensions.SimdJson` | `simdjson` | **+5 MB** | SIMD JSON parsing. Only pays off on large documents |
| `PhpExtensions.DataStructures` | `ds` | ~0 MB | See the caveat below |
| `PhpExtensions.PdoSqlServer` | `pdo_sqlsrv` | **+12 MB** | Microsoft's ODBC driver. Does build on Alpine |
| `PhpExtensions.Imagick` | `imagick` | **+186 MB** | Far more capable than `gd`. See below |

### Imagick

Supported and verified, but **not installed by default** — it adds 186 MB, nearly doubling the CLI image:

| | Base | With Imagick |
|---|---|---|
| `8.5-cli-alpine` | 199 MB | **385 MB** |
| `8.5-frankenphp-alpine` | 314 MB | **497 MB** |

```csharp
builder.AddWordPressApp("blog", "../blog")
       .WithPhpExtension(PhpExtensions.Imagick);
```

Verified as Imagick 3.8.1 on both base images, with 268 formats including JPEG, PNG, WEBP, **AVIF**, **HEIC**,
GIF, TIFF, SVG and PDF — well beyond what `gd` handles.

The check that mattered: **FrankenPHP runs thread-safe (ZTS) PHP**, while the CLI image is NTS, and Imagick has
a long history of trouble under ZTS. It works — a test actually rendered a PNG inside the ZTS image — but that
is worth knowing if you swap in a different base image, because it is the combination most likely to break.

`gd` stays the default for the CMS helpers. It covers ordinary thumbnailing at a fraction of the size; reach for
Imagick when you need the formats or the quality. WordPress will prefer Imagick automatically once it is present.

If you do add it, note that ImageMagick parses a very large number of formats, which is a wide attack surface
for user-uploaded files — its `policy.xml` exists precisely to narrow that, and is worth reviewing if you accept
uploads from the public.

Two more worth knowing before you reach for them:

- **`ds` version 2.0.0 removed `Ds\Vector`, `Ds\Deque`, `Ds\Stack`, `Ds\Queue` and `Ds\PriorityQueue`.** What
  remains is `Ds\Map`, `Ds\Set`, `Ds\Seq`, `Ds\Heap`, `Ds\Pair`. Nearly all existing code and documentation
  targets the older API. It is also niche — neither Laravel nor Symfony uses it, so gains are confined to your
  own hot paths, and under a per-request model the structure is built and destroyed within one request.
- **`pdo_sqlsrv` works on Alpine.** Microsoft's ODBC driver supports musl, so SQL Server does not force you onto
  a Debian base image.

## Alpine or Debian

The default images are Alpine. Debian variants exist — drop the `-alpine` suffix — and are considerably larger:

| Image | Alpine | Debian | Difference |
|---|---|---|---|
| `8.5-cli` | **199 MB** | 804 MB | 4.0× |
| `8.5-frankenphp` | **314 MB** | 883 MB | 2.8× |
| `8.4-cli` | 169 MB | | |
| `8.4-frankenphp` | 278 MB | | |

Alpine is the default for that reason, and everything this integration does — including `pdo_sqlsrv`, the usual
reason people are forced onto glibc — is verified to build on it.

Reach for Debian when you hit a genuine musl incompatibility: a proprietary extension shipped as a glibc binary,
or a workload sensitive to musl's allocator, which is slower than glibc's under heavy multi-threaded malloc.
Switch with `WithDockerfileBaseImage(runtimeImage: "docker.io/serversideup/php:8.5-frankenphp")`.

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

## Choosing a web server

Pass a `PhpWebServer` to `AddPhpWebApp`:

```csharp
builder.AddPhpWebApp("shop", "../shop", webServer: PhpWebServer.FpmNginx);
```

| | `FrankenPhp` (default) | `FpmNginx` | `Apache` |
|---|---|---|---|
| Reads `.htaccess` | ✗ | ✗ | **✓** |
| Processes | **one** | nginx + FPM | Apache + FPM |
| Supervisor | none | s6-overlay | s6-overlay |
| Base | Alpine | Alpine | **Debian** |
| Compressed | 46 MB | 46 MB | **178 MB** |
| PHP build | ZTS | NTS | NTS |
| Worker mode | **✓** | ✗ | ✗ |
| Runs on local PHP | ✓ | ✗ | ✗ |

Shorthands exist for the two FPM options: `AddPhpNginxApp` and `AddPhpApacheApp`.

**FrankenPHP is the default** because it is the only one that is genuinely a single process — Caddy with PHP
compiled into the same binary. The others run a web server and PHP-FPM side by side, which needs a supervisor
as PID 1, so one container carries two log streams and one health state.

**Choose `Apache` only for `.htaccess`.** It is the sole option that reads those files. nginx has no equivalent
feature at all, so its rules have to live in server configuration instead. Apache costs roughly four times the
compressed size, because no Alpine variant of that image exists — `8.5-fpm-apache-alpine` is a 404.

**Choose `FpmNginx`** for the traditional pairing, or when an extension dislikes the thread-safe PHP build
FrankenPHP requires. Same size as FrankenPHP, on non-thread-safe PHP.

Both FPM options **always run as containers**, in every mode. PHP's built-in development server has no FastCGI
layer and ignores `.htaccess`, so standing it in for either would quietly change how the application behaves.
Asking for `PhpRunMode.Executable` with them is rejected rather than silently downgraded.

Verified running side by side through Aspire: FrankenPHP, `nginx/1.30.4` and Apache each serving the same
application, with a rewrite rule and a `Header` directive defined only in `.htaccess` taking effect on Apache.

## Mail

```csharp
// MailPit for development, from CommunityToolkit.Aspire.Hosting.MailPit
var mail = builder.AddMailPit("mail");

builder.AddLaravelApp("shop", "../shop")
       .WithMailReference(mail, from: "shop@example.test");

// Or a real server
builder.AddPhpWebApp("app", "../app")
       .WithSmtp("smtp.example.com", 587, username: "postmaster", password: smtpPassword, encryption: "tls");
```

The SMTP server itself is not this package's concern — `WithMailReference` takes **any** resource with an SMTP
endpoint. MailPit is the obvious development choice and already has an Aspire integration.

**PHP applications send mail in two different ways, so both are configured.**

Frameworks with their own mailer — Laravel, Symfony — speak SMTP themselves and read environment variables, so
those are set in the convention each expects:

| Convention | Variables |
|---|---|
| Laravel | `MAIL_MAILER=smtp`, `MAIL_HOST`, `MAIL_PORT`, `MAIL_USERNAME`, `MAIL_PASSWORD`, `MAIL_ENCRYPTION`, `MAIL_FROM_ADDRESS` |
| Symfony | `MAILER_DSN` |
| WordPress, Joomla, Drupal, generic | `MAIL_*`, plus the sendmail shim below |

Everything else calls PHP's `mail()`, and **`mail()` does not speak SMTP on Linux at all** — it pipes the
message to whatever `sendmail_path` names, which by default is nothing useful in a container. WordPress and
Joomla both work this way. So `msmtp` is installed and `sendmail_path` points at it, which makes `mail()`
deliver over SMTP without the application changing a line.

Verified end to end: a plain `mail()` call reached MailPit with the right From, To and Subject.

Two details worth knowing:

- **The password is read through a command, not passed as an argument.** `--passwordeval` keeps it out of the
  process list, where `--password` would be visible to anything that can run `ps`.
- **Host and port stay runtime configuration.** The generated ini uses `${MAIL_HOST}`, which PHP expands when it
  parses the file, so the image does not have to be rebuilt when the server changes.

## Console commands, workers and schedulers

A framework application is rarely just a web endpoint. Migrations have to run before it starts, queues need
consuming, and schedules need ticking — each as its own resource in the dashboard, with its own logs.

```csharp
var db = builder.AddMySql("mysql").AddDatabase("shopdb");

builder.AddLaravelApp("shop", "../shop")
       .WithDatabaseReference(db)
       .WaitFor(db)
       .WithMigrations()                 // runs once; the app waits for it
       .WithQueueWorker()                // runs until stopped; waits for migrations
       .WithQueueWorker("emails", "artisan", "queue:work", "--queue=emails")
       .WithScheduler();
```

| Method | Default command | Runs |
|---|---|---|
| `WithMigrations(args?)` | Laravel `artisan migrate --force`, Symfony `doctrine:migrations:migrate`, Drupal `drush updatedb` | Once. The app waits for it |
| `WithQueueWorker(name?, args?)` | Laravel `artisan queue:work`, Symfony `messenger:consume async` | Until stopped |
| `WithScheduler(args?)` | Laravel `artisan schedule:work`, Symfony `messenger:consume scheduler_default` | Until stopped |
| `WithPhpConsoleCommand(name, kind, args)` | — | Whichever you choose |

Each command runs in **the same environment as the application** — same image, same extensions, same database
and cache variables — so it sees exactly what the application sees.

It also **inherits the application's `WaitFor`**. Without that, `WaitFor(db)` on the app would leave migrations
racing the database, and migrations run first, so they would lose.

WordPress and Joomla have no migration or queue concept. Asking for one says so, naming the framework, rather
than inventing a command.

Console commands are **not created when publishing** — they would run on the machine doing the publish, which is
the wrong machine. Migrations belong to a deployment step, not to building an image.

### Worker concurrency

Aspire's `WithReplicas` is limited to project resources, so it is not available here. Scale with the worker's
own options — Laravel Horizon, or `queue:work` arguments — or add a second `WithQueueWorker` with its own name.

## Health checks

```csharp
builder.AddLaravelApp("shop", "../shop")
       .WithHealthCheck();        // GET /healthcheck, expects 200
```

Without this a resource reports running the moment its process starts, which for a web application says very
little — the server can be up while every request fails. It also makes `WaitFor` meaningful, since dependents
then wait for the application to actually answer.

The default path is answered by the web server itself, so it stays green even when PHP is broken. Point it at
one of your own routes to check the application instead:

```csharp
.WithHealthCheck("/up")           // Laravel's built-in health route
```

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

## Performance

`WithPhpOptimizations()` applies the settings that actually move the needle for PHP:

```csharp
builder.AddLaravelApp("shop", "../shop")
       .WithPhpOptimizations(options =>
       {
           options.OpcachePreloadScript = "vendor/autoload.php";
           options.IgbinaryForRedis = true;
       });
```

| Setting | Default | Why |
|---|---|---|
| `opcache.enable` | on when publishing, off when running | Stops PHP recompiling every file on every request. Off while running so an edit takes effect immediately |
| `opcache.validate_timestamps` | off when publishing | The source in an image cannot change, so the check is a wasted stat per include per request |
| `opcache.max_accelerated_files` | 20000 | A framework exceeds PHP's default of 10000, and files past the limit are silently recompiled every request — which looks like OPcache not working at all |
| `opcache.memory_consumption` | 128 MB | |
| `opcache.interned_strings_buffer` | 16 MB | |
| `opcache.jit` | **off** | Measured: 40-50% on tight numeric loops, inside noise on request-shaped work. See below |
| `opcache.preload` | not set | Keeps framework classes resident instead of linking them per request. Publishing only |
| `apcu` + `igbinary` | **on by default, no call needed** | igbinary halves what a cache round-trip serializes |
| `session.serialize_handler` | igbinary | |
| `realpath_cache_size` | 4096 KB | PHP's default of 256 KB is far too small for a framework resolving thousands of include paths |
| Composer autoloader | `--classmap-authoritative` | No filesystem fallback. Correct for a fixed image |

Everything is applied as php.ini values, so it works the same for a local process and a container.

### igbinary and APCu are on by default

You get them without calling `WithPhpOptimizations` at all. Verified inside a built image:

```
session.serialize_handler    igbinary
apc.serializer               igbinary
igbinary payload             60 bytes vs serialize 114
```

Two costs to know about:

- **+33 seconds on every image build** — 6 s becomes 39 s, because the extension installer compiles both from
  C source. Layer caching means you pay it per base-image change, not per source edit.
- **igbinary session data is not readable by a PHP instance using the default handler.** Fine for a fresh
  deployment. If you have existing sessions in a shared store, or non-igbinary instances reading the same Redis,
  switching logs those users out.

Opt out per resource:

```csharp
.WithPhpOptimizations(o => { o.Igbinary = false; o.Apcu = false; })
```

### Why the JIT is off by default

Measured rather than assumed, on PHP 8.5 with OPcache on:

| Workload | JIT off | JIT tracing | Change |
|---|---|---|---|
| Tight numeric loop | 0.133–0.153 s | 0.068–0.099 s | **~40–50% faster** |
| Request-shaped work | 0.057–0.063 s | 0.045–0.083 s | within noise |

And the request-shaped benchmark performs **no I/O at all**, so a real request — waiting on a database, a cache,
a network call — would show less again.

**It is not a security problem.** PHP maps the JIT buffer twice: writable in one view, executable in the other.
A process with the JIT actively compiling has **zero writable-and-executable mappings**, confirmed by reading
`/proc/self/maps` after 2,341 bytes of code had been generated. No W^X violation.

What it does cost is the buffer, reserved per process, for no measurable gain on a typical web workload. There
is also one hard incompatibility:

> `JIT is incompatible with third party extensions that override zend_execute_ex(). JIT disabled.`

**Xdebug loads such an extension**, so the JIT and step debugging are mutually exclusive — PHP silently turns
the JIT off when Xdebug is present.

Turn it on when you have measured a CPU-bound workload that benefits:

```csharp
.WithPhpOptimizations(o => o.OpcacheJit = true)
```

That also turns OPcache on, because the JIT is part of OPcache and does nothing without it — asking for the JIT
alone produced ini that read correctly and never engaged.

### igbinary and Redis

igbinary is a drop-in replacement for `serialize()` producing roughly half the bytes — measured at 28 vs 56 on a
small nested array. It only affects things that get serialized, so it is a caching and session optimization,
not a general speedup. The format is binary and PHP-specific: anything outside PHP that reads your cached data
will not be able to.

APCu and sessions pick it up automatically. Redis does not, because **the base images ship the Redis extension
built without igbinary support**, and the extension installer refuses to replace an extension that is already
present. `IgbinaryForRedis = true` uninstalls and rebuilds it, which adds about a minute to the image build.
That makes `Redis::SERIALIZER_IGBINARY` available; your code still has to select it:

```php
$redis->setOption(Redis::OPT_SERIALIZER, Redis::SERIALIZER_IGBINARY);
```

### One trap worth knowing

`opcache.preload` names a file that Composer only creates during the build, and **PHP refuses to start at all
when preload points at a missing file**. Written in the obvious place, it breaks `composer install` a few lines
later. The generated Dockerfile therefore appends the preload settings after the autoloader is built, not
alongside the other ini values.

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
- OPcache, preloading (5 scripts loaded), igbinary for APCu and sessions, an enlarged realpath cache and an
  igbinary-capable Redis extension, all confirmed live with `ini_get` inside the built image
- Console commands: a seed step connected to MySQL through the inherited variables, exited 0, and the app
  started only after it
- Health checks: Aspire registered and passed an HTTP check on the one resource that asked for it
- Apache: a rewrite rule and a header defined only in `.htaccess` both took effect, on NTS PHP
- All three web servers side by side through Aspire: FrankenPHP, `nginx/1.30.4` and Apache
- Mail: a plain `mail()` call reached MailPit through the sendmail shim, with the right From, To and Subject
- Cross-platform CI on Linux, Windows and macOS

- The PHP identity package's own test suite, run against PHP 8.5 in the same serversideup image this package
  publishes: the token cache's refresh arithmetic and every failure message

Honest about what is *not* proven: the framework and CMS helpers set the right document root, extensions and
variable names, and that is unit-tested, but no full WordPress, Laravel, Symfony, Drupal or Joomla installation
has been stood up against them yet. Memcached is not covered — it has no Aspire integration and gets its own
repository.

**Nothing in the deployment work has been run against real Azure.** The manifest shaping is unit-tested and the
PHP package's own logic is tested, but the following are written against documented contracts and not yet
demonstrated, and should be read that way until this list moves upward:

- a PHP application on Container Apps connecting to Azure Database for PostgreSQL with no password
- the same against MySQL, which may not be possible at all from PDO — see [Passwordless databases](#passwordless-databases)
- migrations running as a Container App Job and completing before the app takes traffic
- a scheduled job firing on its cron
- whether an Azure Files mount is writable by `www-data` in practice
- the collector as a second container in the same Container App, which is designed but not yet built

## Deploying

`aspire publish` produces a container that any target will run. What that container does *not* do on its own
is behave correctly once something other than your machine is in front of it, and that is what the deployment
work here covers. Three of the four fixes are not Azure-specific and apply equally to Compose and Kubernetes.

**Console commands now deploy.** Migrations, queue workers and schedulers used to exist only during
`aspire run` and were silently absent from anything you published. They now reach the manifest, each carrying
what kind it is, because a one-shot migration and a long-running worker are the same shape in the app model
and a deployment target cannot guess which is which.

**PHP is told it is behind a proxy.** Every target terminates TLS at the edge and forwards plain HTTP, so PHP
would otherwise report an unencrypted request and build `http://` URLs — mixed content everywhere, and a
redirect loop wherever the framework redirects to its canonical HTTP URL and the platform redirects back.
Laravel and Symfony get the environment variables they read; WordPress, Joomla and Drupal read
`$_SERVER['HTTPS']` directly, so those images get a small prepend file that fills it in from the forwarded
headers. Applied automatically when publishing; `WithTrustedProxies("")` opts out.

**Sessions.** PHP writes them to local disk, which is right for one replica and wrong for any other number.
`WithSessionStore(cache)` shares them. Publishing without it, or without `WithoutSharedSessions()`, warns —
the failure is users being logged out at random, which reads like an application bug rather than a deployment
one.

**Health checks reach the target.** `WithHealthCheck` registers startup, readiness and liveness probes as well
as the dashboard check. The startup probe is deliberately patient: an image with OPcache preloading and a large
autoloader can take tens of seconds to answer its first request, and a tight probe kills the container before
it finishes booting.

### Azure Container Apps

```csharp
var aca = builder.AddAzureContainerAppEnvironment("aca");
var identity = builder.AddAzureUserAssignedIdentity("shop-identity");
var vault = builder.AddAzureKeyVault("secrets");
var storage = builder.AddAzureStorage("storage");

builder.AddLaravelApp("shop", "../shop")
       .WithMigrations()
       .WithQueueWorker()
       .WithScheduler()
       .WithAzureIdentity(identity)
       .WithKeyVaultReference(vault, "app-key", "APP_KEY_SECRET_NAME")
       .WithBlobStorageReference(storage, "uploads")
       .WithAzureContainerApps()
       .WithExternalHttpEndpoints();
```

Aspire already turns the published container into a Container App, provisions a registry and pushes the image.
`WithAzureContainerApps()` adds what it cannot infer: the migration becomes a manually triggered Container App
Job, the scheduler a job on its cron, and the queue worker an app pinned to one replica — a worker allowed to
scale to zero stops consuming the queue, and nothing arrives over HTTP to wake it up again.

Two constraints are enforced rather than left to fail late. A Container App has exactly one external HTTP
ingress, so a second external endpoint is rejected at publish time with both endpoints named. And a volume
becomes an Azure Files mount whose owner cannot be changed — the ARM type carries no mount options at all — so
using one warns and points at `WithBlobStorageReference`, which is the better answer for uploads anyway.

### Passwordless databases

There is no library for this in PHP. Microsoft's own documentation says so, and the Azure SDK for PHP was
retired in 2021, so the companion package [`meghuizen/aspire-azure-identity`](php/aspire-azure-identity) calls
the documented REST endpoint directly. `WithAzureIdentity` publishes the client ID and audience it reads.

```php
// Laravel, config/database.php
'password' => \Meghuizen\AspireAzure\Identity::databasePassword(),
```

Tokens last between five and sixty minutes, so the package refreshes at four fifths of each one's life.
Fetching once at container start looks correct and fails within the hour.

**PostgreSQL first, and MySQL not yet claimed.** Postgres takes a token as an ordinary password over TLS.
MySQL needs the `mysql_clear_password` plugin, and the sources disagree about whether PHP can use it:
Microsoft's driver table lists both `mysqli` and `PDO_MYSQL` as supported, while
[PHP bug #78467](https://bugs.php.net/bug.php?id=78467) says PDO cannot. Only a real connection settles that,
so no MySQL claim is made here. `WithKeyVaultReference` covers MySQL in the meantime, which is what
Microsoft's own flagship PHP tutorial does.

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
