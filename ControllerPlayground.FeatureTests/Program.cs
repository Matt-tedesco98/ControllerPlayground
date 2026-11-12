using System.Diagnostics;
using System.Text.Json;

namespace ControllerPlayground.FeatureTests;

internal sealed record FeatureScenario(
    string Id, string Feature, string Layer, string Description, Action Execute);

internal sealed record ScenarioResult(
    string Id, string Feature, string Layer, string Status,
    long DurationMilliseconds, string? Error);

internal sealed record FeatureReport(
    int SchemaVersion, DateTimeOffset StartedUtc, int Total, int Passed, int Failed,
    IReadOnlyList<ScenarioResult> Results);

internal static class Program {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<FeatureScenario> Scenarios =
        RouterScenarios.All.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();

    private static int Main(string[] args) {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help") {
            Usage();
            return args.Length == 0 ? 2 : 0;
        }

        string command = args[0];
        if (command is not ("list" or "run"))
            return Error($"Unknown command: {command}");

        string? feature = null, id = null, reportPath = null;
        bool all = false, json = false;
        for (int i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--feature":
                    if (feature != null || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        return Error("--feature requires one value");
                    feature = args[++i]; break;
                case "--id":
                    if (id != null || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        return Error("--id requires one value");
                    id = args[++i]; break;
                case "--report":
                    if (reportPath != null || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        return Error("--report requires a file path");
                    reportPath = args[++i]; break;
                case "--all":
                    if (all) return Error("--all specified more than once");
                    all = true; break;
                case "--json":
                    json = true; break;
                default:
                    return Error($"Unknown argument: {args[i]}");
            }
        }

        if (command == "list" && (id != null || all || reportPath != null))
            return Error("list accepts only --feature and --json");

        int selectorCount = (all ? 1 : 0) + (feature != null ? 1 : 0) + (id != null ? 1 : 0);
        if (command == "run" && selectorCount != 1)
            return Error("Specify exactly one of --feature, --id, or --all");

        var selected = Scenarios.Where(s =>
            (feature == null || s.Feature == feature) &&
            (id == null || s.Id == id)).ToArray();

        if (command == "list") {
            if (json) {
                Console.WriteLine(JsonSerializer.Serialize(new {
                    schemaVersion = 1,
                    scenarios = selected.Select(s => new { s.Id, s.Feature, s.Layer, s.Description })
                }, JsonOptions));
            } else {
                foreach (var scenario in selected)
                    Console.WriteLine($"{scenario.Id} [{scenario.Layer}] - {scenario.Description}");
            }
            return 0;
        }

        if (selected.Length == 0) return Error("No registered scenarios match the selection");

        var started = DateTimeOffset.UtcNow;
        var results = new List<ScenarioResult>(selected.Length);
        foreach (var scenario in selected) {
            var watch = Stopwatch.StartNew();
            string status = "passed";
            string? error = null;
            try {
                scenario.Execute();
            } catch (Exception exception) {
                status = "failed";
                error = exception.ToString();
            }
            watch.Stop();
            results.Add(new(scenario.Id, scenario.Feature, scenario.Layer, status, watch.ElapsedMilliseconds, error));
        }

        int passed = results.Count(result => result.Status == "passed");
        var report = new FeatureReport(1, started, results.Count, passed, results.Count - passed, results);
        string reportJson = JsonSerializer.Serialize(report, JsonOptions);

        if (reportPath != null) {
            try {
                string path = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, reportJson + Environment.NewLine);
            } catch (Exception exception) {
                return Error($"Unable to save report: {exception.Message}");
            }
        }

        if (json) {
            Console.WriteLine(reportJson);
        } else {
            foreach (var result in results) {
                Console.WriteLine($"{result.Status.ToUpperInvariant()} {result.Id} ({result.DurationMilliseconds} ms)");
                if (result.Error != null) Console.WriteLine(result.Error);
            }
            Console.WriteLine($"Feature tests: {report.Passed}/{report.Total} passed");
        }

        return report.Failed == 0 ? 0 : 1;
    }

    private static int Error(string message) {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Use 'help' for usage.");
        return 2;
    }

    private static void Usage() {
        Console.WriteLine("""
            ControllerPlayground feature tests (registered, deterministic scenarios)

              list [--feature FEATURE] [--json]
              run (--feature FEATURE | --id SCENARIO | --all) [--json] [--report FILE]
              help

            Exit codes: 0 = all selected tests passed; 1 = test failure; 2 = usage/report error.
            Current layer: managed-router only. No WinUI, real GameInput or virtual HID device is exercised.
            """);
    }
}
