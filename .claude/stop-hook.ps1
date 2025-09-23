# Claude Code Stop Hook Script
param()

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$currentWorkingDir = $env:cwd
if (-not $currentWorkingDir) {
    $currentWorkingDir = Get-Location
}

# Send toast notification
try {
    Import-Module BurntToast -ErrorAction SilentlyContinue
    if (Get-Module -Name BurntToast) {
        $projectName = Split-Path $currentWorkingDir -Leaf
        New-BurntToastNotification -Text "Claude Task Completed", $projectName, "Completed at: $timestamp"
    }
}
catch {
    # Silently ignore notification failures
}

# Send email notification
try {
    $projectName = Split-Path $currentWorkingDir -Leaf
    $emailSubject = "Claude Task Completed"
    $emailBody = "Project: $projectName`nDirectory: $currentWorkingDir`nCompleted at: $timestamp"

    if (Test-Path 'E:\tool\SendMail\SendMail.exe') {
        & 'E:\tool\SendMail\SendMail.exe' $emailSubject $emailBody
    }
}
catch {
    # Silently ignore email failures
}