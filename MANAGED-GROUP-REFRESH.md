# Managed group metadata refresh

Root cause: saved group names/icons were only populated when adding a group; discovery skipped existing groups. GroupConfiguration also lacked binding change notifications.

Fix: GroupConfiguration now notifies name, DisplayName and icon changes. MainViewModel.UpdateManagedGroupMetadata updates the existing object by group ID and saves settings, retaining selection and per-group settings. Discovery updates existing entries. Fresh Group Info API results also synchronize the matching saved entry (startup/selection/manual refresh); cached data does not overwrite metadata. This is refresh-driven, not a live rename subscription.

Verification: tests/GroupMetadataRegression console regression failed before the fix (no UI notifications), then passed afterward for rename/display/icon notifications, unchanged-value suppression and model settings preservation. Release build passed with 0 errors and the 14 prior nullability warnings. No live VRChat rename/UI test performed. No new release published.

Run regression: dotnet run --project tests/GroupMetadataRegression
