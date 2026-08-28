<?php

declare(strict_types=1);

header('Content-Type: text/plain; charset=utf-8');

// Aspire injects the OTEL_* variables. Showing them proves the wiring reached the application.
$otel = array_filter(
    $_SERVER,
    static fn (string $key): bool => str_starts_with($key, 'OTEL_'),
    ARRAY_FILTER_USE_KEY
);

ksort($otel);

echo "Aspire.Hosting.PHP sample\n";
echo str_repeat('-', 40), "\n";
printf("PHP version : %s\n", PHP_VERSION);
printf("SAPI        : %s\n", PHP_SAPI);
printf("Server      : %s\n", $_SERVER['SERVER_SOFTWARE'] ?? 'unknown');
printf("Document root: %s\n", $_SERVER['DOCUMENT_ROOT'] ?? 'unknown');
printf("Process id  : %d\n", getmypid());
printf("Extensions  : %s\n", implode(', ', array_slice(get_loaded_extensions(), 0, 12)));

echo "\nOpenTelemetry environment\n";
echo str_repeat('-', 40), "\n";

if ($otel === []) {
    echo "(none injected)\n";
} else {
    foreach ($otel as $key => $value) {
        printf("%-34s %s\n", $key, $value);
    }
}
