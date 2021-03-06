/// <reference path="../../../../typings/tsd.d.ts" />

import abstractNotification = require("common/notifications/models/abstractNotification");
import generalUtils = require("common/generalUtils");

class recentError extends abstractNotification {

    static currentErrorId = 1;
    
    static licenceLimitMarker = "$$Licence-Limit$$";

    details = ko.observable<string>();
    httpStatus = ko.observable<string>();

    shortMessage: KnockoutComputed<string>;

    constructor(dto: recentErrorDto) {
        super(null, dto);

        this.initObservables();
        this.updateWith(dto);
        this.createdAt(moment.utc());
        this.licenseLimitType(dto.LicenseLimitType);
    }

    updateWith(incomingChanges: recentErrorDto) {
        super.updateWith(incomingChanges);

        this.details(incomingChanges.Details);
        this.httpStatus(incomingChanges.HttpStatus);
        this.severity(incomingChanges.Severity);
    }

    private initObservables() {
        this.hasDetails = ko.pureComputed(() => !!this.details());
        this.shortMessage = ko.pureComputed(() => generalUtils.trimMessage(this.message()));
    }

    static tryExtractMessageAndException(details: string): { message: string, error: string, licenseLimitType: Raven.Server.Commercial.LimitType } {
        try {
            const parsedDetails = JSON.parse(details);

            if (parsedDetails && parsedDetails.Message) {
                return {
                    message: parsedDetails.Message,
                    error: parsedDetails.Error,
                    licenseLimitType: parsedDetails.Type
                };
            }
        } catch (e) {
        }

        // fallback to message with entire details
        return { message: details, error: null, licenseLimitType: null };
    }

    static create(severity: Raven.Server.NotificationCenter.Notifications.NotificationSeverity, title: string, details: string, httpStatus: string) {
        const messageAndException = recentError.tryExtractMessageAndException(details);
        const dto = {
            CreatedAt: null,
            IsPersistent: false,
            Title: title,
            Message: messageAndException.message,
            Id: "RecentError/" + (recentError.currentErrorId++),
            Type: "RecentError",
            Details: messageAndException.error,
            HttpStatus: httpStatus,
            Severity: severity,
            LicenseLimitType: messageAndException.licenseLimitType
        } as recentErrorDto;

        return new recentError(dto);
    }
}

export = recentError;
