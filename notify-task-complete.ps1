# Windows notification script for Claude Code task completion
param(
    [string]$Message = "Claude Code task completed",
    [string]$Title = "Claude Code"
)

try {
    # Try using BurntToast module if available
    if (Get-Module -ListAvailable -Name BurntToast) {
        Import-Module BurntToast
        New-BurntToastNotification -Text $Title, $Message
        exit 0
    }

    # Fallback: Use msg command for simple popup
    $msgText = "$Title`n$Message"
    Start-Process -FilePath "msg.exe" -ArgumentList @("*", "/time:5", $msgText) -NoNewWindow -Wait

} catch {
    # Ultimate fallback: Write to console
    Write-Host "[$Title] $Message" -ForegroundColor Green
}