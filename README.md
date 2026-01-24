# VRCGT - VRChat Group Tools

A powerful desktop toolkit for VRChat group owners and moderators. Fast login, rich group insights, member management, posts, calendar events, invites, audit logs, Discord webhooks, and more—all in one modern WPF app.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![Windows](https://img.shields.io/badge/Platform-Windows-0078D6) ![License](https://img.shields.io/badge/License-MIT-green) ![Build](https://img.shields.io/github/actions/workflow/status/0xE69/VRCGT/build.yml?branch=main)

---

## ✨ Features

### 🔐 Authentication
- Secure VRChat login with 2FA (TOTP/Email) support
- Session caching for instant re-login
- Automatic session restoration on app startup

### 📊 Group Dashboard
- Group name, privacy level, member counts, and online stats
- Creation date, owner info, and group links
- Rules, description, and gallery preview
- Role management and permissions overview
- Upcoming and past events display

### 👥 User Search & Moderation
- Search any VRChat user by username or User ID
- View detailed user profiles with avatar, bio, and badges
- Age verification status display (18+ badge)
- Highlight existing group members
- **Moderation Actions:**
  - Kick users from group
  - Ban/Unban users
  - Assign and remove roles
  - Send group invites

### 📨 Invite to Group
- Search users by name or paste User ID directly
- Click-to-preview user profile before inviting
- View age verification status and bio
- Quick "Use" button to select user
- One-click invite sending

### � Inviter Hub (New!)
- **Instance Inviter**: Detect users in your current VRChat instance and invite them to your group.
    - Filter by Trust Level (Visitor, New User, User, Known, Trusted).
    - **18+ Only Filter**: Only show users with confirmed 18+ age verification.
    - "Select All" and bulk invite capabilities.
- **Friend Inviter**: Quickly invite your online friends to your group.
- **Join Requests**: Monitor and process group join requests.
    - Filter requests by 18+ status.
    - Approve or block users directly.

### �📅 Calendar Events
- Create and manage group events
- **Event Options:**
  - Title, description, category
  - Start/end time with date pickers
  - Visibility (Public/Group)
  - Platform tags (Windows, Android, iOS)
  - Language tags with searchable dropdown
  - Send notification toggle
  - Thumbnail upload support
- **Recurrence Support:**
  - Weekly (select days)
  - Monthly (specific dates)
  - Specific dates list
  - Recurrence end date
- **Templates:** Save and reuse event configurations
- Sync events from VRChat
- Duplicate and delete events

### 📝 Group Posts
- View existing group posts with pagination
- Create new posts with title and content
- Visibility settings (Public/Group/Friends)
- Send notification option
- Edit existing posts
- Delete posts

### 🌐 Instance Creator
- Build VRChat launch links with full customization:
  - Region selection (US, EU, JP)
  - Access type (Public, Friends+, Friends, Invite+, Invite, Group)
  - Queue enabled toggle
  - Age gate (18+) toggle
- Schedule instances with time zone support
- Copy link, send invite, or launch directly

### 👤 Members List
- Browse all group members
- Filter by role
- View member details
- Quick moderation actions

### 🚫 Bans List
- View all banned users
- Unban users with one click
- Ban details and dates

### 📜 Audit Logs
- Full audit log history
- Filter by action type
- Date range selection
- Search functionality
- Auto-refresh toggle
- Fetch complete history
- Cached locally for speed
- **Real-time Security Monitoring:** Automatically tracks suspicious actions

### 🔍 18+ Badge Scanner
- Scan entire group for age verification status
- Filter by verified/unverified/unknown
- **Moderation Actions:**
  - Manual selection with checkboxes
  - Select All Unverified / Unselect All buttons
  - Kick Selected members
  - Kick All Unverified (bulk action)
  - Rate-limited to 500ms per action
- **Copy to Clipboard:**
  - Copy unverified user list
  - Copy filtered results
  - Pipe-delimited format (DisplayName|UserID)
- Export results to CSV
- Progress tracking with real-time stats

### ⚡ Kill Switch (Role Removal)
- Emergency bulk role removal from group members
- Automatically creates a snapshot before removing roles
- Select specific members or roles to remove
- **Snapshot & Restore:**
  - All role assignments saved to local database before removal
  - View historical snapshots with timestamps
  - Restore roles individually or in bulk
  - Track which roles have been restored
- Rate-limited API calls to avoid VRChat restrictions
- Progress tracking with success/failure counts
- Excludes default "Member" role from removal

### � **NEW: Member Backup & Recovery System**
- **Create Snapshots:** Save complete member lists with roles and join dates
- **Disaster Recovery:** Restore members after mass removals or attacks
- **Smart Comparison:** See exactly who's missing vs who's new
- **Selective Restore:** Re-invite only missing members or everyone
- **Backup History:** Keep multiple snapshots with custom descriptions
- **Progress Tracking:** Real-time progress for backup/restore operations
- **Rate Limited:** Automatic delays to respect VRChat API limits
- **Status Tracking:** See which members were successfully re-invited

### 🔒 **NEW: Security Monitor**
- **Automated Threat Detection:** Monitor and respond to suspicious moderator behavior
- **Configurable Thresholds:** Set limits for kicks, bans, role removals, and more
- **Automatic Response:** Remove roles from users exceeding thresholds
- **Discord Alerts:** Receive rich notifications when incidents are detected
- **Owner Protection:** Require owner role for automatic actions
- **Incident History:** View all security events with full details
- **Action Tracking:** Every monitored action logged to database
- **Multiple Categories:**
  - Excessive kicks (e.g., 5 kicks in 10 minutes)
  - Excessive bans (e.g., 3 bans in 10 minutes)
  - Mass role removals (e.g., 5 removals in 10 minutes)
  - Bulk invite rejections (e.g., 10 rejections in 10 minutes)
  - Content deletions (e.g., 5 posts deleted in 10 minutes)
- **Separate Webhook:** Configure dedicated security alert webhook

### �🔔 Discord Webhooks
- Configure webhook URL (supports discord.com and discordapp.com)
- **Comprehensive Event Notifications:**
  - **Member Events:** Joins, leaves, updates, role assignments
  - **Role Events:** Create, update, delete role changes
  - **Instance Events:** Create, update, delete instances
  - **Group Events:** Name, description, icon, banner, privacy changes
  - **Invite & Join Events:** Requests, invites, approvals, rejections
  - **Announcement Events:** Create, update, delete announcements
  - **Gallery Events:** Image submissions, approv
  - **Security Events:** Threshold violations and automatic responses (via separate webhook)als, deletions
  - **Post Events:** Create, update, delete posts
- **Bulk Controls:**
  - Select All / Deselect All toggles
  - Organized by event categories
- Test webhook connection
- Rich embed notifications with color coding and emojis

### ⚙️ Settings
- **Auto-Update System:**
  - Automatic update checks on startup
  - Downloads from GitHub releases
  - One-click update and restart
  - ZIP-based updates with automatic executable replacement
- **Startup Options:**
  - Start with Windows (registry integration)
- Theme customization
- Region and timezone settings
- Local data management
- About and version info

---

## 🚀 Quick Start

1. **Download:** Get the latest release from [Releases](../../releases) or build from source
2. **Run:** Launch `VRCGroupTools.exe`
   - **Windows SmartScreen Warning?** This appears because the app isn't code-signed (signing certificates cost $$$). The app is safe and open source. Click **"More info"** → **"Run anyway"**
3. **Login:** Enter your VRChat credentials + 2FA code
4. **Set Group:** Enter your Group ID (`grp_...` or short code) in the sidebar
5. **Explore:** Navigate modules from the left sidebar

### 🛡️ Security Note

**Windows SmartScreen Warning:** The application may show "Windows protected your PC" on first run. This is normal for unsigned applications. To proceed:
1. Click **"More info"**
2. Click **"Run anyway"**

The application is:
- ✅ **Open Source** - All code is visible on GitHub
- ✅ **Scanned by GitHub Actions** - Built in a clean environment
- ✅ **No Telemetry** - Your data stays local
- ✅ **Community Verified** - Check the issues and discussions

**Why unsigned?** Code signing certificates cost $300-500/year. Once the project grows, we'll invest in proper signing.

---

## 💻 System Requirements

- **OS:** Windows 10/11 (64-bit)
- **Runtime:** .NET 8 Desktop Runtime (included in self-contained builds)
- **Account:** VRChat account with group moderator/owner rights for moderation features

---

## 🔨 Building from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11
- Git (optional, for cloning)

### Quick Build
```cmd
:: Clone the repository
git clone https://github.com/0xE69/VRCGT.git
cd VRCGT

:: Run the build script
build.bat
```

### Manual Build
```cmd
:: Navigate to source directory
cd src

:: Restore packages
dotnet restore

:: Build release version
dotnet build -c Release

:: Publish self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/Publish
```

### Output
The compiled executable will be in `src/bin/Publish/VRCGroupTools.exe`

### Creating an Installer (Optional)
1. Install [Inno Setup](https://jrsoftware.org/isinfo.php)
2. Open `installer/setup.iss`
3. Compile to create the installer

---

## 🔄 GitHub Actions CI/CD

This project includes automated builds via GitHub Actions:

- **On Push/PR:** Builds and uploads artifacts
- **On Tag (v*):** Creates a GitHub Release with the compiled binary

To create a release:
```cmd
git tag v1.0.7
git push origin v1.0.7
```

---

## 📁 Data & Privacy

All data is stored locally in `%LocalAppData%\VRCGroupTools\`:
- `settings.json` - App configuration
- `vrcgrouptools.db` - SQLite database for caching, security logs, and backups
- `Logs/` - Application logs
- `CrashReports/` - Error reports

**Privacy:** 
- Credentials are sent directly to VRChat's official API
- No data is sent to third parties
- Discord webhooks are configured and stored locally
- All security monitoring and backups stored locally

---

## 📝 License

MIT License - see [LICENSE](LICENSE) for details.

---

## ⚠️ Disclaimer

This project is not affiliated with, endorsed by, or connected to VRChat Inc. VRChat and related marks are trademarks of VRChat Inc. Use at your own risk and in accordance with VRChat's Terms of Service.

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📋 Changelog

### v1.0.7 (2026-01-15)
**🔧 Stability & Caching**

**Improvements:**
- Prevent duplicate Discord audit webhook posts
- Added cache load options for group info, posts, bans, and badge scans

### v1.0.6 (2025-01-14)
**🔒 Security & Recovery Features**

**New Features:**
- **Security Monitor System** 🔒
  - Automatic detection of suspicious moderator behavior
  - Configurable thresholds for kicks, bans, role removals, invite rejections, and content deletions
  - Automatic role removal for users exceeding limits
  - Dedicated Discord webhook for security alerts
  - Owner-only protection mode
  - Complete incident history and action logging
  - Real-time monitoring integrated with audit logs

- **Member Backup & Recovery System** 💾
  - Create complete snapshots of all group members with roles
  - Smart comparison between backups and current membership
  - Selective member restoration (missing only or all)
  - Multiple backup history with custom descriptions
  - Progress tracking for backup/restore operations
  - Visual status indicators (missing/re-invited)
  - Rate-limited API calls for safe restoration

**Improvements:**
- Enhanced database schema with security and backup tables
- Additional VRChat API methods for member role management
- Improved error handling and logging throughout
- Better performance with indexed database queries

### v1.0.5 (2025-12-XX)
- Initial public release
- Core group management features
- Discord webhook integration
- Kill Switch functionality
- 18+ Badge Scanner
- Calendar events and posts management

---

## 🙏 Acknowledgments

- VRChat community for feedback and testing
- Material Design In XAML for UI components
- All contributors and supporters
