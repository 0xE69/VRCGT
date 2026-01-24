using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VRCGroupTools.Services;

public interface IInstanceInviterService
{
    CurrentInstanceInfo? GetCurrentInstance();
    Task<bool> InviteToCurrentInstanceAsync(string userId, IVRChatApiService apiService);
    Task<List<InstanceUser>> GetCurrentInstanceUsersAsync(IVRChatApiService apiService);
}

public class InstanceInviterService : IInstanceInviterService
{
    // More flexible regex to match various instance ID formats
    private static readonly Regex InstanceLocationRegex = new Regex(
        @"wrld_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}:[^\s]+",
        RegexOptions.Compiled);

    private static readonly Regex WorldIdRegex = new Regex(
        @"wrld_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}",
        RegexOptions.Compiled);

    public CurrentInstanceInfo? GetCurrentInstance()
    {
        Console.WriteLine("[INSTANCE-SVC] GetCurrentInstance called");
        try
        {
            // Get VRChat log file path from AppData
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
            var vrchatLogPath = Path.Combine(appDataLocal, "VRChat", "VRChat");
            Console.WriteLine($"[INSTANCE-SVC] Checking log path: {vrchatLogPath}");

            if (!Directory.Exists(vrchatLogPath))
            {
                Console.WriteLine("[INSTANCE-SVC] VRChat log directory not found");
                return null;
            }

            // Get the most recent log file
            var logFiles = Directory.GetFiles(vrchatLogPath, "output_log_*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (logFiles.Count == 0)
            {
                Console.WriteLine("[INSTANCE-INVITER] No VRChat log files found");
                return null;
            }

            var latestLog = logFiles[0];
            Console.WriteLine($"[INSTANCE-INVITER] Reading log: {Path.GetFileName(latestLog)}");

            // Read the entire log file or last 5MB to find current instance
            using var stream = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bufferSize = Math.Min(5000000, stream.Length); // Read last 5MB or entire file if smaller
            var buffer = new byte[bufferSize];
            
            if (stream.Length > bufferSize)
            {
                stream.Seek(-bufferSize, SeekOrigin.End);
            }
            stream.Read(buffer, 0, (int)bufferSize);
            
            Console.WriteLine($"[INSTANCE-INVITER] File size: {stream.Length:N0} bytes, reading last {bufferSize:N0} bytes");
            
            var logContent = System.Text.Encoding.UTF8.GetString(buffer);
            var lines = logContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();

            Console.WriteLine($"[INSTANCE-INVITER] Searching through {lines.Count} log lines...");
            
            int behaviourCount = 0;
            int joiningCount = 0;

            // Look for the most recent instance location
            // World ID is on the same line as "Joining" or "Rejoining local world:"
            foreach (var line in lines)
            {
                // Skip lines that don't have the Behaviour tag and joining keywords
                if (!line.Contains("[Behaviour]")) continue;
                behaviourCount++;
                
                // Debug: Print first few [Behaviour] lines to see what we're getting
                if (behaviourCount <= 5)
                {
                    Console.WriteLine($"[INSTANCE-INVITER] Behaviour line #{behaviourCount}: {line.Substring(0, Math.Min(120, line.Length))}");
                }
                
                if (!line.Contains("Joining") && !line.Contains("Rejoining local world:")) continue;
                joiningCount++;
                
                // Skip "Joining or Creating Room" lines which are different
                if (line.Contains("Joining or Creating Room:")) 
                {
                    Console.WriteLine($"[INSTANCE-INVITER] Skipping 'Joining or Creating Room' line");
                    continue;
                }
                
                Console.WriteLine($"[INSTANCE-INVITER] Found Joining/Rejoining line!");
                Console.WriteLine($"[INSTANCE-INVITER] Line content: {line.Substring(0, Math.Min(150, line.Length))}");
                
                var match = InstanceLocationRegex.Match(line);
                if (match.Success)
                {
                    var fullLocation = match.Value;
                    var worldMatch = WorldIdRegex.Match(fullLocation);
                    
                    if (worldMatch.Success)
                    {
                        var worldId = worldMatch.Value;
                        var instanceId = fullLocation.Substring(worldId.Length + 1); // Skip the ':'
                        
                        Console.WriteLine($"[INSTANCE-INVITER] ✓ Found instance: {worldId}:{instanceId}");
                        Console.WriteLine($"[INSTANCE-INVITER] Full location: {fullLocation}");
                        
                        return new CurrentInstanceInfo
                        {
                            WorldId = worldId,
                            InstanceId = instanceId,
                            FullLocation = fullLocation,
                            DetectedAt = DateTime.Now
                        };
                    }
                }
            }

            Console.WriteLine($"[INSTANCE-INVITER] Stats: {behaviourCount} [Behaviour] lines, {joiningCount} Joining lines found");
            Console.WriteLine("[INSTANCE-INVITER] ✗ No recent instance found in logs");
            Console.WriteLine("[INSTANCE-INVITER] Tip: Make sure VRChat is running and you're in a world");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-INVITER] Error reading VRChat logs: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> InviteToCurrentInstanceAsync(string userId, IVRChatApiService apiService)
    {
        var currentInstance = GetCurrentInstance();
        if (currentInstance == null)
        {
            Console.WriteLine("[INSTANCE-INVITER] Cannot invite: No current instance detected");
            return false;
        }

        Console.WriteLine($"[INSTANCE-INVITER] Inviting user {userId} to {currentInstance.WorldId}:{currentInstance.InstanceId}");
        return await apiService.InviteUserToInstanceAsync(userId, currentInstance.WorldId, currentInstance.InstanceId);
    }

    public async Task<List<InstanceUser>> GetCurrentInstanceUsersAsync(IVRChatApiService apiService)
    {
        var users = new List<InstanceUser>();
        
        try
        {
            var currentInstance = GetCurrentInstance();
            
            if (currentInstance == null)
            {
                Console.WriteLine("[INSTANCE-USERS] No current instance detected");
                return users;
            }

            Console.WriteLine($"[INSTANCE-USERS] Looking for users in instance: {currentInstance.WorldId}:{currentInstance.InstanceId}");

            // Get instance details from API to verify and get user count
            var instanceDetails = await apiService.GetInstanceAsync(currentInstance.WorldId, currentInstance.InstanceId);
            if (instanceDetails != null)
            {
                Console.WriteLine($"[INSTANCE-USERS] API reports {instanceDetails.UserCount} users in instance");
            }

            // Parse VRChat logs to find users
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
            var vrchatLogPath = Path.Combine(appDataLocal, "VRChat", "VRChat");
            var logFiles = Directory.GetFiles(vrchatLogPath, "output_log_*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (logFiles.Count == 0)
            {
                Console.WriteLine("[INSTANCE-USERS] No VRChat log files found");
                return users;
            }

            var latestLog = logFiles[0];
            Console.WriteLine($"[INSTANCE-USERS] Reading log: {Path.GetFileName(latestLog)}");

            // Read last 10MB of log to capture player join events
            using var stream = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            var bufferSize = Math.Min(10000000, stream.Length); // 10MB
            var buffer = new byte[bufferSize];
            
            if (stream.Length > bufferSize)
            {
                stream.Seek(-bufferSize, SeekOrigin.End);
            }
            
            stream.Read(buffer, 0, (int)bufferSize);
            var logContent = System.Text.Encoding.UTF8.GetString(buffer);
            var lines = logContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();

            Console.WriteLine($"[INSTANCE-USERS] Searching through {lines.Count} log lines");

            // Track when we entered the current instance
            bool foundInstanceEntry = false;
            bool inCurrentInstance = false;
            
            // Regex patterns for finding user information
            var userIdPattern = new Regex(@"usr_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
            var onPlayerJoinedPattern = new Regex(@"OnPlayerJoined\s+(.+?)(?:\s*$)", RegexOptions.Compiled);
            var playerJoinedPattern = new Regex(@"\[Player\]\s+(.+?)\s+joined", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
            // Search from newest to oldest to find current instance boundary
            foreach (var line in lines)
            {
                // Look for when we entered the current instance
                if (!foundInstanceEntry && (line.Contains($"Joining or Creating Room:") || line.Contains($"Rejoining local world:")) && line.Contains(currentInstance.WorldId))
                {
                    Console.WriteLine($"[INSTANCE-USERS] Found instance entry point");
                    foundInstanceEntry = true;
                    inCurrentInstance = true;
                    continue;
                }

                // If we haven't found the instance entry yet, skip
                if (!foundInstanceEntry)
                    continue;

                // If we hit another instance join before this one, stop
                if (inCurrentInstance && (line.Contains("Joining or Creating Room:") || line.Contains("Rejoining local world:")) && !line.Contains(currentInstance.WorldId))
                {
                    Console.WriteLine($"[INSTANCE-USERS] Found previous instance boundary, stopping search");
                    break;
                }

                // Look for OnPlayerJoined events
                if (line.Contains("OnPlayerJoined"))
                {
                    var match = onPlayerJoinedPattern.Match(line);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var displayName = match.Groups[1].Value.Trim();
                        
                        // Extract user ID if present
                        var userIdMatch = userIdPattern.Match(line);
                        var userId = userIdMatch.Success ? userIdMatch.Value : string.Empty;
                        
                        // Only add if not already in list
                        if (!string.IsNullOrEmpty(displayName) && !users.Any(u => u.DisplayName == displayName))
                        {
                            users.Add(new InstanceUser
                            {
                                DisplayName = displayName,
                                UserId = userId
                            });
                            Console.WriteLine($"[INSTANCE-USERS] Found user: {displayName} ({userId})");
                        }
                    }
                }
                
                // Also look for alternate "[Player] X joined" format
                if (line.Contains("[Player]") && line.Contains("joined"))
                {
                    var match = playerJoinedPattern.Match(line);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var displayName = match.Groups[1].Value.Trim();
                        
                        if (!string.IsNullOrEmpty(displayName) && !users.Any(u => u.DisplayName == displayName))
                        {
                            users.Add(new InstanceUser
                            {
                                DisplayName = displayName,
                                UserId = string.Empty // Try to find ID in nearby lines
                            });
                            Console.WriteLine($"[INSTANCE-USERS] Found user from [Player] log: {displayName}");
                        }
                    }
                }
            }
            
            Console.WriteLine($"[INSTANCE-USERS] Found {users.Count} users in current instance from logs");
            
            // If log parsing found no users but API says there are users, at least show the current user
            if (users.Count == 0 && instanceDetails != null && instanceDetails.UserCount > 0 && apiService.IsLoggedIn)
            {
                Console.WriteLine($"[INSTANCE-USERS] No users found in logs but instance has {instanceDetails.UserCount} users");
                Console.WriteLine($"[INSTANCE-USERS] Adding current user as fallback");
                
                if (!string.IsNullOrEmpty(apiService.CurrentUserDisplayName))
                {
                    users.Add(new InstanceUser
                    {
                        DisplayName = apiService.CurrentUserDisplayName ?? "You",
                        UserId = apiService.CurrentUserId ?? string.Empty
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-USERS] Error reading instance users: {ex.Message}");
            Console.WriteLine($"[INSTANCE-USERS] Stack trace: {ex.StackTrace}");
        }
        
        return users;
    }
}

public class CurrentInstanceInfo
{
    public string WorldId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string FullLocation { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}

public class InstanceUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
}
