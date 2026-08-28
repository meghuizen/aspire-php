namespace Aspire.Hosting.PHP;

/// <summary>
/// The console command each framework uses for migrations, queue work and scheduling.
/// </summary>
/// <remarks>
/// These differ in more than spelling. Laravel routes everything through <c>artisan</c>; Symfony through
/// <c>bin/console</c>; Drupal uses a separate tool, <c>drush</c>, installed as a Composer dependency. WordPress
/// and Joomla have no migration or queue concept at all, which is reported rather than guessed at.
/// </remarks>
internal static class PhpFrameworkCommands
{
    /// <summary>
    /// The arguments for applying database migrations, or <see langword="null"/> when the framework has none.
    /// </summary>
    public static string[]? Migrate(PhpConnectionConvention convention) => convention switch
    {
        // --force because artisan refuses to migrate in production without it, and a published container is
        // production by definition.
        PhpConnectionConvention.Laravel => ["artisan", "migrate", "--force"],
        PhpConnectionConvention.Symfony => ["bin/console", "doctrine:migrations:migrate", "--no-interaction"],
        PhpConnectionConvention.Drupal => ["vendor/bin/drush", "updatedb", "--yes"],
        _ => null
    };

    /// <summary>
    /// The arguments for consuming a queue, or <see langword="null"/> when the framework has none.
    /// </summary>
    public static string[]? QueueWorker(PhpConnectionConvention convention) => convention switch
    {
        PhpConnectionConvention.Laravel => ["artisan", "queue:work"],
        PhpConnectionConvention.Symfony => ["bin/console", "messenger:consume", "async"],
        _ => null
    };

    /// <summary>
    /// The arguments for running scheduled tasks, or <see langword="null"/> when the framework has none.
    /// </summary>
    /// <remarks>
    /// Laravel's <c>schedule:work</c> is a long-running process that ticks every minute itself, so it needs no
    /// cron. Symfony has no equivalent built in; its scheduler runs as a Messenger transport, so that is what
    /// is used here.
    /// </remarks>
    public static string[]? Scheduler(PhpConnectionConvention convention) => convention switch
    {
        PhpConnectionConvention.Laravel => ["artisan", "schedule:work"],
        PhpConnectionConvention.Symfony => ["bin/console", "messenger:consume", "scheduler_default"],
        _ => null
    };

    /// <summary>
    /// A human-readable name for the convention, for error messages.
    /// </summary>
    public static string DisplayName(PhpConnectionConvention convention) => convention switch
    {
        PhpConnectionConvention.Laravel => "Laravel",
        PhpConnectionConvention.Symfony => "Symfony",
        PhpConnectionConvention.WordPress => "WordPress",
        PhpConnectionConvention.Drupal => "Drupal",
        PhpConnectionConvention.Joomla => "Joomla",
        _ => "PHP"
    };
}
