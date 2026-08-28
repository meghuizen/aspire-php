<?php
header('Content-Type: text/plain; charset=utf-8');
echo "Aspire.Hosting.PHP - Apache with .htaccess\n";
echo str_repeat('-', 44), "\n";
printf("SAPI          : %s\n", PHP_SAPI);
printf("Server        : %s\n", $_SERVER['SERVER_SOFTWARE'] ?? 'unknown');
printf("Document root : %s\n", $_SERVER['DOCUMENT_ROOT'] ?? 'unknown');
printf("Request URI   : %s\n", $_SERVER['REQUEST_URI'] ?? '');
printf("Thread safety : %s\n", PHP_ZTS ? 'ZTS' : 'NTS');
