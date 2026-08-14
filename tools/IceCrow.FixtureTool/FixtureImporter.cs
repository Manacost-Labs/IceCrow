using System.Text;
using IceCrow.Recording;

namespace IceCrow.FixtureTool;

public sealed record FixtureImportOptions(
    string Name,
    string SourceType,
    string Reason,
    string? HearthstoneVersion = null);

public static class FixtureImporter
{
    public const string RecordingFileName = "recording.icecrow.json";
    public const string ManifestFileName = "expected.json";

    public static async Task<string> ImportAsync(
        string inputRecording,
        string outputDirectory,
        FixtureImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRecording);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(options);
        if (options.SourceType is not (FixtureSourceTypes.Synthetic or FixtureSourceTypes.RealAnonymized))
        {
            throw new ArgumentException(
                "Source type must be explicitly set to synthetic or real-anonymized.",
                nameof(options));
        }

        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new IOException(
                $"Candidate fixture output already exists: '{output}'. Refusing to overwrite it.");
        }

        var parent = Path.GetDirectoryName(output) ??
                     throw new ArgumentException("Output directory has no parent.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".icecrow-fixture-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporary);

        try
        {
            var source = await RecordingSerializer
                .LoadAsync(Path.GetFullPath(inputRecording), cancellationToken)
                .ConfigureAwait(false);
            var sanitized = new RecordingAnonymizer().Anonymize(source);
            RecordingAnonymizer.ValidateSanitized(sanitized);
            var manifest = new FixtureManifest
            {
                SchemaVersion = FixtureManifest.CurrentSchemaVersion,
                Name = RecordingAnonymizer.SanitizeFreeText(options.Name)!,
                SourceType = options.SourceType,
                HearthstoneVersion = RecordingAnonymizer.SanitizeFreeText(
                    options.HearthstoneVersion),
                IceCrowFormatVersion = sanitized.FormatVersion,
                Reason = RecordingAnonymizer.SanitizeFreeText(options.Reason)!,
                InputType = FixtureInputTypes.NormalizedRecording,
                InputFile = RecordingFileName,
                ExpectedCheckpoints = FixtureExpectedStateBuilder.Build(sanitized),
            };
            FixtureManifestSerializer.Validate(manifest);

            await RecordingSerializer
                .SaveAsync(
                    Path.Combine(temporary, RecordingFileName),
                    sanitized,
                    cancellationToken)
                .ConfigureAwait(false);
            await FixtureManifestSerializer
                .SaveAsync(
                    Path.Combine(temporary, ManifestFileName),
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    Path.Combine(temporary, "README.md"),
                    CreateReadme(manifest),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            _ = await FixtureGoldenRunner.RunAsync(temporary, cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(temporary, output);
            return output;
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static string CreateReadme(FixtureManifest manifest) =>
        $"# {manifest.Name}{Environment.NewLine}{Environment.NewLine}" +
        $"- Source: `{manifest.SourceType}`{Environment.NewLine}" +
        $"- IceCrow recording format: `{manifest.IceCrowFormatVersion}`{Environment.NewLine}" +
        $"- Hearthstone version: `{manifest.HearthstoneVersion ?? "unknown"}`{Environment.NewLine}" +
        $"- Reason: {manifest.Reason}{Environment.NewLine}{Environment.NewLine}" +
        "`expected.json` was generated as a candidate template. Review and reduce its " +
        $"semantic checkpoints before committing this fixture.{Environment.NewLine}";
}
