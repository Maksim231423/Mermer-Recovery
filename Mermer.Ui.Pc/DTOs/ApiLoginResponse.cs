using System.Collections.Generic;

namespace Mermer.Ui.Pc.DTOs;

public class ApiLoginResponse
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string Token { get; set; } = string.Empty;
}