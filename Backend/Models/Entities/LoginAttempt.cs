using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One login attempt, successful or not. Backs the "Login Successes" and "Login Errors" tabs of
/// the existing /activity-logs page, which has been calling those endpoints and silently
/// rendering empty because no backend existed.
///
/// Stores the attempted email as free text rather than a user FK — failed attempts frequently
/// reference an address that matches no account, which is exactly what you want to see.
/// </summary>
public class LoginAttempt
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    public bool Success { get; set; }

    /// <summary>Why it failed, e.g. "Invalid password", "Account inactive". Null on success.</summary>
    [MaxLength(200)]
    public string? FailureReason { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>Resolved only when the email matched a real account.</summary>
    public int? UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
