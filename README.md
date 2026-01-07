# VRCGT - VRChat Group Tools

A powerful desktop toolkit for VRChat group owners and moderators. Fast login, rich group insights, member management, posts, calendar events, invites, audit logs, Discord webhooks, and more—all in one modern WPF app.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![Windows](https://img.shields.io/badge/Platform-Windows-0078D6) ![License](https://img.shields.io/badge/License-MIT-green) ![Build](https://img.shields.io/github/actions/workflow/status/yourusername/VRCGroupTools/build.yml?branch=main)

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

### 📅 Calendar Events
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

### 🔍 18+ Badge Scanner
- Scan entire group for age verification status
- Filter by verified/unverified
- Export results to CSV
- Progress tracking

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

### 🔔 Discord Webhooks
- Configure webhook URL
- Select which events to notify:
  - Member joins/leaves
  - Bans/Unbans
  - Role changes
  - Posts created
  - Events scheduled
- Test webhook functionality
- Select/deselect all events

### ⚙️ Settings
- Auto-update checks from GitHub releases
- Theme customization
- Local data management
- About and version info

---

## 🚀 Quick Start

1. **Download:** Get the latest release from [Releases](../../releases) or build from source
2. **Run:** Launch `VRCGroupTools.exe`
3. **Login:** Enter your VRChat credentials + 2FA code
4. **Set Group:** Enter your Group ID (`grp_...` or short code) in the sidebar
5. **Explore:** Navigate modules from the left sidebar

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

### Quick Build
```cmd
:: Clone the repository
git clone https://github.com/yourusername/VRCGroupTools.git
cd VRCGroupTools

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
git tag v1.0.0
git push origin v1.0.0
```

---

## 📁 Data & Privacy

All data is stored locally in `%LocalAppData%\VRCGroupTools\`:
- `settings.json` - App configuration
- `cache.db` - SQLite database for caching
- `Logs/` - Application logs
- `CrashReports/` - Error reports

**Privacy:** 
- Credentials are sent directly to VRChat's official API
- No data is sent to third parties
- Discord webhooks are configured and stored locally

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
