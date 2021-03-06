﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Raven.Client.ServerWide.ETL;
using Raven.Server.Documents.ETL.Providers.SQL.Enumerators;
using Raven.Server.Documents.ETL.Providers.SQL.Metrics;
using Raven.Server.Documents.ETL.Providers.SQL.RelationalWriters;
using Raven.Server.Documents.ETL.Providers.SQL.Simulation;
using Raven.Server.Documents.ETL.Stats;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Providers.SQL
{
    public class SqlEtl : EtlProcess<ToSqlItem, SqlTableWithRecords, SqlEtlConfiguration, SqlConnectionString>
    {
        public const string SqlEtlTag = "SQL ETL";

        public readonly SqlEtlMetricsCountersManager SqlMetrics = new SqlEtlMetricsCountersManager();

        public SqlEtl(Transformation transformation, SqlEtlConfiguration configuration, DocumentDatabase database, ServerStore serverStore)
            : base(transformation, configuration, database, serverStore, SqlEtlTag)
        {
            Metrics = SqlMetrics;
        }

        protected override IEnumerator<ToSqlItem> ConvertDocsEnumerator(IEnumerator<Document> docs, string collection)
        {
            return new DocumentsToSqlItems(docs, collection);
        }

        protected override IEnumerator<ToSqlItem> ConvertTombstonesEnumerator(IEnumerator<DocumentTombstone> tombstones, string collection)
        {
            return new TombstonesToSqlItems(tombstones, collection);
        }

        protected override EtlTransformer<ToSqlItem, SqlTableWithRecords> GetTransformer(DocumentsOperationContext context)
        {
            return new SqlDocumentTransformer(Transformation, Database, context, Configuration);
        }

        protected override void LoadInternal(IEnumerable<SqlTableWithRecords> records, JsonOperationContext context)
        {
            using (var writer = new RelationalDatabaseWriter(this, Database))
            {
                foreach (var table in records)
                {
                    var stats = writer.Write(table, CancellationToken);

                    LogStats(stats, table);
                }

                writer.Commit();
            }
        }

        private void LogStats(SqlWriteStats stats, SqlTableWithRecords table)
        {
            if (table.Inserts.Count > 0)
            {
                if (Logger.IsInfoEnabled)
                {
                    Logger.Info($"[{Name}] Inserted {stats.InsertedRecordsCount} (out of {table.Inserts.Count}) records to '{table.TableName}' table " +
                        $"from the following documents: {string.Join(", ", table.Inserts.Select(x => x.DocumentId))}");
                }
            }

            if (table.Deletes.Count > 0)
            {
                if (Logger.IsInfoEnabled)
                {
                    Logger.Info($"[{Name}] Deleted {stats.DeletedRecordsCount} (out of {table.Deletes.Count}) records from '{table.TableName}' table " +
                        $"for the following documents: {string.Join(", ", table.Inserts.Select(x => x.DocumentId))}");
                }
            }
        }

        protected override void HandleFallback()
        {
            if (Statistics.LastErrorTime == null)
                FallbackTime = TimeSpan.FromSeconds(5);
            else
            {
                // double the fallback time (but don't cross 15 minutes)
                var secondsSinceLastError = (Database.Time.GetUtcNow() - Statistics.LastErrorTime.Value).TotalSeconds;

                FallbackTime = TimeSpan.FromSeconds(Math.Min(60 * 15, Math.Max(5, secondsSinceLastError * 2)));
            }
        }

        protected override bool ShouldFilterOutHiLoDocument()
        {
            return true;
        }

        public SqlEtlSimulationResult Simulate(SimulateSqlEtl simulateSqlEtl, DocumentsOperationContext context, IEnumerable<SqlTableWithRecords> toWrite)
        {
            var summaries = new List<TableQuerySummary>();

            if (simulateSqlEtl.PerformRolledBackTransaction)
            {
                using (var writer = new RelationalDatabaseWriter(this, Database))
                {
                    foreach (var records in toWrite)
                    {
                        var commands = new List<DbCommand>();

                        writer.Write(records, CancellationToken, commands);

                        summaries.Add(TableQuerySummary.GenerateSummaryFromCommands(records.TableName, commands));
                    }

                    writer.Rollback();
                }
            }
            else
            {
                var simulatedwriter = new RelationalDatabaseWriterSimulator(Configuration);

                foreach (var records in toWrite)
                {
                    var commands = simulatedwriter.SimulateExecuteCommandText(records, CancellationToken).Select(x => new TableQuerySummary.CommandData
                    {
                        CommandText = x
                    }).ToArray();

                    summaries.Add(new TableQuerySummary
                    {
                        TableName = records.TableName,
                        Commands = commands
                    });
                }
            }

            return new SqlEtlSimulationResult
            {
                LastAlert = Statistics.LastAlert,
                Summary = summaries
            };
        }

        public static SqlEtlSimulationResult SimulateSqlEtl(SimulateSqlEtl simulateSqlEtl, DocumentDatabase database, ServerStore serverStore, DocumentsOperationContext context)
        {
            var document = database.DocumentsStorage.Get(context, simulateSqlEtl.DocumentId);
            
            if (document == null)
                throw new InvalidOperationException($"Document {simulateSqlEtl.DocumentId} does not exist");

            if (serverStore.LoadDatabaseRecord(database.Name, out _).SqlConnectionStrings.TryGetValue(simulateSqlEtl.Configuration.ConnectionStringName, out var connectionString) == false)
                throw new InvalidOperationException($"Connection string named {simulateSqlEtl.Configuration.ConnectionStringName} was not found in the database record");

            simulateSqlEtl.Configuration.Initialize(connectionString);

            if (simulateSqlEtl.Configuration.Validate(out List<string> errors) == false)
            {
                throw new InvalidOperationException($"Invalid ETL configuration for '{simulateSqlEtl.Configuration.Name}'. " +
                                                    $"Reason{(errors.Count > 1 ? "s" : string.Empty)}: {string.Join(";", errors)}.");
            }

            // TODO arek - those constraints can be changed later on

            if (simulateSqlEtl.Configuration.Transforms.Count != 1)
            {
                throw new InvalidOperationException($"Invalid number of transformations. You have provided {simulateSqlEtl.Configuration.Transforms.Count} " +
                                                    "while SQL ETL simulation expects to get exactly 1 transformation script");
            }

            if (simulateSqlEtl.Configuration.Transforms[0].Collections.Count != 1)
            {
                throw new InvalidOperationException($"Invalid number of collections specified in the transformation script. You have provided {simulateSqlEtl.Configuration.Transforms[0].Collections.Count} " +
                                                    "while SQL ETL simulation is supposed to work with exactly 1 collection");
            }

            using (var etl = new SqlEtl(simulateSqlEtl.Configuration.Transforms[0], simulateSqlEtl.Configuration, database, null))
            {
                etl.EnsureThreadAllocationStats();

                var collection = simulateSqlEtl.Configuration.Transforms[0].Collections[0];
                var transformed = etl.Transform(new[] {new ToSqlItem(document, collection)}, context, new EtlStatsScope(new EtlRunStats()), new EtlProcessState());

                return etl.Simulate(simulateSqlEtl, context, transformed);
            }
        }
    }
}
