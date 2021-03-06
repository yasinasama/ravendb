﻿using System;
using System.Collections.Generic;
using Raven.Client.Documents.Indexes;
using Raven.Client.Util;
using Raven.Server.Exceptions;

namespace Raven.Server.Documents.Indexes
{
    public class IndexingRunStats
    {
        public int MapAttempts;
        public int MapSuccesses;
        public int MapErrors;

        public int ReduceAttempts;
        public int ReduceSuccesses;
        public int ReduceErrors;

        public int IndexingOutputs;
        public Size AllocatedBytes;

        public ReduceRunDetails ReduceDetails;

        public MapRunDetails MapDetails;

        public StorageCommitDetails CommitDetails;

        public List<IndexingError> Errors;

        public int MaxNumberOfOutputsPerDocument;

        public override string ToString()
        {
            return $"Map - attempts: {MapAttempts}, successes: {MapSuccesses}, errors: {MapErrors} / " +
                   $"Reduce - attempts: {ReduceAttempts}, successes: {ReduceSuccesses}, errors: {ReduceErrors}";
        }

        public void AddMapError(string key, string message)
        {
            AddError(key, message, "Map");
        }

        public void AddReduceError(string message)
        {
            AddError(null, message, "Reduce");
        }

        public void AddCorruptionError(Exception exception)
        {
            AddError(null, $"Index corruption occurred: {exception}", "Corruption");
        }

        public void AddWriteError(IndexWriteException exception)
        {
            AddError(null, $"Write exception occurred: {exception.Message}", "Write");
        }

        public void AddAnalyzerError(IndexAnalyzerException exception)
        {
            AddError(null, $"Could not create analyzer: {exception.Message}", "Analyzer");
        }

        public void AddUnexpectedError(Exception exception)
        {
            AddError(null, $"Unexpected exception occurred: {exception}", "Critical");
        }

        public void AddCriticalError(Exception exception)
        {
            AddError(null, $"Critical exception occurred: {exception}", "Critical");
        }

        public void AddMemoryError(OutOfMemoryException oome)
        {
            AddError(null, $"Memory exception occurred: {oome}", "Memory");
        }

        private void AddError(string key, string message, string action)
        {
            if (Errors == null)
                Errors = new List<IndexingError>();

            Errors.Add(new IndexingError
            {
                Action = action ?? string.Empty,
                Document = key ?? string.Empty,
                Timestamp = SystemTime.UtcNow,
                Error = message ?? string.Empty
            });
        }
    }
}
