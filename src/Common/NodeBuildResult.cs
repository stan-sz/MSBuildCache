﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;

namespace Microsoft.MSBuildCache;

public sealed class NodeBuildResult
{
    public const uint CurrentVersion = 1;

    [JsonConstructor]
    public NodeBuildResult(
        SortedDictionary<string, ContentHash> outputs,
        SortedDictionary<string, string> packageFilesToCopy,
        IReadOnlyList<NodeTargetResult> targetResults,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        string? buildId)
    {
        Outputs = outputs;
        PackageFilesToCopy = packageFilesToCopy;
        TargetResults = targetResults;
        StartTimeUtc = startTimeUtc;
        EndTimeUtc = endTimeUtc;
        BuildId = buildId;
    }

    // Use a sorted dictionary so the JSON output is deterministically sorted and easier to compare build-to-build.
    // These paths are repo-relative.
    [JsonConverter(typeof(SortedDictionaryConverter))]
    public SortedDictionary<string, ContentHash> Outputs { get; }

    // Use a sorted dictionary so the JSON output is deterministically sorted and easier to compare build-to-build.
    public SortedDictionary<string, string> PackageFilesToCopy { get; }

    public IReadOnlyList<NodeTargetResult> TargetResults { get; }

    public DateTime StartTimeUtc { get; }

    public DateTime EndTimeUtc { get; }

    public string? BuildId { get; }

    public static NodeBuildResult FromBuildResult(
        SortedDictionary<string, ContentHash> outputs,
        SortedDictionary<string, string> packageFilesToCopy,
        BuildResult buildResult,
        DateTime creationTimeUtc,
        DateTime endTimeUtc,
        string? buildId,
        PathNormalizer pathNormalizer)
    {
        List<NodeTargetResult> targetResults = new(buildResult.ResultsByTarget.Count);
        foreach (KeyValuePair<string, TargetResult> kvp in buildResult.ResultsByTarget)
        {
            targetResults.Add(NodeTargetResult.FromTargetResult(kvp.Key, kvp.Value, pathNormalizer));
        }

        return new NodeBuildResult(outputs, packageFilesToCopy, targetResults, creationTimeUtc, endTimeUtc, buildId);
    }

    public CacheResult ToCacheResult(PathNormalizer pathNormalizer)
    {
        List<PluginTargetResult> targetResults = new(TargetResults.Count);
        foreach (NodeTargetResult targetResult in TargetResults)
        {
            targetResults.Add(targetResult.ToPluginTargetResult(pathNormalizer));
        }

        return CacheResult.IndicateCacheHit(targetResults);
    }

    private sealed class SortedDictionaryConverter : JsonConverter<SortedDictionary<string, ContentHash>>
    {
        public override SortedDictionary<string, ContentHash>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var contentHashConverter = (JsonConverter<ContentHash>)options.GetConverter(typeof(ContentHash));
            var outputs = new SortedDictionary<string, ContentHash>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException($"Unexpected token: {reader.TokenType}");
                }

                string propertyName = reader.GetString()!;
                if (!reader.Read())
                {
                    throw new JsonException($"Property name '{propertyName}' does not have a value.");
                }

                ContentHash? contentHash = contentHashConverter.Read(ref reader, typeof(ContentHash), options);
                if (contentHash == null)
                {
                    throw new JsonException($"Property value for '{propertyName}' could not be parsed.");
                }

                outputs.Add(propertyName, contentHash.Value);
            }

            return outputs;
        }

        public override void Write(Utf8JsonWriter writer, SortedDictionary<string, ContentHash> value, JsonSerializerOptions options)
        {
            var defaultConverter = (JsonConverter<SortedDictionary<string, ContentHash>>)
                options.GetConverter(typeof(SortedDictionary<string, ContentHash>));
            defaultConverter.Write(writer, value, options);
        }
    }
}