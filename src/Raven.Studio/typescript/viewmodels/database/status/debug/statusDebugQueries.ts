import getStatusDebugQueriesCommand = require("commands/database/debug/getStatusDebugQueriesCommand");
import getKillQueryCommand = require("commands/database/query/getKillQueryCommand");
import viewModelBase = require("viewmodels/viewModelBase");
import aceEditorBindingHandler = require("common/bindingHelpers/aceEditorBindingHandler");
import statusDebugQueriesGroup = require("models/database/debug/statusDebugQueriesGroup");
import statusDebugQueriesQuery = require("models/database/debug/statusDebugQueriesQuery");
import autoRefreshBindingHandler = require("common/bindingHelpers/autoRefreshBindingHandler");
import eventsCollector = require("common/eventsCollector");

class statusDebugQueries extends viewModelBase {
    data = ko.observableArray<statusDebugQueriesGroup>();

    constructor() {
        super();
        autoRefreshBindingHandler.install();
        aceEditorBindingHandler.install();
    }

    activate(args: any) {
        super.activate(args);
        this.updateHelpLink('JHZ574');
        this.activeDatabase.subscribe(() => this.fetchCurrentQueries());
        return this.fetchCurrentQueries();
    }

    fetchCurrentQueries(): JQueryPromise<statusDebugQueriesGroupDto[]> {
        var db = this.activeDatabase();
        if (db) {
            return new getStatusDebugQueriesCommand(db)
                .execute()
                .done((results: statusDebugQueriesGroupDto[]) => this.onResultsLoaded(results));
        }

        return null;
    }

    onResultsLoaded(results: statusDebugQueriesGroupDto[]) {
        var currentGroups = $.map(this.data(), (group) => group.indexName);

        $.map(results, (dtoGroup) => {
            if (dtoGroup.Queries.length > 0) {
                var foundGroup = this.data().find((item) => item.indexName === dtoGroup.IndexName);
                if (foundGroup) {
                    _.pull(currentGroups, dtoGroup.IndexName);
                } else {
                    foundGroup = new statusDebugQueriesGroup(dtoGroup);
                    this.data.push(foundGroup);
                }
                this.updateGroup(foundGroup, dtoGroup);
            }
        });

        // remove empty and unused groups
        currentGroups.forEach(group => {
            var foundGroup = this.data().find((item) => item.indexName === group);
            if (foundGroup) {
                this.data.remove(foundGroup);
            }
        });
    }

    updateGroup(group: statusDebugQueriesGroup, dtoGroup: statusDebugQueriesGroupDto) {
        var currentQueryIds = $.map(group.queries(), (query) => query.queryId);

        $.map(dtoGroup.Queries, (dtoQuery) => {
            var foundQuery = group.queries().find((item) => item.queryId === dtoQuery.QueryId);
            if (foundQuery) {
                _.pull(currentQueryIds, foundQuery.queryId);
                foundQuery.duration(dtoQuery.Duration);
            } else {
                group.queries.push(new statusDebugQueriesQuery(dtoQuery));
            }
        });

        // remove unused queries
        currentQueryIds.forEach(query => {
            var foundQuery = group.queries().find((item) => item.queryId === query);
            if (foundQuery) {
                group.queries.remove(foundQuery);
            }
        });
    } 

    killQuery(queryId: number) {
        eventsCollector.default.reportEvent("query", "kill");
        new getKillQueryCommand(this.activeDatabase(), queryId)
            .execute()
            .done(() => {
                // find and delete query from model
                this.data().forEach(group => {
                    var foundQuery = group.queries().find(q => q.queryId === queryId);
                    if (foundQuery) {
                        group.queries.remove(foundQuery);
                    }
                });
            });
    }
}

export = statusDebugQueries;
