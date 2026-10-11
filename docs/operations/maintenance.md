# Maintenance and Recovery

This is operational safety guidance, not a tested production runbook. The supplied SQL maintenance scripts are labeled for development/test use. None were executed during drafting.

## Routine Checks

- Review sanitized startup, broker, synchronization, and notification-dispatch logs.
- Monitor SQL storage and broker capacity through approved administrative tooling.
- Keep runtime credentials available: the Host administers broker entities after startup.
- Close sessions and remove deliveries through REST when workflows finish.
- Maintain SQL backups, protected configuration, and a broker recovery policy. Test restoration in isolation.

[Discovery](../api/configuration-discovery.md) is not a SQL or Service Bus health check.

## Installation and Upgrades

Use the [fresh installer](../getting-started/installation.md#initialize-sql-server) for an empty target. It can return early when an existing schema marker is present and is not a verified upgrade tool. Do not reset a database to make an upgrade work or run development migrations without a separately reviewed plan.

## Development Cleanup Scripts

Review the target database and full script before any use. Confirmation variables do not establish that a database is disposable.

| Script | Effect | Required input |
| --- | --- | --- |
| [PurgeResolvedNotifications.sql](../../RapidRedPanda.ISBM.Host/Database/Scripts/Maintenance/PurgeResolvedNotifications.sql) | Removes Delivered, Failed, Suppressed, and Expired notices with ResolvedUtc before the cutoff and their topic rows. Does not select Pending, RetryScheduled, or Sending notices. | ConfirmPurge = YES and ResolvedBeforeUtcText in ISO format. |
| [PurgeClosedSessions.sql](../../RapidRedPanda.ISBM.Host/Database/Scripts/Maintenance/PurgeClosedSessions.sql) | Removes old closed sessions and dependent message, response, recipient, and notification rows. | ConfirmPurge = YES and ClosedBeforeUtcText in ISO format. |
| [ResetRuntimeData.sql](../../RapidRedPanda.ISBM.Host/Database/Scripts/Maintenance/ResetRuntimeData.sql) | Deletes runtime messaging/session data, preserving channels, channel tokens, and SchemaVersions. | ConfirmReset = YES. |
| [ResetAllData.sql](../../RapidRedPanda.ISBM.Host/Database/Scripts/Maintenance/ResetAllData.sql) | Deletes application data including channels/tokens, preserving schema and SchemaVersions. | ConfirmReset = YES. |

Scripts reside under `RapidRedPanda.ISBM.Host/Database/Scripts/Maintenance/`. Purge cutoffs have no automatic retention default. Transactions roll back failures, but successful deletion is not reversible without a suitable backup.

The scripts modify SQL only, without coordinating broker cleanup or restore. Do not run them against an active Host or use them as a production retention scheduler. In a separately approved disposable environment, stop participating applications and the Host, review both backends, and plan broker cleanup independently before restart.

## Recovery Boundaries

Restoring SQL or recreating topics does not restore an entire exchange. Broker delivery, SQL lifecycle state, and application processing may differ. Reconcile them before resuming; blind replay can duplicate business actions.

Channel-topic synchronization cannot recover deleted messages or every session subscription. Publication subscription reconciliation occurs when a consumer subscription opens, not as a comprehensive startup restore. No first-release REST replay/recovery interface for failed notices or dead-letter management is documented.

Preserve diagnostics before cleanup. Do not purge notice history until its diagnostic/retention value is understood. See [Troubleshooting](troubleshooting.md), [Notifications](../api/notifications.md), and the [documentation index](../README.md).
