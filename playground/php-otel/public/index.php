<?php

declare(strict_types=1);

require __DIR__ . '/../vendor/autoload.php';

use OpenTelemetry\Contrib\Otlp\SpanExporter;
use OpenTelemetry\SDK\Common\Attribute\Attributes;
use OpenTelemetry\SDK\Common\Export\Http\PsrTransportFactory;
use OpenTelemetry\SDK\Common\Time\ClockFactory;
use OpenTelemetry\SDK\Resource\ResourceInfo;
use OpenTelemetry\SDK\Resource\ResourceInfoFactory;
use OpenTelemetry\SDK\Trace\SpanProcessor\BatchSpanProcessor;
use OpenTelemetry\SDK\Trace\TracerProvider;

header('Content-Type: text/plain; charset=utf-8');

$env = static fn (string $k): ?string => ($_SERVER[$k] ?? getenv($k)) ?: null;

$endpoint = rtrim($env('OTEL_EXPORTER_OTLP_ENDPOINT') ?? 'http://localhost:4318', '/') . '/v1/traces';

$resource = ResourceInfoFactory::defaultResource()->merge(ResourceInfo::create(Attributes::create([
    'service.name' => $env('OTEL_SERVICE_NAME') ?? 'php-otel',
])));

$provider = TracerProvider::builder()
    ->addSpanProcessor(new BatchSpanProcessor(
        new SpanExporter((new PsrTransportFactory())->create($endpoint, 'application/x-protobuf')),
        ClockFactory::getDefault()
    ))
    ->setResource($resource)
    ->build();

$tracer = $provider->getTracer('aspire-php-playground');

$root = $tracer->spanBuilder('handle-request')->startSpan();
$scope = $root->activate();

$child = $tracer->spanBuilder('do-some-work')->startSpan();
usleep(15_000);
$child->setAttribute('work.items', 3);
$child->end();

$root->setAttribute('http.route', '/');
$root->end();
$scope->detach();

// PHP has no background thread, so nothing flushes on its own. Outside worker mode this shutdown is the
// only chance the batch processor gets, and it runs inside the request.
$provider->shutdown();

echo "traced a request\n";
printf("exported to : %s\n", $endpoint);
printf("service     : %s\n", $env('OTEL_SERVICE_NAME') ?? '(unset)');
printf("trace id    : %s\n", $root->getContext()->getTraceId());
