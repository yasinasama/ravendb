﻿using System.Collections.Generic;
using Raven.Client.Extensions;
using Sparrow.Json.Parsing;

namespace Raven.Client.ServerWide.ETL
{
    public class EtlProcessState
    {
        public EtlProcessState()
        {
            LastProcessedEtagPerNode = new Dictionary<string, long>();
            ChangeVector = null;
        }

        public string ConfigurationName { get; set; }

        public string TransformationName { get; set; }

        public Dictionary<string, long> LastProcessedEtagPerNode { get; set; }

        public string ChangeVector { get; set; }

        public long GetLastProcessedEtagForNode(string nodeTag)
        {
            if (LastProcessedEtagPerNode.TryGetValue(nodeTag, out var etag))
                return etag;

            return 0;
        }

        public DynamicJsonValue ToJson()
        {
            var json = new DynamicJsonValue
            {
                [nameof(ConfigurationName)] = ConfigurationName,
                [nameof(TransformationName)] = TransformationName,
                [nameof(LastProcessedEtagPerNode)] = LastProcessedEtagPerNode.ToJson(),
                [nameof(ChangeVector)] = ChangeVector
            };

            return json;
        }

        public static string GenerateItemName(string databaseName, string configurationName, string transformationName)
        {
            return $"values/{databaseName}/etl/{configurationName.ToLowerInvariant()}/{transformationName.ToLowerInvariant()}";
        }
    }
}