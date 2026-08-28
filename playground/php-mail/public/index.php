<?php
declare(strict_types=1);
header('Content-Type: text/plain; charset=utf-8');

$env = static fn (string $k): ?string => ($_SERVER[$k] ?? getenv($k)) ?: null;

echo "Aspire.Hosting.PHP - mail delivery\n", str_repeat('-', 44), "\n";
printf("MAIL_HOST     : %s\n", $env('MAIL_HOST') ?? '(unset)');
printf("MAIL_PORT     : %s\n", $env('MAIL_PORT') ?? '(unset)');
printf("MAIL_FROM     : %s\n", $env('MAIL_FROM_ADDRESS') ?? '(unset)');
printf("sendmail_path : %s\n", ini_get('sendmail_path') ?: '(default)');

// mail() does not speak SMTP on Linux; it pipes to sendmail_path, which the integration points at msmtp.
$ok = mail(
    'inbox@example.test',
    'Sent by the Aspire PHP sample',
    "If this is in Mailpit, mail() reached SMTP.\n"
);

printf("\nmail() returned: %s\n", $ok ? 'true' : 'FALSE');
