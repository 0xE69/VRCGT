# Changelog

## [1.1.9] - 2026-05-11

### Fixed

- **Discord webhooks not sending after saving settings** (`DiscordWebhookService.cs`)

  Three bugs were causing Discord webhook notifications to be silently dropped even when valid webhook URLs were configured in Discord Settings.

  1. **`IsConfiguredForGroup` only checked the legacy field.**  
     `GroupConfiguration.DiscordWebhookUrl` is a legacy migration field. The Discord Settings UI saves webhook URLs to the per-category fields (`DiscordWebhookMemberEvents`, `DiscordWebhookRoleEvents`, etc.). Because `IsConfiguredForGroup` only checked the legacy field, it always returned `false` for groups configured through the current UI, causing `SendModerationActionAsync` to exit silently.  
     *Fix:* Now checks all per-category webhook fields, the legacy field, and the global webhook as a last resort.

  2. **Global webhook fallback was gated on `groupId` being empty.**  
     In `SendMessageAsync`, the fallback to `Settings.DiscordWebhookUrl` was guarded by `&& string.IsNullOrEmpty(groupId)`. Since every group event carries a `groupId`, the global webhook was never reached as a fallback, silently dropping messages when no per-group webhook was set.  
     *Fix:* The global webhook is now tried whenever no group-level webhook resolves, regardless of whether a `groupId` was supplied.

  3. **`GetWebhookUrlForEventType` returned `null` instead of the global webhook.**  
     When neither the per-category nor the legacy per-group webhook was set, the method returned `groupConfig.DiscordWebhookUrl` (null), rather than falling through to the global settings webhook. `SendAuditEventAsync` then logged "Webhook not configured" and returned without sending.  
     *Fix:* The method now chains: category webhook → legacy per-group webhook → global settings webhook.
