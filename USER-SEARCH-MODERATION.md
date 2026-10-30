# User Search moderation

User Search supports name search and direct usr_ ID lookup. Select the result to open its profile.

Non-members now have a **Ban from Group** button alongside Invite to Group. Previously the only ban button was nested inside the members-only panel. Both buttons use the existing BanUserCommand and moderation dialog (reason, description and duration), then the existing moderation helper/API ban flow. A Group ID must be configured; VRChat enforces the operator's permissions. Kick and role management remain members-only.

Verification: Release build succeeded with 0 errors and 14 existing nullability warnings. No live ban was issued during verification. The change is local and has not been published as an installer/release.
