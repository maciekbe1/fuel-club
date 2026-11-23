using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FuelClub.Simulator;

// Synthetic traffic for the demo instance (ADR 0017). Public HTTP API only.
//
//   populate --run <id> [--accounts 5] [--api http://localhost:5301] [--manifest path]
//   converse --run <id> [--conversations 10] [--chat-per-minute 60] [--regressions-each 3] [--pass v8]
//            (--pass: the same conversations again as new ones, e.g. after an adapter change)
//   cleanup  --run <id> [--manifest path]     (SIM_DEMO_DATABASE: connection string)
//   prune    [--days 14]                      (deletes transcripts and judge files past the retention)
//   judge    --run <id> [--random 40] [--errors 20] [--regressions 10] [--max-usd 0.20] [--model M]
//   calibrate --run <id> [--size 30]           (blind sheet for human grades)
//   agreement --run <id>                       (human vs judge on the filled sheet)
//   compare  --run <id> --pass <name>          (the same turns before and after, today's checks)
//   coupon-cases --run <id> [--api]            (11 coupon-selection cases on a fresh instance)
//   coupon-cases-report --run <id> --base <id> (the same cases from two runs side by side)
//
// The judge's Gemini key: GEMINI_API_KEY, else the API's user-secrets (Assistant:Gemini:ApiKey).
//
// Secrets come from the environment, never from arguments or the manifest:
//   SIM_SECRET (derives the accounts' passwords), SIM_CASHIER_EMAIL/PASSWORD, SIM_ADMIN_EMAIL/PASSWORD.

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

if (
    args.Length == 0
    || args[0]
        is not (
            "populate"
            or "converse"
            or "cleanup"
            or "prune"
            or "judge"
            or "calibrate"
            or "agreement"
            or "compare"
            or "coupon-cases"
            or "coupon-cases-report"
        )
)
{
    Console.WriteLine(
        "usage: populate|converse|cleanup|judge|calibrate|agreement|compare|coupon-cases|coupon-cases-report --run <id> [options] | prune [--days 14]"
    );
    return 64;
}

var options = Options.Parse(args[1..]);
if (args[0] == "prune")
{
    var removed = ChatScenarios.Prune(
        ".simulation",
        TimeSpan.FromDays(options.Int("days", 14)),
        DateTimeOffset.UtcNow
    );
    Console.WriteLine($"removed {removed} files (transcripts and judge files)");
    return 0;
}
var run = options.Get("run") ?? throw new ArgumentException("--run is required");
if (!Regex.IsMatch(run, "^[a-z0-9][a-z0-9-]{0,39}$"))
    throw new ArgumentException("--run: 1-40 characters a-z, 0-9, '-'");
var manifestPath = options.Get("manifest") ?? Path.Combine(".simulation", run, "manifest.json");

var pass = options.Get("pass");
if (pass is not null && !Regex.IsMatch(pass, "^[a-z0-9][a-z0-9-]{0,19}$"))
    throw new ArgumentException("--pass: 1-20 characters a-z, 0-9, '-'");

if (args[0] == "compare")
{
    if (pass is null)
        throw new ArgumentException("compare needs --pass");
    var baseFolder = Path.GetDirectoryName(manifestPath)!;
    var passFolder = Path.Combine(baseFolder, "passes", pass);
    if (!Directory.Exists(passFolder))
    {
        Console.WriteLine($"no pass {pass} in {baseFolder} (run converse --pass {pass} first)");
        return 1;
    }
    var (rows, changes, skipped) = Comparison.Compare(
        TranscriptFile.LoadRun(baseFolder),
        TranscriptFile.LoadRun(passFolder)
    );
    var comparison = Path.Combine(passFolder, "report-comparison.md");
    File.WriteAllText(comparison, Comparison.Report(run, pass, rows, changes, skipped));
    Console.WriteLine($"{changes.Count} changed turns: {comparison}");
    return 0;
}

if (args[0] == "coupon-cases-report")
{
    var baseRun = options.Get("base") ?? throw new ArgumentException("--base <run> is required");
    var load = (string id) =>
        System.Text.Json.JsonSerializer.Deserialize<CouponCases.ResultFile>(
            File.ReadAllText(Path.Combine(".simulation", id, "coupon-cases.json")),
            Manifest.Json
        )!;
    var report = Path.Combine(".simulation", run, $"report-coupon-cases-vs-{baseRun}.md");
    File.WriteAllText(report, CouponCases.Report(load(baseRun), load(run)));
    Console.WriteLine(report);
    return 0;
}

if (args[0] is "judge" or "calibrate" or "agreement")
    return await JudgeCommand.RunAsync(args[0], run, manifestPath, options, cancel.Token);

if (args[0] == "cleanup")
{
    var connection =
        Environment.GetEnvironmentVariable("SIM_DEMO_DATABASE")
        ?? throw new ArgumentException("SIM_DEMO_DATABASE is required for cleanup");
    var existing = Manifest.Load(manifestPath);
    if (existing.RunId != run)
        throw new ArgumentException($"The manifest belongs to run '{existing.RunId}'.");
    return await Cleanup.RunAsync(existing, connection, cancel.Token);
}

var api = options.Get("api") ?? "http://localhost:5301";

// Its own accounts and offers, no manifest: the run folder holds only the results.
var checking = args[0] == "coupon-cases";
if (args[0] == "converse" && !File.Exists(manifestPath))
    throw new ArgumentException("No manifest for this run: run populate first.");
var manifest =
    checking
        ? new Manifest
        {
            RunId = run,
            ApiUrl = api,
            SaleDate = DateOnly.FromDateTime(DateTime.Today),
        }
    : File.Exists(manifestPath) ? Manifest.Load(manifestPath)
    : Manifest.Create(manifestPath, run, api, options.Get("station-time-zone") ?? "Europe/Warsaw");
if (manifest.RunId != run || manifest.ApiUrl != api)
    throw new ArgumentException(
        $"The manifest is for run '{manifest.RunId}' at {manifest.ApiUrl}, not '{run}' at {api}."
    );
if (manifest.CleanedAt is not null)
    throw new InvalidOperationException("This run was cleaned up; start a new run id.");

// The server decides the run and the model; the simulator only checks it reached the right one.
string provider,
    modelName;
using (var probe = new HttpClient { BaseAddress = new Uri(api) })
{
    var response = await probe.GetAsync("/api/simulation", cancel.Token);
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine(
            $"Refused: {api} is not a simulation instance (/api/simulation: {(int)response.StatusCode})."
        );
        return 2;
    }
    var simulation = await response.Content.ReadFromJsonAsync<SimulationInfo>(cancel.Token);
    if (simulation?.RunId != run)
    {
        Console.WriteLine($"Refused: the instance runs '{simulation?.RunId}', not '{run}'.");
        return 2;
    }
    var expected = options.Get("expect-provider") ?? "demo";
    if (simulation.Provider != expected)
    {
        Console.WriteLine(
            $"Refused: the instance uses '{simulation.Provider}', expected '{expected}'."
        );
        return 2;
    }
    Console.WriteLine($"run {run} at {api}: {simulation.Provider}/{simulation.Model}");
    provider = simulation.Provider;
    modelName = simulation.Model;
}

var conversing = args[0] == "converse";
if (checking)
    return await CouponCases.RunAsync(
        api,
        run,
        Path.Combine(".simulation", run),
        new Settings(
            Required("SIM_SECRET"),
            Required("SIM_CASHIER_EMAIL"),
            Required("SIM_CASHIER_PASSWORD"),
            Required("SIM_ADMIN_EMAIL"),
            Required("SIM_ADMIN_PASSWORD"),
            1,
            8,
            50
        ),
        provider,
        modelName,
        cancel.Token
    );
var settings = new Settings(
    Secret: Required("SIM_SECRET"),
    CashierEmail: conversing ? "" : Required("SIM_CASHIER_EMAIL"),
    CashierPassword: conversing ? "" : Required("SIM_CASHIER_PASSWORD"),
    AdminEmail: conversing ? "" : Required("SIM_ADMIN_EMAIL"),
    AdminPassword: conversing ? "" : Required("SIM_ADMIN_PASSWORD"),
    Parallel: options.Int("parallel", 4),
    AuthPerMinute: options.Int("auth-per-minute", 8),
    PurchasesPerMinute: options.Int("purchases-per-minute", 50)
);
if (conversing)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    // A global pace; each account also stays under the API's 20 turns/min (ChatScenarios).
    var chat = new ChatScenarios(
        manifest,
        settings,
        options.Int("chat-per-minute", 60),
        options.Int("regressions-each", 3),
        pass
    );
    await chat.RunAsync(options.Int("conversations", 10), cancel.Token);
    var (technical, correctness) = Reports.Write(
        manifest,
        chat.RunFolder,
        pass,
        provider,
        modelName,
        clock.Elapsed,
        ApiSession.RateLimitWaits
    );
    Console.WriteLine($"reports: {technical}, {correctness}");
}
else
    await new Population(manifest, settings).RunAsync(options.Int("accounts", 5), cancel.Token);
return Environment.ExitCode;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new ArgumentException($"{name} is required (environment variable)");

namespace FuelClub.Simulator
{
    /// <summary>
    /// Paces default below the API's defaults (auth 10/min per IP, purchases 60/min per cashier);
    /// a demo instance with higher server limits can take higher values.
    /// </summary>
    public sealed record Settings(
        string Secret,
        string CashierEmail,
        string CashierPassword,
        string AdminEmail,
        string AdminPassword,
        int Parallel,
        int AuthPerMinute,
        int PurchasesPerMinute
    );

    internal sealed record SimulationInfo(string RunId, string Provider, string Model, bool Demo);

    internal sealed class Options(Dictionary<string, string> values)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>();
            for (var i = 0; i < args.Length; i++)
                if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
                    values[args[i][2..]] = args[++i];
                else
                    throw new ArgumentException($"Unexpected argument '{args[i]}'");
            return new Options(values);
        }

        public string? Get(string name) => values.GetValueOrDefault(name);

        public int Int(string name, int fallback) =>
            values.TryGetValue(name, out var value) ? int.Parse(value) : fallback;
    }
}
