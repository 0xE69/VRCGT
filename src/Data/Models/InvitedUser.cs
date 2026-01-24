using System;
using System.ComponentModel.DataAnnotations;

namespace VRCGroupTools.Data.Models;

public class InvitedUser
{
    [Key]
    public int Id { get; set; }
    
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePicUrl { get; set; }
    
    public bool IsAgeVerified { get; set; }
    public string? TrustLevel { get; set; }
    
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;
    public string? WorldId { get; set; }
    public string? InstanceId { get; set; }
    
    public bool InviteSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}
