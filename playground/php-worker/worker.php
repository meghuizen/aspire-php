<?php

declare(strict_types=1);

// A long-running worker. Aspire streams stdout into the dashboard, so plain echo is enough to see it working.
$iteration = 0;

while (true) {
    $iteration++;

    printf(
        "[%s] worker tick %d (php %s)%s",
        date('H:i:s'),
        $iteration,
        PHP_VERSION,
        PHP_EOL
    );

    // Flush explicitly: without it the output sits in PHP's buffer and the dashboard shows nothing.
    if (ob_get_level() > 0) {
        ob_flush();
    }
    flush();

    sleep(5);
}
