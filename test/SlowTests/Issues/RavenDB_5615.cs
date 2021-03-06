﻿using FastTests;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using SlowTests.Core.Utils.Indexes;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_5615 : RavenTestBase
    {
        [Fact]
        public void CanEnableAndDisableIndex()
        {
            using (var store = GetDocumentStore())
            {
                var index = new Users_ByCity();
                index.Execute(store);

                var indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Normal, indexStats.State);
                Assert.Equal(IndexRunningStatus.Running, indexStats.Status);

                store.Admin.Send(new EnableIndexOperation(index.IndexName)); // no-op

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Normal, indexStats.State);
                Assert.Equal(IndexRunningStatus.Running, indexStats.Status);

                store.Admin.Send(new DisableIndexOperation(index.IndexName));

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Disabled, indexStats.State);
                Assert.Equal(IndexRunningStatus.Disabled, indexStats.Status);

                store.Admin.Send(new DisableIndexOperation(index.IndexName)); // no-op

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Disabled, indexStats.State);
                Assert.Equal(IndexRunningStatus.Disabled, indexStats.Status);

                store.Admin.Send(new StartIndexOperation(index.IndexName)); // cannot start disabled index

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Disabled, indexStats.State);
                Assert.Equal(IndexRunningStatus.Disabled, indexStats.Status);

                store.Admin.Send(new EnableIndexOperation(index.IndexName));

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexState.Normal, indexStats.State);
                Assert.Equal(IndexRunningStatus.Running, indexStats.Status);
            }
        }

        [Fact]
        public void CanChangeIndexPriority()
        {
            using (var store = GetDocumentStore())
            {
                var index = new Users_ByCity();
                index.Execute(store);

                var indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexPriority.Normal, indexStats.Priority);

                store.Admin.Send(new SetIndexesPriorityOperation(index.IndexName, IndexPriority.Normal)); // no-op

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexPriority.Normal, indexStats.Priority);

                store.Admin.Send(new SetIndexesPriorityOperation(index.IndexName, IndexPriority.Low));

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexPriority.Low, indexStats.Priority);

                store.Admin.Send(new SetIndexesPriorityOperation(index.IndexName, IndexPriority.High));

                indexStats = store.Admin.Send(new GetIndexStatisticsOperation(index.IndexName));
                Assert.Equal(IndexPriority.High, indexStats.Priority);
            }
        }
    }
}