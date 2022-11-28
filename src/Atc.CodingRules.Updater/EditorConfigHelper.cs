// ReSharper disable InvertIf
// ReSharper disable ReturnTypeCanBeEnumerable.Local
// ReSharper disable SuggestBaseTypeForParameter
// ReSharper disable ReplaceSubstringWithRangeIndexer
// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
namespace Atc.CodingRules.Updater;

public static class EditorConfigHelper
{
    public const string FileName = ".editorconfig";
    public const string SectionDivider = "##########################################";
    public const string CustomSectionHeaderPrefix = "# Custom - ";
    public const string CustomSectionHeaderCodeAnalyzersRulesSuffix = "Code Analyzers Rules";
    public const string CustomSectionFirstLine = "[*.{cs,csx,cake}]";
    public const string AutogeneratedCustomSectionHeaderPrefix = "# ATC temporary suppressions";

    public static void HandleFile(
        ILogger logger,
        string area,
        string rawCodingRulesDistribution,
        DirectoryInfo path,
        string urlPart)
    {
        ArgumentNullException.ThrowIfNull(path);

        var descriptionPart = string.IsNullOrEmpty(urlPart)
            ? $"[yellow]root: [/]{FileName}"
            : $"[yellow]{urlPart}: [/]{FileName}";

        var file = new FileInfo(Path.Combine(path.FullName, FileName));

        var rawGitUrl = string.IsNullOrEmpty(urlPart)
            ? $"{rawCodingRulesDistribution}/{FileName}"
            : $"{rawCodingRulesDistribution}/{urlPart}/{FileName}";

        var displayName = rawGitUrl.Replace(Constants.GitRawContentUrl, Constants.GitHubPrefix, StringComparison.Ordinal);

        try
        {
            if (!file.Directory!.Exists)
            {
                Directory.CreateDirectory(file.Directory.FullName);
            }

            var contentGit = HttpClientHelper.GetAsString(logger, rawGitUrl, displayName).TrimEndForEmptyLines();
            var contentFile = FileHelper.ReadAllText(file);

            HandleFile(logger, area, contentGit, contentFile, descriptionPart, file);
        }
        catch (Exception ex)
        {
            logger.LogError($"{EmojisConstants.Error} {area} - {ex.Message}");
            throw;
        }
    }

    public static void HandleFile(
        ILogger logger,
        string area,
        string contentGit,
        string contentFile,
        string descriptionPart,
        FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(contentGit);
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            if (FileHelper.IsFileDataLengthEqual(contentGit, contentFile))
            {
                logger.LogInformation($"{EmojisConstants.FileNotUpdated}   {descriptionPart} nothing to update");
                return;
            }

            if (string.IsNullOrEmpty(contentFile))
            {
                FileHelper.CreateFile(logger, file, contentGit, descriptionPart);
                return;
            }

            var contentGitBasePart = ExtractContentBasePart(contentGit);
            var contentFileBasePart = ExtractContentBasePart(contentFile);

            if (FileHelper.IsFileDataLengthEqual(contentGitBasePart, contentFileBasePart))
            {
                logger.LogInformation($"{EmojisConstants.FileNotUpdated}   {descriptionPart} nothing to update");
                return;
            }

            UpdateFile(logger, contentGit, contentFile, file, descriptionPart, contentGitBasePart);
        }
        catch (Exception ex)
        {
            logger.LogError($"{EmojisConstants.Error} {area} - {ex.Message}");
            throw;
        }
    }

    public static Task<string> ReadAllText(
        DirectoryInfo path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var file = new FileInfo(Path.Combine(path.FullName, FileName));
        return File.ReadAllTextAsync(file.FullName, Encoding.UTF8, cancellationToken);
    }

    public static Task WriteAllText(
        DirectoryInfo path,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var file = new FileInfo(Path.Combine(path.FullName, FileName));
        return File.WriteAllTextAsync(file.FullName, content, Encoding.UTF8, cancellationToken);
    }

    public static Task UpdateRootFileRemoveCustomAtcAutogeneratedRuleSuppressions(
        DirectoryInfo projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var rootEditorConfigFile = new FileInfo(Path.Combine(projectPath.FullName, FileName));
        var rawFileData = FileHelper.ReadAllText(rootEditorConfigFile);
        var lines = rawFileData.Split(FileHelper.LineBreaks, StringSplitOptions.None);
        var hasSection = lines.Any(x => x.Equals(AutogeneratedCustomSectionHeaderPrefix, StringComparison.Ordinal));
        if (!hasSection)
        {
            return Task.CompletedTask;
        }

        var linesBefore = ExtractBeforeCustomAutogeneratedRulesHeader(lines);
        var linesAfter = ExtractAfterCustomAutogeneratedRulesContent(lines);

        var linesToWrite = new List<string>();
        linesToWrite.AddRange(linesBefore);
        linesToWrite.AddRange(linesAfter);

        var contentToWrite = linesToWrite.TrimEndForEmptyValuesToString();
        return FileHelper.WriteAllTextAsync(rootEditorConfigFile, contentToWrite);
    }

    public static Task UpdateRootFileAddCustomAtcAutogeneratedRuleSuppressions(
        DirectoryInfo projectPath,
        IList<Tuple<string, List<string>>> suppressionLinesPrAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(suppressionLinesPrAnalyzer);

        var rootEditorConfigFile = new FileInfo(Path.Combine(projectPath.FullName, FileName));
        var rawFileData = FileHelper.ReadAllText(rootEditorConfigFile);
        var lines = rawFileData.Split(FileHelper.LineBreaks, StringSplitOptions.None).ToList();

        lines.Add(string.Empty);
        lines.Add(string.Empty);
        lines.Add(SectionDivider);
        lines.Add(AutogeneratedCustomSectionHeaderPrefix);
        lines.Add($"# generated @ {DateTime.Now:F}");
        lines.Add("# Please fix all generated temporary suppressions");
        lines.Add("# either by code changes or move the");
        lines.Add("# suppressions one by one to the relevant");
        lines.Add("# 'Custom - Code Analyzers Rules' section.");
        lines.Add(SectionDivider);
        lines.Add("[*.cs]");
        foreach (var (analyzerName, suppressionLines) in suppressionLinesPrAnalyzer)
        {
            lines.Add($"{Environment.NewLine}# {analyzerName}");
            lines.AddRange(suppressionLines);
        }

        var contentToWrite = lines.TrimEndForEmptyValuesToString();
        return FileHelper.WriteAllTextAsync(rootEditorConfigFile, contentToWrite);
    }

    private static void UpdateFile(
        ILogger logger,
        string contentGit,
        string contentFile,
        FileInfo file,
        string descriptionPart,
        string contentGitBasePart)
    {
        var contentGitCustomParts = ExtractContentCustomParts(contentGit);
        var contentFileCustomParts = ExtractContentCustomParts(contentFile);

        MergeCustomPartsToFileCustomParts(contentGitCustomParts, contentFileCustomParts);

        EnsureCustomSectionCodeAnalyzersRulesFirstLine(contentGit, contentFileCustomParts);

        var newContentFile = BuildNewContentFile(contentGitBasePart, contentFileCustomParts);

        File.WriteAllText(file.FullName, newContentFile);
        logger.LogInformation($"{EmojisConstants.FileUpdated}   {descriptionPart} files merged");

        var customLines = contentFileCustomParts
            .Find(x => x.Item1.Equals(CustomSectionHeaderCodeAnalyzersRulesSuffix, StringComparison.Ordinal))
            ?.Item2;

        if (customLines is not null)
        {
            var gitKeyValues = contentGit.GetDotnetDiagnosticSeverityKeyValues();
            var fileKeyValues = contentFile.GetDotnetDiagnosticSeverityKeyValues();
            var fileCustomKeyValues = customLines.ToArray().GetDotnetDiagnosticSeverityKeyValues();
            LogSeverityDiffs(logger, gitKeyValues, fileKeyValues, fileCustomKeyValues, contentGit, newContentFile);
        }
    }

    private static void MergeCustomPartsToFileCustomParts(
        List<Tuple<string, List<string>>> contentGitCustomParts,
        List<Tuple<string, List<string>>> contentFileCustomParts)
    {
        foreach (var contentGitCustomPart in contentGitCustomParts)
        {
            if (!contentFileCustomParts.Any(x => x.Item1.Equals(contentGitCustomPart.Item1, StringComparison.Ordinal)))
            {
                if (contentFileCustomParts.Any(x =>
                        x.Item1.Equals(CustomSectionHeaderCodeAnalyzersRulesSuffix, StringComparison.Ordinal)))
                {
                    contentFileCustomParts.Insert(0, contentGitCustomPart);
                }
                else
                {
                    contentFileCustomParts.Add(contentGitCustomPart);
                }
            }
        }
    }

    private static void EnsureCustomSectionCodeAnalyzersRulesFirstLine(
        string contentGit,
        List<Tuple<string, List<string>>> contentFileCustomParts)
    {
        if (!contentGit.Contains("root = true", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (header, lines) in contentFileCustomParts)
        {
            if (header.Equals(CustomSectionHeaderCodeAnalyzersRulesSuffix, StringComparison.Ordinal) &&
                !lines.Any())
            {
                lines.Insert(0, CustomSectionFirstLine);
            }
        }
    }

    private static string BuildNewContentFile(
        string contentGitBasePart,
        List<Tuple<string, List<string>>> contentFileCustomParts)
    {
        var sbNewContentFile = new StringBuilder();
        sbNewContentFile.Append(contentGitBasePart);
        if (contentFileCustomParts.Count > 0)
        {
            sbNewContentFile.AppendLine();
        }

        foreach (var (header, lines) in contentFileCustomParts)
        {
            sbNewContentFile.AppendLine();
            sbNewContentFile.AppendLine();
            sbNewContentFile.AppendLine(SectionDivider);
            sbNewContentFile.AppendLine(CustomSectionHeaderPrefix + header);
            sbNewContentFile.AppendLine(SectionDivider);
            foreach (var line in lines)
            {
                sbNewContentFile.AppendLine(line);
            }
        }

        var newContentFile = sbNewContentFile
            .ToString()
            .TrimEndForEmptyLines();

        return newContentFile;
    }

    private static string ExtractContentBasePart(
        string content)
    {
        var lines = content.Split(FileHelper.LineBreaks, StringSplitOptions.None);

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Equals(SectionDivider, StringComparison.Ordinal) &&
                i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1];
                if (nextLine.StartsWith(CustomSectionHeaderPrefix, StringComparison.Ordinal))
                {
                    return sb
                        .ToString()
                        .TrimEndForEmptyLines();
                }
            }

            sb.AppendLine(line);
        }

        return sb
            .ToString()
            .TrimEndForEmptyLines();
    }

    private static List<Tuple<string, List<string>>> ExtractContentCustomParts(
        string content)
    {
        var customParts = new List<Tuple<string, List<string>>>();

        var lines = content.Split(FileHelper.LineBreaks, StringSplitOptions.None);

        var workingOnCustomHeader = string.Empty;
        var workingOnCustomLines = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Equals(SectionDivider, StringComparison.Ordinal) &&
                i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1];
                if (nextLine.StartsWith(CustomSectionHeaderPrefix, StringComparison.Ordinal))
                {
                    if (workingOnCustomHeader.Length != 0)
                    {
                        workingOnCustomLines.TrimEndForEmptyValues();
                        customParts.Add(new Tuple<string, List<string>>(workingOnCustomHeader, workingOnCustomLines));
                    }

                    workingOnCustomHeader = nextLine.Substring(CustomSectionHeaderPrefix.Length).Trim();
                    workingOnCustomLines = new List<string>();
                }
            }

            if (workingOnCustomHeader.Length > 0 &&
                !(line.Equals(SectionDivider, StringComparison.Ordinal) ||
                  line.StartsWith(CustomSectionHeaderPrefix, StringComparison.Ordinal)))
            {
                workingOnCustomLines.Add(line);
            }
        }

        if (workingOnCustomHeader.Length > 0 &&
            !customParts.Any(x => x.Item1.Equals(workingOnCustomHeader, StringComparison.Ordinal)))
        {
            workingOnCustomLines.TrimEndForEmptyValues();
            customParts.Add(new Tuple<string, List<string>>(workingOnCustomHeader, workingOnCustomLines));
        }

        return customParts;
    }

    [SuppressMessage("Performance", "MA0098:Use indexer instead of LINQ methods", Justification = "OK.")]
    private static string[] ExtractBeforeCustomAutogeneratedRulesHeader(
        IEnumerable<string> lines)
    {
        var result = lines
            .TakeWhile(line => !line.Equals(AutogeneratedCustomSectionHeaderPrefix, StringComparison.Ordinal))
            .ToList();

        if (result.Last().Equals(SectionDivider, StringComparison.Ordinal))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result.ToArray();
    }

    private static string[] ExtractAfterCustomAutogeneratedRulesContent(
        IEnumerable<string> lines)
    {
        var result = new List<string>();
        var foundHeader = false;
        var foundSectionDividerStart = false;
        var foundSectionDividerEnd = false;
        foreach (var line in lines)
        {
            switch (foundHeader)
            {
                case false when line.Equals(AutogeneratedCustomSectionHeaderPrefix, StringComparison.Ordinal):
                    foundHeader = true;
                    continue;
                case false:
                    continue;
            }

            if (line.Equals(SectionDivider, StringComparison.Ordinal))
            {
                if (!foundSectionDividerStart)
                {
                    foundSectionDividerStart = true;
                    continue;
                }

                if (!foundSectionDividerEnd)
                {
                    foundSectionDividerEnd = true;
                }
            }

            if (foundSectionDividerStart && foundSectionDividerEnd)
            {
                result.Add(line);
            }
        }

        return result.ToArray();
    }

    private static void LogSeverityDiffs(
        ILogger logger,
        IEnumerable<KeyValueItem> gitKeyValues,
        IReadOnlyCollection<KeyValueItem> fileKeyValues,
        IReadOnlyCollection<KeyValueItem> fileCustomKeyValues,
        string contentGit,
        string contentFile)
    {
        var gitLines = contentGit.Split(FileHelper.LineBreaks, StringSplitOptions.None);
        var fileLines = contentFile.Split(FileHelper.LineBreaks, StringSplitOptions.None);

        foreach (var gitKeyValue in gitKeyValues)
        {
            var key = gitKeyValue.Key;
            var item = fileCustomKeyValues.FirstOrDefault(x => x.Key.Equals(key, StringComparison.Ordinal));
            if (item != null)
            {
                // Duplicate
                var gitLineNumber = GetLineNumberForwardSearch(gitLines, key);
                var fileLineNumber = GetLineNumberReverseSearch(fileLines, item);

                logger.LogWarning($"{AppEmojisConstants.DuplicateKey}   Duplicate key: {key}");
                logger.LogWarning($"{FormattableString.Invariant($"     -- GitHub section (line {gitLineNumber:0000}): ")}{gitKeyValue.Value.Trim()}");
                logger.LogWarning($"{FormattableString.Invariant($"     -- Custom section (line {fileLineNumber:0000}): ")}{item.Value.Trim()}");
            }
            else if (!fileKeyValues.Any(x => x.Key.Equals(key, StringComparison.Ordinal)))
            {
                // New
                logger.LogDebug($"     - New key/value - {key}={gitKeyValue.Value}");
            }
        }
    }

    private static int GetLineNumberForwardSearch(
        IReadOnlyList<string> lines,
        string key)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(key, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static int GetLineNumberReverseSearch(
        IReadOnlyList<string> lines,
        KeyValueItem keyValueItem)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Equals($"{keyValueItem.Key}={keyValueItem.Value}", StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return -1;
    }
}