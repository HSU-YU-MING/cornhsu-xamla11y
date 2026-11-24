# .NET Framework 4.8 cross-check (Windows PowerShell 5.1 runs on it): same facts file as 'uiaoracle names'.
# Names are read through a UIA *client* on another thread. Reading in-process peers gives different answers for
# LabeledBy and item containers, because WPF only creates those peers once a client walks the tree.
# Keep this file ASCII: PS 5.1 reads BOM-less files as the ANSI code page.
param([string]$facts)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, UIAutomationTypes, UIAutomationClient
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$icon = "<Path Data='M0,0 L10,10 L0,10 Z' Fill='Black' Width='12' Height='12'/>"
"runtime $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)"
$ns = 'xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:sys="clr-namespace:System;assembly=mscorlib"'

$rs = [RunspaceFactory]::CreateRunspace(); $rs.ApartmentState = 'MTA'; $rs.Open()
$client = {
  param($h)
  Add-Type -AssemblyName UIAutomationTypes, UIAutomationClient
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  $stack = New-Object System.Collections.Stack
  $stack.Push([System.Windows.Automation.AutomationElement]::FromHandle($h))
  $found = $null
  while ($stack.Count -gt 0) {
    $e = $stack.Pop()
    if ($e.Current.AutomationId -eq 'p') { $found = $e }
    for ($k = $walker.GetFirstChild($e); $k -ne $null; $k = $walker.GetNextSibling($k)) { $stack.Push($k) }
  }
  if ($found -eq $null) { '(no peer)' } else { '"' + $found.Current.Name + '"' }
}

foreach ($line in [IO.File]::ReadAllLines($facts, [Text.Encoding]::UTF8)) {
  if ($line.Trim().Length -eq 0 -or $line.StartsWith('#')) { continue }
  $p = $line -split ' \| '
  $body = $p[2].Replace('[ICON]', $icon).Replace("'", '"'); $res = ''
  if ($p.Count -gt 3) { $res = $p[3].Replace('[ICON]', $icon).Replace("'", '"') }
  try {
    $w = [System.Windows.Markup.XamlReader]::Parse("<Window $ns Width=`"300`" Height=`"200`" Left=`"40`" Top=`"40`" ShowActivated=`"False`" ShowInTaskbar=`"False`"><Window.Resources><ResourceDictionary>$res</ResourceDictionary></Window.Resources><StackPanel x:Name=`"host`">$body</StackPanel></Window>")
    $w.Show(); $w.UpdateLayout()
    $w.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $h = (New-Object System.Windows.Interop.WindowInteropHelper $w).Handle
    $ps = [PowerShell]::Create(); $ps.Runspace = $rs
    [void]$ps.AddScript($client).AddArgument($h)
    $async = $ps.BeginInvoke()
    while (-not $async.IsCompleted) { $w.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle); Start-Sleep -Milliseconds 5 }
    $name = $ps.EndInvoke($async); $ps.Dispose()
    "  {0,-46} {1}" -f $p[1], ($name -join '')
    $w.Close()
  } catch { "  {0,-46} !! {1}" -f $p[1], $_.Exception.Message }
}
