namespace Aspire.Hosting.PHP;

/// <summary>
/// Names of PHP extensions, for use with <c>WithPhpExtension</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>WithPhpExtension</c> takes any string, so these are a convenience rather than a restriction — they save
/// guessing between spellings such as <c>pdo_pgsql</c> and <c>pdo-postgres</c>, which fail only at image build
/// time.
/// </para>
/// <para>
/// Members marked <b>already present</b> ship in the default base images. Requesting one is harmless but does
/// nothing: the extension installer will not replace an extension that is already installed. That also means
/// such an extension cannot gain optional features it was not originally built with — see
/// <see cref="PhpOptimizationOptions.IgbinaryForRedis"/> for the one case where that matters.
/// </para>
/// </remarks>
public static class PhpExtensions
{
    // ---- Databases ----

    /// <summary>MySQL and MariaDB through PDO. <b>Already present.</b></summary>
    public const string PdoMySql = "pdo_mysql";

    /// <summary>PostgreSQL through PDO. <b>Already present.</b></summary>
    public const string PdoPostgreSql = "pdo_pgsql";

    /// <summary>SQLite through PDO. <b>Already present.</b></summary>
    public const string PdoSqlite = "pdo_sqlite";

    /// <summary>
    /// Microsoft SQL Server through PDO.
    /// </summary>
    /// <remarks>
    /// Pulls in Microsoft's ODBC driver, which adds roughly 12 MB to the image. It does build on Alpine —
    /// the driver supports musl — so the default base images do not have to be changed for it.
    /// </remarks>
    public const string PdoSqlServer = "pdo_sqlsrv";

    /// <summary>Microsoft SQL Server through the sqlsrv API rather than PDO.</summary>
    public const string SqlServer = "sqlsrv";

    /// <summary>
    /// MySQL through the mysqli API rather than PDO.
    /// </summary>
    /// <remarks>Needed by WordPress and Joomla, which call mysqli directly rather than going through PDO.</remarks>
    public const string MySqli = "mysqli";

    /// <summary>PostgreSQL through the pgsql API rather than PDO.</summary>
    public const string PostgreSql = "pgsql";

    /// <summary>MongoDB.</summary>
    public const string MongoDb = "mongodb";

    /// <summary>LDAP directory access.</summary>
    public const string Ldap = "ldap";

    // ---- Caching and serialization ----

    /// <summary>Redis client. <b>Already present</b>, but built without igbinary support.</summary>
    public const string Redis = "redis";

    /// <summary>Memcached client.</summary>
    public const string Memcached = "memcached";

    /// <summary>Local in-memory key/value cache, per process rather than shared.</summary>
    public const string Apcu = "apcu";

    /// <summary>Compact binary replacement for <c>serialize()</c>, roughly half the size.</summary>
    public const string Igbinary = "igbinary";

    /// <summary>MessagePack serialization, for interchange with non-PHP consumers.</summary>
    public const string MsgPack = "msgpack";

    // ---- Performance ----

    /// <summary>Bytecode cache. <b>Already present</b> and configured by <c>WithPhpOptimizations</c>.</summary>
    public const string Opcache = "opcache";

    /// <summary>
    /// Efficient collection types: <c>Ds\Map</c>, <c>Ds\Set</c>, <c>Ds\Seq</c>, <c>Ds\Heap</c>, <c>Ds\Pair</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PHP arrays are hash tables with substantial per-element overhead, so these use far less memory for large
    /// collections. Niche in practice: neither Laravel nor Symfony uses them, so the gain is confined to your
    /// own hot paths, and under a per-request model a structure is built and destroyed within one request.
    /// </para>
    /// <para>
    /// Version 2.0.0 removed <c>Ds\Vector</c>, <c>Ds\Deque</c>, <c>Ds\Stack</c>, <c>Ds\Queue</c> and
    /// <c>Ds\PriorityQueue</c>. Most existing code and documentation targets the older API and will not run
    /// against it.
    /// </para>
    /// </remarks>
    public const string DataStructures = "ds";

    /// <summary>
    /// SIMD-accelerated JSON parsing, several times faster than <c>json_decode</c> on large documents.
    /// </summary>
    /// <remarks>
    /// Worth it for parsing large JSON payloads; irrelevant for small ones, where the cost is not in parsing.
    /// Adds roughly 5 MB to the image.
    /// </remarks>
    public const string SimdJson = "simdjson";

    /// <summary>Zstandard compression.</summary>
    public const string Zstd = "zstd";

    /// <summary>LZ4 compression.</summary>
    public const string Lz4 = "lz4";

    // ---- Images, text and web ----

    /// <summary>Image processing. Needed by every CMS here for thumbnails.</summary>
    public const string Gd = "gd";

    /// <summary>ImageMagick, more capable than <see cref="Gd"/> and considerably larger.</summary>
    public const string Imagick = "imagick";

    /// <summary>Reads image metadata. WordPress uses it when handling uploads.</summary>
    public const string Exif = "exif";

    /// <summary>Internationalisation: collation, formatting, transliteration.</summary>
    public const string Intl = "intl";

    /// <summary>Archive handling. <b>Already present.</b></summary>
    public const string Zip = "zip";

    /// <summary>Arbitrary precision arithmetic, for money and other values that must not use floats.</summary>
    public const string BcMath = "bcmath";

    /// <summary>SOAP client and server.</summary>
    public const string Soap = "soap";

    /// <summary>XSLT transformation.</summary>
    public const string Xsl = "xsl";

    /// <summary>Low-level sockets.</summary>
    public const string Sockets = "sockets";

    // ---- Observability and development ----

    /// <summary>
    /// Zero-code OpenTelemetry instrumentation. Configured by <c>WithOpenTelemetry</c>.
    /// </summary>
    public const string OpenTelemetry = "opentelemetry";

    /// <summary>
    /// Step debugging and profiling. Configured by <c>WithXdebug</c>.
    /// </summary>
    /// <remarks>Never ship this to production: it is a large slowdown and exposes execution detail.</remarks>
    public const string Xdebug = "xdebug";

    /// <summary>Code coverage, far cheaper than <see cref="Xdebug"/> when coverage is all you need.</summary>
    public const string Pcov = "pcov";
}
