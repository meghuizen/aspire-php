<?php

declare(strict_types=1);

// Runs once, before the web app starts, as a WithPhpConsoleCommand resource. It gets the same DB_*
// variables the application does, which is what makes it a useful stand-in for a real migration step.
$env = static fn (string $k): ?string => ($_SERVER[$k] ?? getenv($k)) ?: null;

$dsn = sprintf(
    'mysql:host=%s;port=%s;dbname=%s',
    $env('DB_HOST') ?? 'localhost',
    $env('DB_PORT') ?? '3306',
    $env('DB_DATABASE') ?? ''
);

echo "seed: connecting to ", $env('DB_HOST') ?? '?', "\n";

$pdo = new PDO($dsn, $env('DB_USERNAME') ?? '', $env('DB_PASSWORD') ?? '', [
    PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
]);

$pdo->exec('CREATE TABLE IF NOT EXISTS aspire_seed (id INT PRIMARY KEY, note VARCHAR(64))');
$pdo->exec('DELETE FROM aspire_seed WHERE id = 1');
$pdo->exec("INSERT INTO aspire_seed (id, note) VALUES (1, 'seeded before the app started')");

echo "seed: done\n";
