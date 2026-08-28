<?php

declare(strict_types=1);

namespace Meghuizen\AspireAzure;

/**
 * Something went wrong getting a token.
 *
 * The messages carry the diagnosis rather than only the symptom, because the two common failures here --
 * running somewhere with no managed identity, and an identity that has not been granted anything -- look
 * identical from the application's side otherwise.
 */
final class IdentityException extends \RuntimeException
{
}
