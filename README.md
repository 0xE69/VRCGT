# VRCGT - VRChat Group Tools

A powerful desktop application for VRChat group management with comprehensive moderation tools.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

### 🔐 Secure Login
- Login with your VRChat credentials
- Full 2FA/OTP (Time-based One-Time Password) support
- Session caching for seamless re-login
- Secure credential handling (sent only to VRChat API)

### 🔍 18+ Badge Scanner
- Scan all members in your VRChat group
- Identify who has the 18+ age verification badge
- Real-time progress tracking
- Filter results by: Verified, Unverified, Unknown
- Export results to CSV for record-keeping

### 👥 User Search & Moderation
- Search for any VRChat user by name
- View detailed user profiles including:
  - Display name and bio
  - Trust rank and status
  - Profile picture
  - Age verification status
- **Moderation Actions** (for group members):
  - Kick from group
  - Ban from group
  - Unban users

### 📋 Audit Logs
- View comprehensive group audit logs
- Track all group activities:
  - Member joins/leaves
  - Kicks and bans
  - Role changes
  - Group setting updates
  - Announcements
  - Instance activity
- **Advanced Filtering**:
  - Search by username or action
  - Filter by event type
  - Date range selection
- **Auto-refresh** polling (60-second intervals)
- **Fetch History** - Download complete audit log history
- Local SQLite caching for fast loading

### 🔔 Discord Notifications
- Send group events to a Discord channel via webhook
- **Configurable event notifications**:
  - 👋 User Joins
  - 🚪 User Leaves
  - 👢 User Kicked
  - 🔨 User Banned
  - ✅ User Unbanned
  - 🌐 Instance Opened
  - 🔒 Instance Closed
  - 📥 Join Requests
  - 🏷️ Role Updates
- Test webhook connection
- Select All / Deselect All quick toggles

### 🎨 Modern UI
- Dark Material Design theme
- Responsive layout
- Background task support (scans continue when switching modules)

### 🔄 Auto Updates
- Automatic update checks from GitHub releases
- One-click update installation

## Screenshots

*Coming soon*

## Requirements

- Windows 10/11 (64-bit)
- VRChat account
- Group ownership or moderator permissions for moderation features

## Installation

### Option 1: Download Release
1. Download the latest release from the [Releases](../../releases) page
2. Run `VRCGT_Setup_x.x.x.exe`
3. Follow the installation wizard

### Option 2: Build from Source
1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone this repository
3. Run `build.bat` to build and publish
4. The executable will be in `src\bin\Publish\`

## Usage

### Getting Started
1. **Login**: Enter your VRChat username and password
2. **2FA**: If prompted, enter your authenticator code
3. **Set Group ID**: Enter your VRChat Group ID in the sidebar
4. **Navigate**: Use the sidebar to access different modules

### Badge Scanner
1. Click "🔍 18+ Badge Scanner"
2. Click "Start Scan" to begin scanning members
3. Watch real-time progress
4. Use filters to view specific verification statuses
5. Export results to CSV if needed

### User Search
1. Click "👥 User Search"
2. Enter a username and click "Search"
3. Click on a user to view their profile
4. Use moderation buttons (Kick/Ban/Unban) as needed

### Audit Logs
1. Click "📋 Audit Logs"
2. Logs load automatically from cache
3. Click "Fetch History" to download complete history
4. Use filters to find specific events
5. Toggle "Auto Refresh" for live updates

### Discord Notifications
1. Click "🔔 Discord"
2. Create a webhook in Discord: Server Settings → Integrations → Webhooks → New Webhook
3. Paste the webhook URL and click "Test"
4. Enable desired event notifications
5. Click "Save Settings"

## Finding Your Group ID

1. Go to [VRChat Groups](https://vrchat.com/home/groups)
2. Click on your group
3. The Group ID is in the URL: `vrchat.com/home/group/grp_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`

## Data Storage

All data is stored locally in `%LocalAppData%\VRCGroupTools\`:
- `vrcgt.db` - SQLite database (audit logs cache)
- `settings.json` - Application settings
- `Logs/` - Application logs
- `CrashReports/` - Error reports for debugging

## Project Structure

```
VRCGT/
├── src/                    # Source code
│   ├── App.xaml           # Application entry
│   ├── Converters/        # XAML value converters
│   ├── Data/              # Database entities & context
│   ├── Services/          # API, caching, and Discord services
│   ├── ViewModels/        # MVVM ViewModels
│   └── Views/             # WPF Views (XAML + code-behind)
├── installer/             # Inno Setup installer script
├── build.bat              # Build script
├── VRCGroupTools.sln      # Visual Studio solution
├── LICENSE                # MIT License
└── README.md              # This file
```

## Technologies

- **.NET 8** - Framework
- **WPF** - UI Framework
- **Material Design in XAML** - UI Theme
- **CommunityToolkit.Mvvm** - MVVM Pattern
- **Entity Framework Core** - SQLite Database
- **Octokit** - GitHub API for auto-updates
- **Newtonsoft.Json** - JSON Handling

## Privacy & Security

- ✅ Credentials are only sent directly to VRChat's API
- ✅ Auth cookies are stored locally only
- ✅ Discord webhooks stored in local settings only
- ✅ No data is sent to third parties
- ✅ All sensitive files excluded from git

## Troubleshooting

### Login Issues
- Ensure your credentials are correct
- Check if 2FA is enabled on your account
- VRChat may rate-limit login attempts - wait a few minutes

### Audit Logs Not Loading
- Make sure you've set a valid Group ID
- Click "Refresh Now" to force a refresh
- Check the status message for errors

### Discord Notifications Not Working
- Verify webhook URL starts with `https://discord.com/api/webhooks/`
- Use the "Test" button to verify connection
- Check that the desired events are enabled

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## Changelog

### v1.0.0
- Initial release with 18+ Badge Scanner
- User Search & Moderation (Kick/Ban/Unban)
- Comprehensive Audit Logs with caching
- Discord Webhook Notifications
- Modern Material Design UI

---

## Disclaimer

VRCGT is not endorsed by VRChat and does not reflect the views or opinions of VRChat or anyone officially involved in producing or managing VRChat properties. VRChat and all associated properties are trademarks or registered trademarks of VRChat Inc. VRChat © VRChat Inc.
