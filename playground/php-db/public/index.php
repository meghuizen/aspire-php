<?php

declare(strict_types=1);

header('Content-Type: text/plain; charset=utf-8');

echo "Aspire.Hosting.PHP - database and cache references\n";
echo str_repeat('=', 52), "\n\n";

$env = static fn (string $key): ?string => ($_SERVER[$key] ?? getenv($key)) ?: null;

echo "Injected variables\n", str_repeat('-', 52), "\n";
foreach ($_SERVER as $key => $value) {
    if (preg_match('/^(DB_|REDIS_|DATABASE_URL)/', (string) $key)) {
        // Never echo a password back, even in a sample.
        $shown = str_contains($key, 'PASSWORD') || str_contains($key, 'URL') ? '***' : $value;
        printf("%-22s %s\n", $key, $shown);
    }
}

echo "\nDatabase\n", str_repeat('-', 52), "\n";
try {
    $driver = $env('DB_CONNECTION') === 'pgsql' ? 'pgsql' : 'mysql';

    $dsn = sprintf(
        '%s:host=%s;port=%s;dbname=%s',
        $driver,
        $env('DB_HOST') ?? 'localhost',
        $env('DB_PORT') ?? ($driver === 'pgsql' ? '5432' : '3306'),
        $env('DB_DATABASE') ?? ''
    );

    $pdo = new PDO($dsn, $env('DB_USERNAME') ?? '', $env('DB_PASSWORD') ?? '', [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_TIMEOUT => 5,
    ]);

    printf("CONNECTED  %s, server version %s\n", $driver, $pdo->query('SELECT VERSION()')->fetchColumn());

    $pdo->exec('CREATE TABLE IF NOT EXISTS aspire_check (id INT PRIMARY KEY, note VARCHAR(64))');
    $pdo->exec("DELETE FROM aspire_check WHERE id = 1");
    $pdo->exec("INSERT INTO aspire_check (id, note) VALUES (1, 'written by the Aspire sample')");
    printf("READ BACK  %s\n", $pdo->query('SELECT note FROM aspire_check WHERE id = 1')->fetchColumn());
} catch (Throwable $e) {
    printf("FAILED     %s\n", $e->getMessage());
}

echo "\nRedis\n", str_repeat('-', 52), "\n";
try {
    if (!extension_loaded('redis')) {
        throw new RuntimeException('the redis extension is not loaded');
    }

    // REDIS_URL is read rather than REDIS_HOST because it is the only value carrying the scheme, and Aspire
    // turns Redis TLS on by default while running. phpredis needs an explicit tls:// prefix for that; given
    // a bare host it connects in plaintext and fails with an unhelpful "read error on connection".
    $url = parse_url($env('REDIS_URL') ?? '') ?: [];
    $secure = ($url['scheme'] ?? 'redis') === 'rediss';

    $redis = new Redis();
    $redis->connect(
        ($secure ? 'tls://' : '') . ($url['host'] ?? $env('REDIS_HOST') ?? 'localhost'),
        (int) ($url['port'] ?? $env('REDIS_PORT') ?? 6379),
        5.0
    );

    $password = $env('REDIS_PASSWORD') ?? (isset($url['pass']) ? rawurldecode($url['pass']) : null);
    if ($password !== null) {
        $redis->auth($password);
    }

    $redis->set('aspire:check', 'written by the Aspire sample');
    printf("CONNECTED  tls=%s\n", $secure ? 'yes' : 'no');
    printf("READ BACK  %s\n", $redis->get('aspire:check'));
} catch (Throwable $e) {
    printf("FAILED     %s\n", $e->getMessage());
}
