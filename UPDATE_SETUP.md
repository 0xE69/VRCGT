# Auto-Update Setup Guide

## Overview
The application now has a fully functional auto-update system that checks GitHub releases and automatically downloads and installs updates.

## What You Need to Do

### 1. Update the GitHub Repository Name
In [src/App.xaml.cs](src/App.xaml.cs), update line 23:
```csharp
public static string GitHubRepo => "bradh/VRCGroupTools"; // Update to your actual repo: owner/repo-name
```
Replace `"bradh/VRCGroupTools"` with your actual GitHub repository in the format `"owner/repository-name"`.

### 2. Create Your First Release

When you're ready to release a new version:

1. **Update the version number** in 3 places:
   - [src/App.xaml.cs](src/App.xaml.cs) line 22: `public static string Version => "1.0.2";`
   - [src/VRCGroupTools.csproj](src/VRCGroupTools.csproj) line 13: `<Version>1.0.2</Version>`
   - [src/VRCGroupTools.csproj](src/VRCGroupTools.csproj) line 14-15: `<AssemblyVersion>1.0.2.0</AssemblyVersion>` and `<FileVersion>1.0.2.0</FileVersion>`

2. **Commit and push** your changes:
   ```bash
   git add .
   git commit -m "Release v1.0.2"
   git push
   ```

3. **Create and push a tag**:
   ```bash
   git tag v1.0.2
   git push origin v1.0.2
   ```

4. The GitHub Actions workflow will automatically:
   - Build the application
   - Create a ZIP file named `VRCGroupTools-v1.0.2-win-x64.zip`
   - Create a GitHub Release with the ZIP attached

### 3. How the Auto-Update Works

**For Users:**
1. When the app starts, it checks GitHub for new releases
2. If a newer version is found, a dialog appears asking to update
3. If they click "Yes":
   - Downloads the new version ZIP
   - Extracts the new executable
   - Creates a batch script to replace the old .exe after the app closes
   - Closes the current app
   - The batch script replaces the executable and restarts the app

**Technical Details:**
- Uses [Octokit](https://github.com/octokit/octokit.net) to query GitHub API
- Compares version numbers (e.g., 1.0.2 > 1.0.1)
- Only looks for stable releases (ignores prereleases unless tagged with "beta" or "alpha")
- Downloads ZIP files (not installers)
- Uses a temporary batch script for the file replacement to avoid locking issues

## GitHub Workflow Features

The workflow ([.github/workflows/build.yml](.github/workflows/build.yml)) includes:

- **On every push**: Builds the app and uploads artifacts (for testing)
- **On tagged releases** (tags starting with `v`):
  - Builds a release version
  - Creates a ZIP with the executable
  - Creates a GitHub Release with automatic release notes
  - Attaches the ZIP file to the release

## Release Checklist

Before creating a new release:

- [ ] Update version in `App.xaml.cs`
- [ ] Update version in `VRCGroupTools.csproj` (3 places)
- [ ] Test the build locally: `dotnet build -c Release`
- [ ] Commit changes with descriptive message
- [ ] Create and push version tag (e.g., `v1.0.2`)
- [ ] Verify GitHub Actions workflow completes successfully
- [ ] Check that the release appears on GitHub with the ZIP file attached
- [ ] Test the auto-update by running the old version

## Troubleshooting

**Update check fails:**
- Verify the `GitHubRepo` value in `App.xaml.cs` matches your repo
- Check that the repository is public (or add GitHub token for private repos)
- Ensure you have internet connectivity

**No releases found:**
- Make sure you've pushed at least one tag starting with `v` (e.g., `v1.0.1`)
- Check that the GitHub Actions workflow completed successfully
- Verify the release has a ZIP file attached

**Update download fails:**
- Ensure the GitHub Release has a `.zip` file attached
- Check internet connectivity
- Verify the ZIP contains `VRCGroupTools.exe`

**App doesn't restart after update:**
- Check Windows Defender or antivirus isn't blocking the batch script
- Look in `%TEMP%` for `VRCGroupTools_Update.bat` and check for errors
- Ensure the user has write permissions to the app directory

## Advanced: Private Repositories

If your repository is private, you'll need to add a GitHub Personal Access Token:

1. Create a token at: https://github.com/settings/tokens
2. Update `UpdateService.cs` constructor:
```csharp
public UpdateService()
{
    _gitHubClient = new GitHubClient(new ProductHeaderValue("VRCGroupTools"))
    {
        Credentials = new Credentials("YOUR_GITHUB_TOKEN")
    };
}
```

**Note:** For security, store the token in a config file or environment variable, not hardcoded.
