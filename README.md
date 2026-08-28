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

The PHP version is read from `.php-version` or `composer.json` automatically; `WithPhpVersion` is only needed
when the application declares neither.

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
- Cross-platform CI on Linux, Windows and macOS

Not yet built: WordPress, Laravel, Symfony, Drupal and Joomla helpers, and MySQL / PostgreSQL / Redis / Memcached
connection mapping. The gap there is that `WithReference(db)` injects an ADO.NET connection string that no PHP
application reads — each framework wants a different shape, so a translation layer is needed.

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
