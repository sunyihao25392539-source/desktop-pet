param(
    [switch]$Demo
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

function T([string]$Value) {
    return [regex]::Unescape($Value)
}

$Text = @{
    App = T '\u91d1\u4e1d\u7334\u684c\u9762\u5ba0\u7269'
    AlreadyRunning = T '\u5c0f\u7334\u5df2\u7ecf\u5728\u684c\u9762\u4e0a\u4e86\uff0c\u4e0d\u9700\u8981\u91cd\u590d\u6253\u5f00\u3002'
    Feed = T '\u5582\u6a31\u6843'
    Love = T '\u7231\u5fc3\u7559\u8a00'
    TopMost = T '\u59cb\u7ec8\u7f6e\u9876'
    Exit = T '\u9000\u51fa'
    Fed = T '\u8fd9\u9897\u6a31\u6843\u9001\u7ed9\u4f60'
}

$createdNew = $false
$singleInstance = [System.Threading.Mutex]::new($true, "GoldenMonkeyDesktopPet.GirlfriendCherry.V4", [ref]$createdNew)
if (-not $createdNew) {
    $singleInstance.Dispose()
    exit 0
}

$AppName = $Text.App
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$SpriteRoot = Join-Path $Root "assets\sprites\girlfriend_cherry"
$FramePaths = @{
    idle = Join-Path $SpriteRoot "idle.png"
    blink_half = Join-Path $SpriteRoot "blink_half.png"
    blink_closed = Join-Path $SpriteRoot "blink_closed.png"
    happy = Join-Path $SpriteRoot "happy.png"
}

foreach ($framePath in $FramePaths.Values) {
    if (-not (Test-Path -LiteralPath $framePath)) {
        [System.Windows.Forms.MessageBox]::Show("Missing sprite: $framePath", $AppName) | Out-Null
        $singleInstance.ReleaseMutex()
        $singleInstance.Dispose()
        exit 1
    }
}

$transparent = [System.Drawing.Color]::Magenta
$frames = @{}
foreach ($name in $FramePaths.Keys) {
    $frames[$name] = [System.Drawing.Image]::FromFile($FramePaths[$name])
}
$image = $frames.idle

$form = New-Object System.Windows.Forms.Form
$form.Text = $AppName
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.BackColor = $transparent
$form.TransparencyKey = $transparent
$form.Width = $image.Width
$form.Height = $image.Height + 42

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Left = [Math]::Max(0, $screen.Right - $form.Width - 80)
$form.Top = [Math]::Max(0, $screen.Bottom - $form.Height - 80)

$canvas = New-Object System.Windows.Forms.Panel
$canvas.Dock = [System.Windows.Forms.DockStyle]::Fill
$canvas.BackColor = $transparent
$form.Controls.Add($canvas)

$pet = New-Object System.Windows.Forms.PictureBox
$pet.Image = $image
$pet.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::AutoSize
$pet.BackColor = [System.Drawing.Color]::Transparent
$pet.Left = 0
$pet.Top = 28
$canvas.Controls.Add($pet)

$script:dragging = $false
$script:dragPending = $false
$script:dragStartCursor = New-Object System.Drawing.Point 0, 0
$script:dragStartForm = New-Object System.Drawing.Point 0, 0
$script:rng = New-Object System.Random
$script:effects = New-Object System.Collections.ArrayList
$script:animationFrames = @()
$script:animationIndex = 0
$script:nextAnimationFrame = [DateTime]::Now
$script:nextBlink = [DateTime]::Now.AddMilliseconds($script:rng.Next(2200, 4800))

function Start-PetAnimation([string[]]$Names, [int]$Interval) {
    $script:animationFrames = $Names
    $script:animationIndex = 0
    $script:animationInterval = $Interval
    $script:nextAnimationFrame = [DateTime]::Now
}

function Show-HappyPet {
    Start-PetAnimation @("happy", "happy", "happy", "idle") 320
}

$animationTimer = New-Object System.Windows.Forms.Timer
$animationTimer.Interval = 70
$animationTimer.Add_Tick({
    $now = [DateTime]::Now
    if ($script:animationIndex -lt $script:animationFrames.Count) {
        if ($now -ge $script:nextAnimationFrame) {
            $frameName = $script:animationFrames[$script:animationIndex]
            $pet.Image = $frames[$frameName]
            $script:animationIndex += 1
            $script:nextAnimationFrame = $now.AddMilliseconds($script:animationInterval)
            if ($script:animationIndex -ge $script:animationFrames.Count) {
                $script:nextBlink = $now.AddMilliseconds($script:rng.Next(2400, 5200))
            }
        }
    } elseif ($now -ge $script:nextBlink) {
        Start-PetAnimation @("blink_half", "blink_closed", "blink_half", "idle") 90
    }
})
$animationTimer.Start()

function New-FloatingText([string]$Text) {
    $label = New-Object System.Windows.Forms.Label
    $label.AutoSize = $true
    $label.Text = $Text
    $label.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 16, [System.Drawing.FontStyle]::Bold)
    $label.ForeColor = [System.Drawing.Color]::FromArgb(107, 58, 22)
    $label.BackColor = $transparent
    $label.Left = [Math]::Max(0, [int](($form.Width - 160) / 2))
    $label.Top = 6
    $canvas.Controls.Add($label)
    $label.BringToFront()

    [void]$script:effects.Add([pscustomobject]@{
        Control = $label
        Age = 0
        Lifetime = 58
        Rise = 1
        Drift = 0
    })
}

function New-Heart([int]$Count) {
    1..$Count | ForEach-Object {
        $heartSize = $script:rng.Next(22, 34)
        $colors = @(
            [System.Drawing.Color]::FromArgb(255, 107, 138),
            [System.Drawing.Color]::FromArgb(255, 143, 176),
            [System.Drawing.Color]::FromArgb(233, 75, 106)
        )
        $heart = New-Object System.Windows.Forms.Panel
        $heart.Size = New-Object System.Drawing.Size $heartSize, $heartSize
        $heart.BackColor = $colors[$script:rng.Next(0, $colors.Count)]

        $points = [System.Drawing.Point[]]@(
            (New-Object System.Drawing.Point ([int]($heartSize * 0.50)), ([int]($heartSize * 0.20))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.34)), ([int]($heartSize * 0.05))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.12)), ([int]($heartSize * 0.08))),
            (New-Object System.Drawing.Point 0, ([int]($heartSize * 0.28))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.04)), ([int]($heartSize * 0.50))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.50)), ([int]($heartSize * 0.96))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.96)), ([int]($heartSize * 0.50))),
            (New-Object System.Drawing.Point $heartSize, ([int]($heartSize * 0.28))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.88)), ([int]($heartSize * 0.08))),
            (New-Object System.Drawing.Point ([int]($heartSize * 0.66)), ([int]($heartSize * 0.05)))
        )
        $heartPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $heartPath.AddPolygon($points)
        $heart.Region = New-Object System.Drawing.Region $heartPath
        $heartPath.Dispose()
        $heart.Left = $script:rng.Next([int]($form.Width / 3), [int]($form.Width * 2 / 3))
        $heart.Top = $script:rng.Next([int]($form.Height / 3), [int]($form.Height / 2))
        $canvas.Controls.Add($heart)
        $heart.BringToFront()

        [void]$script:effects.Add([pscustomobject]@{
            Control = $heart
            Age = 0
            Lifetime = 42
            Rise = 2
            Drift = $script:rng.Next(-1, 2)
        })
    }
}

$effectTimer = New-Object System.Windows.Forms.Timer
$effectTimer.Interval = 45
$effectTimer.Add_Tick({
    for ($i = $script:effects.Count - 1; $i -ge 0; $i--) {
        $effect = $script:effects[$i]
        $control = $effect.Control
        if ($null -eq $control -or $control.IsDisposed) {
            $script:effects.RemoveAt($i)
            continue
        }

        $effect.Age += 1
        $control.SetBounds(
            $control.Left + $effect.Drift,
            $control.Top - $effect.Rise,
            $control.Width,
            $control.Height
        )
        if ($effect.Age -gt $effect.Lifetime) {
            $canvas.Controls.Remove($control)
            $control.Dispose()
            $script:effects.RemoveAt($i)
        }
    }
})
$effectTimer.Start()

function Show-LoveNote {
    $notes = @(
        (T '\u597d\u60f3\u4f60\u5440'),
        (T '\u6a31\u6843\u9001\u7ed9\u4f60'),
        (T '\u4eca\u5929\u4e5f\u8981\u5f00\u5fc3'),
        (T '\u6211\u4f1a\u4e00\u76f4\u966a\u7740\u4f60')
    )
    New-FloatingText $notes[$script:rng.Next(0, $notes.Count)]
    New-Heart 7
    Show-HappyPet
}

function Feed-Pet {
    New-FloatingText $Text.Fed
    New-Heart 5
    Show-HappyPet
}

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$feedItem = $menu.Items.Add($Text.Feed)
$noteItem = $menu.Items.Add($Text.Love)
[void]$menu.Items.Add("-")
$topItem = $menu.Items.Add($Text.TopMost)
$topItem.Checked = $true
$exitItem = $menu.Items.Add($Text.Exit)
$feedItem.Add_Click({ Feed-Pet })
$noteItem.Add_Click({ Show-LoveNote })
$topItem.Add_Click({
    $topItem.Checked = -not $topItem.Checked
    $form.TopMost = $topItem.Checked
})
$exitItem.Add_Click({ $form.Close() })
$canvas.ContextMenuStrip = $menu
$pet.ContextMenuStrip = $menu

$mouseDown = {
    param($sender, $event)
    if ($event.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $script:dragPending = $true
        $script:dragging = $false
        $script:dragStartCursor = [System.Windows.Forms.Cursor]::Position
        $script:dragStartForm = $form.Location
    }
}

$mouseMove = {
    param($sender, $event)
    if ($script:dragPending) {
        $pos = [System.Windows.Forms.Cursor]::Position
        $deltaX = $pos.X - $script:dragStartCursor.X
        $deltaY = $pos.Y - $script:dragStartCursor.Y
        if (([Math]::Abs($deltaX) + [Math]::Abs($deltaY)) -ge 5) {
            $script:dragging = $true
            $form.SetDesktopLocation(
                $script:dragStartForm.X + $deltaX,
                $script:dragStartForm.Y + $deltaY
            )
        }
    }
}

$mouseUp = {
    param($sender, $event)
    if ($event.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $script:dragPending = $false
        $script:dragging = $false
    }
}

$doubleClick = { Show-LoveNote }

$canvas.Add_MouseDown($mouseDown)
$canvas.Add_MouseMove($mouseMove)
$canvas.Add_MouseUp($mouseUp)
$canvas.Add_DoubleClick($doubleClick)
$pet.Add_MouseDown($mouseDown)
$pet.Add_MouseMove($mouseMove)
$pet.Add_MouseUp($mouseUp)
$pet.Add_DoubleClick($doubleClick)

$randomHeart = New-Object System.Windows.Forms.Timer
$randomHeart.Interval = 6500
$randomHeart.Add_Tick({
    if ((-not $script:dragging) -and ($script:rng.NextDouble() -lt 0.35)) {
        New-Heart 1
    }
})
$randomHeart.Start()

$script:demoTimer = $null
$script:demoStep = 0
if ($Demo) {
    $form.SetDesktopLocation(
        [int]($screen.Left + (($screen.Width - $form.Width) / 2)),
        [int]($screen.Top + (($screen.Height - $form.Height) / 2))
    )
    $form.Add_Shown({
        $form.TopMost = $true
        $form.Activate()
        $form.BringToFront()
        Feed-Pet
        $script:demoTimer = New-Object System.Windows.Forms.Timer
        $script:demoTimer.Interval = 1800
        $script:demoTimer.Add_Tick({
            $script:demoStep += 1
            if ($script:demoStep -eq 1) {
                Show-LoveNote
            } else {
                $script:demoTimer.Stop()
                $script:demoTimer.Dispose()
                $script:demoTimer = $null
            }
        })
        $script:demoTimer.Start()
    })
}

[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::Run($form)

$effectTimer.Dispose()
$animationTimer.Dispose()
$randomHeart.Dispose()
if ($null -ne $script:demoTimer) {
    $script:demoTimer.Dispose()
}
foreach ($frame in $frames.Values) {
    $frame.Dispose()
}
$singleInstance.ReleaseMutex()
$singleInstance.Dispose()

