using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;


/// Loads YAML files from StreamingAssets with UnityWebRequest.
/// Use this reader for Android, iOS, WebGL, and desktop builds.
public static class MobileYamlReader
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
        string fileUrl = GetStreamingAssetsUrl(relativePath);

        using (UnityWebRequest request = UnityWebRequest.Get(fileUrl))
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException(
                    "Could not read YAML file '" + relativePath + "': " + request.error);
            }

            try
            {
                return StrictUtf8.GetString(request.downloadHandler.data);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "YAML file '" + relativePath + "' is not valid UTF-8.", exception);
            }
        }
    }

    private static string GetStreamingAssetsUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative file path is required.", nameof(relativePath));
        }

        string normalizedPath = relativePath.Replace('\\', '/');
        string[] pathSegments = normalizedPath.Split('/');
        StringBuilder escapedPath = new StringBuilder();

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

            if (escapedPath.Length > 0)
            {
                escapedPath.Append('/');
            }

            escapedPath.Append(Uri.EscapeDataString(pathSegment));
        }

        string streamingAssetsPath = Application.streamingAssetsPath;

        if (streamingAssetsPath.Contains("://"))
        {
            return streamingAssetsPath.TrimEnd('/') + "/" + escapedPath;
        }

        string localPath = Path.Combine(
            streamingAssetsPath,
            normalizedPath.Replace('/', Path.DirectorySeparatorChar));

        return new Uri(Path.GetFullPath(localPath)).AbsoluteUri;
    }
}
