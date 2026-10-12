using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


/// Loads YAML files from the local StreamingAssets folder.
/// Use this reader for Windows, macOS, and Linux desktop builds.
public static class DesktopYamlReader
{
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static Dictionary<string, string> Parse(string yamlText, string sourceName = "<memory>")
    {
        return YamlTextParser.Parse(yamlText, sourceName);
    }

    public static List<string> GetVariations(
        Dictionary<string, string> values,
        string baseId)
    {
        return YamlTextParser.GetVariations(values, baseId);
    }

    public static string GetRandomVariation(
        Dictionary<string, string> values,
        string baseId)
    {
        return YamlTextParser.GetRandomVariation(values, baseId);
    }

    public static async Task<Dictionary<string, string>> LoadAsync(
        string relativePath,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        string yamlText = await ReadTextAsync(relativePath, cancellationToken);
        return Parse(yamlText, relativePath);
    }

    public static Task<Dictionary<string, string>> LoadManyAsync(params string[] relativePaths)
    {
        return LoadManyAsync((IEnumerable<string>)relativePaths, CancellationToken.None);
    }

    public static async Task<Dictionary<string, string>> LoadManyAsync(
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        if (relativePaths == null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string> keyLocations =
            new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string yamlText = await ReadTextAsync(relativePath, cancellationToken);
            YamlTextParser.ParseInto(yamlText, relativePath, values, keyLocations);
        }

        return values;
    }

    public static async Task<string> ReadTextAsync(
        string relativePath,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested();
        string filePath = GetStreamingAssetsFilePath(relativePath);

        try
        {
            byte[] fileContents = await Task.Run(
                () => File.ReadAllBytes(filePath),
                cancellationToken);
            return StrictUtf8.GetString(fileContents);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "YAML file '" + relativePath + "' is not valid UTF-8.", exception);
        }
        catch (IOException exception)
        {
            throw new IOException(
                "Could not read YAML file '" + relativePath + "'.", exception);
        }
    }

    internal static class YamlTextParser
    {
        public static Dictionary<string, string> Parse(string yamlText, string sourceName)
        {
            if (yamlText == null)
            {
                throw new ArgumentNullException(nameof(yamlText));
            }

            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> keyLocations =
                new Dictionary<string, string>(StringComparer.Ordinal);

            ParseInto(yamlText, sourceName, values, keyLocations);
            return values;
        }

        public static List<string> GetVariations(
            Dictionary<string, string> values,
            string baseId)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (baseId == null)
            {
                throw new ArgumentNullException(nameof(baseId));
            }

            List<string> variations = new List<string>();
            string text;

            if (values.TryGetValue(baseId, out text))
            {
                variations.Add(text);
            }

            string prefix = baseId + "_";
            List<KeyValuePair<string, string>> numberedValues =
                new List<KeyValuePair<string, string>>();

            foreach (KeyValuePair<string, string> entry in values)
            {
                if (entry.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                    IsPositiveNumber(entry.Key.Substring(prefix.Length)))
                {
                    numberedValues.Add(entry);
                }
            }

            numberedValues.Sort(delegate(
                KeyValuePair<string, string> left,
                KeyValuePair<string, string> right)
            {
                return CompareVariationIds(left.Key, right.Key, prefix);
            });

            foreach (KeyValuePair<string, string> entry in numberedValues)
            {
                variations.Add(entry.Value);
            }

            return variations;
        }

        public static string GetRandomVariation(
            Dictionary<string, string> values,
            string baseId)
        {
            List<string> variations = GetVariations(values, baseId);

            if (variations.Count == 0)
            {
                return null;
            }

            return variations[UnityEngine.Random.Range(0, variations.Count)];
        }

        public static void ParseInto(
            string yamlText,
            string sourceName,
            Dictionary<string, string> values,
            Dictionary<string, string> keyLocations)
        {
            if (yamlText.Length > 0 && yamlText[0] == '\uFEFF')
            {
                yamlText = yamlText.Substring(1);
            }

            using (StringReader reader = new StringReader(yamlText))
            {
                string line;
                int lineNumber = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string trimmedLine = line.Trim();

                    if (trimmedLine.Length == 0 || trimmedLine[0] == '#')
                    {
                        continue;
                    }

                    if (line.IndexOf('\uFFFD') >= 0)
                    {
                        Warn(sourceName, lineNumber,
                            "Found the Unicode replacement character (�). Check the file encoding.");
                    }

                    int separatorIndex = line.IndexOf(':');

                    if (separatorIndex <= 0)
                    {
                        Warn(sourceName, lineNumber, "Expected key: \"value\".");
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string valueText = line.Substring(separatorIndex + 1).Trim();

                    if (key.Length == 0)
                    {
                        Warn(sourceName, lineNumber, "The key cannot be empty.");
                        continue;
                    }

                    string value;
                    string parsingError;

                    if (!TryReadQuotedString(valueText, out value, out parsingError))
                    {
                        Warn(sourceName, lineNumber, parsingError);
                        continue;
                    }

                    string previousLocation;

                    if (keyLocations.TryGetValue(key, out previousLocation))
                    {
                        Warn(sourceName, lineNumber,
                            "Duplicate key '" + key + "'. First defined at " + previousLocation + ".");
                        continue;
                    }

                    values.Add(key, value);
                    keyLocations.Add(key, sourceName + ":" + lineNumber);
                }
            }
        }

        private static bool IsPositiveNumber(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            bool hasNonZeroDigit = false;

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];

                if (character < '0' || character > '9')
                {
                    return false;
                }

                if (character != '0')
                {
                    hasNonZeroDigit = true;
                }
            }

            return hasNonZeroDigit;
        }

        private static int CompareVariationIds(string left, string right, string prefix)
        {
            string leftNumber = NormalizeNumber(left.Substring(prefix.Length));
            string rightNumber = NormalizeNumber(right.Substring(prefix.Length));

            int lengthComparison = leftNumber.Length.CompareTo(rightNumber.Length);

            if (lengthComparison != 0)
            {
                return lengthComparison;
            }

            int numberComparison =
                string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);

            if (numberComparison != 0)
            {
                return numberComparison;
            }

            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private static string NormalizeNumber(string number)
        {
            int firstNonZeroDigit = 0;

            while (firstNonZeroDigit < number.Length - 1 && number[firstNonZeroDigit] == '0')
            {
                firstNonZeroDigit++;
            }

            return number.Substring(firstNonZeroDigit);
        }

        private static bool TryReadQuotedString(
            string valueText,
            out string value,
            out string error)
        {
            value = null;
            error = null;

            if (valueText.Length == 0 || valueText[0] != '"')
            {
                error = "The value must begin with a double quote.";
                return false;
            }

            StringBuilder result = new StringBuilder();

            for (int index = 1; index < valueText.Length; index++)
            {
                char currentCharacter = valueText[index];

                if (currentCharacter == '"')
                {
                    string remainingText = valueText.Substring(index + 1).Trim();

                    if (remainingText.Length > 0 && remainingText[0] != '#')
                    {
                        error = "Unexpected characters after the closing quote.";
                        return false;
                    }

                    value = result.ToString();
                    return true;
                }

                if (currentCharacter != '\\')
                {
                    result.Append(currentCharacter);
                    continue;
                }

                if (index + 1 >= valueText.Length)
                {
                    error = "The string ends with an unfinished escape sequence.";
                    return false;
                }

                index++;

                switch (valueText[index])
                {
                    case 'n':
                        result.Append('\n');
                        break;
                    case '"':
                        result.Append('"');
                        break;
                    case '\\':
                        result.Append('\\');
                        break;
                    default:
                        error = "Unsupported escape sequence: \\" + valueText[index];
                        return false;
                }
            }

            error = "Missing closing double quote.";
            return false;
        }

        private static void Warn(string sourceName, int lineNumber, string message)
        {
            Debug.LogWarning("[YamlReader] " + sourceName + ":" + lineNumber + " - " + message);
        }
    }

    private static string GetStreamingAssetsFilePath(string relativePath)
    {
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            throw new PlatformNotSupportedException(
                "DesktopYamlReader supports Windows, macOS, and Linux desktop builds only. " +
                "Use MobileYamlReader on Android, iOS, or WebGL.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative file path is required.", nameof(relativePath));
        }

        string normalizedPath = relativePath.Replace('\\', '/');
        string[] pathSegments = normalizedPath.Split('/');

        foreach (string pathSegment in pathSegments)
        {
            if (string.IsNullOrEmpty(pathSegment) ||
                pathSegment == "." ||
                pathSegment == ".." ||
                pathSegment.IndexOf(':') >= 0)
            {
                throw new ArgumentException(
                    "The file path must stay inside StreamingAssets: " + relativePath,
                    nameof(relativePath));
            }
        }

        return Path.GetFullPath(Path.Combine(
            Application.streamingAssetsPath,
            normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
