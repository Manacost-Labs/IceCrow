namespace IceCrow.FixtureTool;

public static class FixtureToolCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Usage()).ConfigureAwait(false);
            return 0;
        }

        try
        {
            var options = ParseOptions(args.Skip(1));
            switch (args[0])
            {
                case "import":
                    var imported = await FixtureImporter.ImportAsync(
                            Require(options, "input"),
                            Require(options, "output"),
                            new FixtureImportOptions(
                                Require(options, "name"),
                                Require(options, "source-type"),
                                Require(options, "reason"),
                                Get(options, "hearthstone-version")),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await output.WriteLineAsync(
                            $"Candidate fixture created at: {imported}{Environment.NewLine}" +
                            "Review expected.json; nothing was committed.")
                        .ConfigureAwait(false);
                    return 0;

                case "validate":
                    var result = await FixtureGoldenRunner
                        .RunAsync(Require(options, "fixture"), cancellationToken)
                        .ConfigureAwait(false);
                    await output.WriteLineAsync(
                            $"Fixture '{result.FixtureName}' passed {result.Checkpoints.Count} checkpoints.")
                        .ConfigureAwait(false);
                    return 0;

                case "benchmark":
                    var benchmark = await HardeningBenchmarkRunner
                        .RunAsync(
                            Get(options, "repository") ?? Directory.GetCurrentDirectory(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await output.WriteLineAsync(HardeningBenchmarkRunner.Format(benchmark))
                        .ConfigureAwait(false);
                    return 0;

                default:
                    throw new ArgumentException($"Unknown command '{args[0]}'.");
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var enumerator = arguments.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var option = enumerator.Current;
            if (!option.StartsWith("--", StringComparison.Ordinal) || option.Length == 2)
            {
                throw new ArgumentException($"Expected an option, received '{option}'.");
            }

            if (!enumerator.MoveNext() || enumerator.Current.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{option}' requires a value.");
            }

            var key = option[2..];
            if (!values.TryAdd(key, enumerator.Current))
            {
                throw new ArgumentException($"Option '{option}' was supplied more than once.");
            }
        }

        return values;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string name) =>
        Get(options, name) ?? throw new ArgumentException($"Missing required option '--{name}'.");

    private static string? Get(IReadOnlyDictionary<string, string> options, string name) =>
        options.GetValueOrDefault(name);

    private static string Usage() =>
        "IceCrow fixture tool" + Environment.NewLine + Environment.NewLine +
        "Import and anonymize a captured recording:" + Environment.NewLine +
        "  import --input <recording> --output <new-directory> --name <name> " +
        "--source-type <synthetic|real-anonymized> --reason <reason> " +
        "[--hearthstone-version <version>]" + Environment.NewLine + Environment.NewLine +
        "Validate one candidate fixture:" + Environment.NewLine +
        "  validate --fixture <fixture-directory>" + Environment.NewLine + Environment.NewLine +
        "Run the non-gating hardening performance baseline:" + Environment.NewLine +
        "  benchmark [--repository <repository-root>]";
}
