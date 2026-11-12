function Get-UiSnapshot {
    param($Process)

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $handle = $Process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        throw 'ControllerPlayground has no main window yet. Wait for startup and restore the window.'
    }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    if ($null -eq $window -or $window.Current.IsOffscreen) {
        throw 'The main window is unavailable or minimized.'
    }

    $markers = [ordered]@{}
    foreach ($id in 'ShellGuideButton', 'DiscordButton', 'ExitAppButton', 'CancelExitButton', 'ConfirmExitButton') {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        $visible = 0
        foreach ($element in $elements) {
            if (!$element.Current.IsOffscreen) { $visible++ }
        }
        $markers[$id] = $visible
    }
    if ($markers.ShellGuideButton -ne 1) {
        throw 'Expected one visible ShellGuideButton in the accessibility tree. Check that the app is fully loaded and exposes its WinUI controls.'
    }

    $focus = $null
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    for ($ancestor = $focused; $null -ne $ancestor; $ancestor = $walker.GetParent($ancestor)) {
        if ([System.Windows.Automation.Automation]::Compare($ancestor, $window)) {
            $current = $focused.Current
            if ($current.HasKeyboardFocus -and !$current.IsOffscreen) {
                $focus = [ordered]@{
                    automationId = $current.AutomationId
                    name = $current.Name
                    controlType = $current.ControlType.ProgrammaticName
                    runtimeId = @($focused.GetRuntimeId())
                }
            }
            break
        }
    }

    [ordered]@{
        processId = $Process.Id
        processStartedUtc = $Process.StartTime.ToUniversalTime().ToString('o')
        windowHandle = '0x{0:X}' -f $handle.ToInt64()
        markers = $markers
        focus = $focus
    }
}

function Get-UiAssertionFailure {
    param($Scenario, $Snapshot, $AutomationId, $Baseline)

    $markers = $Snapshot.markers
    switch ($Scenario) {
        'guide-open' {
            if ($markers.DiscordButton -ne 1) { return 'Expected one visible Guide overlay Discord button.' }
        }
        'guide-closed' {
            if ($markers.DiscordButton -ne 0) { return 'Expected the Guide overlay Discord button to be hidden.' }
        }
        'exit-open' {
            if ($markers.CancelExitButton -ne 1 -or $markers.ConfirmExitButton -ne 1) {
                return 'Expected one visible Cancel button and one visible Confirm button.'
            }
            if ($Snapshot.focus.automationId -cne 'CancelExitButton') {
                return 'Expected focus on Cancel immediately after opening the exit confirmation. Keep the app in the foreground.'
            }
        }
        { $_ -in 'exit-closed', 'exit-focus-restored' } {
            if ($markers.ExitAppButton -ne 1 -or $markers.CancelExitButton -ne 0 -or $markers.ConfirmExitButton -ne 0) {
                return 'Expected the Settings exit button to be visible and both confirmation buttons to be hidden.'
            }
            if ($Scenario -eq 'exit-focus-restored') {
                $before = $Baseline.evidence
                if ($before.processId -ne $Snapshot.processId -or
                    $before.processStartedUtc -cne $Snapshot.processStartedUtc -or
                    $before.windowHandle -cne $Snapshot.windowHandle) {
                    return 'The baseline and current capture must refer to the same running app window.'
                }
                if ($Snapshot.focus.automationId -cne 'ExitAppButton' -or
                    ($before.focus.runtimeId -join ',') -cne ($Snapshot.focus.runtimeId -join ',')) {
                    return 'Expected focus on the same exit button captured before opening confirmation.'
                }
            }
        }
        'focused' {
            if ($Snapshot.focus.automationId -cne $AutomationId) {
                return "Expected keyboard focus on '$AutomationId'. Keep the app in the foreground."
            }
        }
    }
}
