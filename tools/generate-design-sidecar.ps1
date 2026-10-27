$ErrorActionPreference = 'Stop'

# Generates .impeccable/design.json — the DESIGN.md sidecar.
# Tonal ramps are computed in HSL space from the canonical hex values in DESIGN.md so that the
# ramps are arithmetically consistent with the tokens they describe.

function Convert-HexToRgb([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 3) { $h = "$h$h$h$h$h$h" }
    $r = [Convert]::ToInt32($h.Substring(0, 2), 16) / 255.0
    $g = [Convert]::ToInt32($h.Substring(2, 2), 16) / 255.0
    $b = [Convert]::ToInt32($h.Substring(4, 2), 16) / 255.0
    return @($r, $g, $b)
}

function Convert-RgbToHsl([double[]]$rgb) {
    $r, $g, $b = $rgb
    $max = [Math]::Max($r, [Math]::Max($g, $b))
    $min = [Math]::Min($r, [Math]::Min($g, $b))
    $l = ($max + $min) / 2.0
    if ($max -eq $min) { return @(0.0, 0.0, $l) }
    $d = $max - $min
    $s = if ($l -gt 0.5) { $d / (2.0 - $max - $min) } else { $d / ($max + $min) }
    if ($max -eq $r) {
        $h = ($g - $b) / $d
        if ($g -lt $b) { $h += 6.0 }
    }
    elseif ($max -eq $g) { $h = ($b - $r) / $d + 2.0 }
    else { $h = ($r - $g) / $d + 4.0 }
    $h /= 6.0
    return @($h, $s, $l)
}

function Convert-HslToHex([double]$h, [double]$s, [double]$l) {
    $h = ($h % 1.0 + 1.0) % 1.0
    if ($s -eq 0) {
        $v = [int][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, $l * 255)))
        return '#{0:X2}{1:X2}{2:X2}' -f $v, $v, $v
    }
    $q = if ($l -lt 0.5) { $l * (1.0 + $s) } else { $l + $s - $l * $s }
    $p = 2.0 * $l - $q

    $t1 = (($h + 1.0 / 3.0) % 1.0 + 1.0) % 1.0
    $t2 = ($h % 1.0 + 1.0) % 1.0
    $t3 = (($h - 1.0 / 3.0) % 1.0 + 1.0) % 1.0

    $c1 = if ($t1 -lt 1.0 / 6.0) { $p + ($q - $p) * 6.0 * $t1 } elseif ($t1 -lt 0.5) { $q } elseif ($t1 -lt 2.0 / 3.0) { $p + ($q - $p) * (2.0 / 3.0 - $t1) * 6.0 } else { $p }
    $c2 = if ($t2 -lt 1.0 / 6.0) { $p + ($q - $p) * 6.0 * $t2 } elseif ($t2 -lt 0.5) { $q } elseif ($t2 -lt 2.0 / 3.0) { $p + ($q - $p) * (2.0 / 3.0 - $t2) * 6.0 } else { $p }
    $c3 = if ($t3 -lt 1.0 / 6.0) { $p + ($q - $p) * 6.0 * $t3 } elseif ($t3 -lt 0.5) { $q } elseif ($t3 -lt 2.0 / 3.0) { $p + ($q - $p) * (2.0 / 3.0 - $t3) * 6.0 } else { $p }

    $r = [int][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, $c1 * 255)))
    $g = [int][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, $c2 * 255)))
    $b = [int][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, $c3 * 255)))
    return '#{0:X2}{1:X2}{2:X2}' -f $r, $g, $b
}

# 8 steps, dark to light: lightness 15% -> 95%, saturation eased towards the ends so the
# extreme steps stay legible as surfaces rather than as pure pigment.
$lightnessSteps = @(0.15, 0.28, 0.40, 0.52, 0.64, 0.76, 0.86, 0.95)

function Get-TonalRamp([string]$hex) {
    $hsl = Convert-RgbToHsl (Convert-HexToRgb $hex)
    $h, $s, $l = $hsl
    $ramp = @()
    foreach ($target in $lightnessSteps) {
        # Saturation never exceeds the canonical value: at the dark end a near-neutral token
        # (ink) would otherwise turn visibly blue, because a tiny hue difference in a
        # near-grey is amplified when saturation is scaled up.
        $fade = 1.0 - 0.55 * ([Math]::Abs($target - 0.5) / 0.5)
        $stepS = $s * $fade
        $ramp += (Convert-HslToHex $h $stepS $target)
    }
    return $ramp
}

# slug, displayName, role, canonical hex  — mirrors the DESIGN.md frontmatter exactly.
$colorDefs = @(
    @('paper-canvas', 'Paper Canvas', 'neutral', '#F7F5F1'),
    @('paper-raised', 'Paper Raised', 'neutral', '#FFFFFF'),
    @('paper-sunken', 'Paper Sunken', 'neutral', '#EFECE6'),
    @('paper-overlay', 'Paper Overlay', 'neutral', '#FFFFFF'),
    @('rule-subtle', 'Rule Subtle', 'neutral', '#E2DED6'),
    @('rule-strong', 'Rule Strong', 'neutral', '#8B8578'),
    @('ink-primary', 'Ink Primary', 'neutral', '#16181D'),
    @('ink-secondary', 'Ink Secondary', 'neutral', '#4A4E57'),
    @('ink-muted', 'Ink Muted', 'neutral', '#666C73'),
    @('ink-inverse', 'Ink Inverse', 'neutral', '#FFFFFF'),
    @('petrol', 'Petrol', 'primary', '#0F5E63'),
    @('petrol-deep', 'Petrol Deep', 'primary', '#0C4C50'),
    @('petrol-deeper', 'Petrol Deeper', 'primary', '#093D41'),
    @('petrol-wash', 'Petrol Wash', 'primary', '#DCEBEC'),
    @('petrol-ink', 'Petrol Ink', 'primary', '#FFFFFF'),
    @('seal-critical', 'Seal Critical', 'secondary', '#B3261E'),
    @('seal-warning', 'Seal Warning', 'secondary', '#8A5200'),
    @('seal-success', 'Seal Success', 'secondary', '#2E6B3E'),
    @('seal-info', 'Seal Info', 'secondary', '#1F5C99'),
    @('seal-critical-wash', 'Seal Critical Wash', 'secondary', '#FBE9E7'),
    @('seal-warning-wash', 'Seal Warning Wash', 'secondary', '#FBF0DC'),
    @('seal-success-wash', 'Seal Success Wash', 'secondary', '#E4F0E6'),
    @('seal-info-wash', 'Seal Info Wash', 'secondary', '#E3EEF8'),
    @('focus-ring', 'Focus Ring', 'primary', '#0F5E63'),
    @('selection-wash', 'Selection Wash', 'neutral', '#DCEBEC'),
    @('selection-ink', 'Selection Ink', 'neutral', '#16181D'),
    @('series-1', 'Series 1', 'tertiary', '#0F5E63'),
    @('series-2', 'Series 2', 'tertiary', '#B26B00'),
    @('series-3', 'Series 3', 'tertiary', '#2E6B3E'),
    @('series-4', 'Series 4', 'tertiary', '#B3261E'),
    @('series-5', 'Series 5', 'tertiary', '#1F5C99'),
    @('series-6', 'Series 6', 'tertiary', '#6B4E9B')
)

$colorMeta = [ordered]@{}
foreach ($d in $colorDefs) {
    $colorMeta[$d[0]] = [ordered]@{
        role        = $d[2]
        displayName = $d[1]
        canonical   = $d[3]
        tonalRamp   = @(Get-TonalRamp $d[3])
    }
}

$typographyMeta = [ordered]@{
    display  = [ordered]@{ displayName = 'Display';  purpose = 'The grand total on the point-of-sale screen and window-level titles. Deliberately rare.' }
    headline = [ordered]@{ displayName = 'Headline'; purpose = 'Screen titles and section headings.' }
    title    = [ordered]@{ displayName = 'Title';    purpose = 'Card titles and sub-section headings.' }
    body     = [ordered]@{ displayName = 'Body';     purpose = 'Labels, table cells, and message text.' }
    label    = [ordered]@{ displayName = 'Label';    purpose = 'Column headers, captions, and badge text.' }
}

$components = @(
    [ordered]@{
        name        = 'Primary Button'
        kind        = 'button'
        refersTo    = 'button-primary'
        description = 'The one action a screen exists to perform. Petrol fill, one per screen.'
        html        = '<button class="ds-btn-primary">Confirm sale</button>'
        css         = @'
.ds-btn-primary { position: relative; background: #0F5E63; color: #FFFFFF; border: 1px solid #0F5E63; border-radius: 6px; padding: 0 16px; height: 32px; min-width: 32px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; font-weight: 400; cursor: pointer; transition: opacity 0.12s ease-out; }
.ds-btn-primary:hover { opacity: 0.9; }
.ds-btn-primary:active { opacity: 0.75; }
.ds-btn-primary:focus-visible { outline: 2px solid #0F5E63; outline-offset: 3px; opacity: 1; }
.ds-btn-primary:disabled { opacity: 0.45; cursor: default; }
'@
    },
    [ordered]@{
        name        = 'Secondary Button'
        kind        = 'button'
        refersTo    = 'button-secondary'
        description = 'The workhorse action. Raised paper with a visible rule-strong border.'
        html        = '<button class="ds-btn-secondary">Print receipt</button>'
        css         = @'
.ds-btn-secondary { background: #FFFFFF; color: #16181D; border: 1px solid #8B8578; border-radius: 6px; padding: 0 16px; height: 32px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; cursor: pointer; transition: opacity 0.12s ease-out; }
.ds-btn-secondary:hover { opacity: 0.9; }
.ds-btn-secondary:active { opacity: 0.75; }
.ds-btn-secondary:focus-visible { outline: 2px solid #0F5E63; outline-offset: 3px; opacity: 1; }
.ds-btn-secondary:disabled { opacity: 0.45; cursor: default; }
'@
    },
    [ordered]@{
        name        = 'Danger Button'
        kind        = 'button'
        refersTo    = 'button-danger'
        description = 'Destructive confirmation only. Seal-critical fill, never a default action.'
        html        = '<button class="ds-btn-danger">Delete</button>'
        css         = @'
.ds-btn-danger { background: #B3261E; color: #FFFFFF; border: 1px solid #B3261E; border-radius: 6px; padding: 0 16px; height: 32px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; cursor: pointer; transition: opacity 0.12s ease-out; }
.ds-btn-danger:hover { opacity: 0.9; }
.ds-btn-danger:active { opacity: 0.75; }
.ds-btn-danger:focus-visible { outline: 2px solid #0F5E63; outline-offset: 3px; opacity: 1; }
.ds-btn-danger:disabled { opacity: 0.45; cursor: default; }
'@
    },
    [ordered]@{
        name        = 'Ghost Button'
        kind        = 'button'
        refersTo    = 'button-ghost'
        description = 'Tertiary and toolbar actions with no fill.'
        html        = '<button class="ds-btn-ghost">Cancel</button>'
        css         = @'
.ds-btn-ghost { background: transparent; color: #4A4E57; border: 1px solid transparent; border-radius: 6px; padding: 0 16px; height: 32px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; cursor: pointer; transition: opacity 0.12s ease-out; }
.ds-btn-ghost:hover { opacity: 0.9; }
.ds-btn-ghost:active { opacity: 0.75; }
.ds-btn-ghost:focus-visible { outline: 2px solid #0F5E63; outline-offset: 3px; opacity: 1; }
'@
    },
    [ordered]@{
        name        = 'Text Field'
        kind        = 'input'
        refersTo    = 'input-field'
        description = 'A single-line input with a visible rule-strong border that shifts to petrol on hover.'
        html        = '<input class="ds-input" type="text" placeholder="Scan or type a barcode" />'
        css         = @'
.ds-input { background: #FFFFFF; color: #16181D; border: 1px solid #8B8578; border-radius: 6px; padding: 10px 6px; height: 32px; box-sizing: border-box; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; }
.ds-input:hover { border-color: #0F5E63; }
.ds-input:focus { outline: 2px solid #0F5E63; outline-offset: 3px; border-color: #0F5E63; }
.ds-input:disabled { opacity: 0.5; }
'@
    },
    [ordered]@{
        name        = 'Status Badge'
        kind        = 'chip'
        refersTo    = 'chip-badge'
        description = 'A status chip. Always pairs a colour with an icon and a word, so it survives greyscale.'
        html        = '<span class="ds-badge ds-badge-success"><span class="ds-badge-icon">&#10003;</span>Paid</span>'
        css         = @'
.ds-badge { display: inline-flex; align-items: center; gap: 4px; background: #EFECE6; color: #4A4E57; border: 1px solid #E2DED6; border-radius: 4px; padding: 2px 8px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 12px; line-height: 1.4; }
.ds-badge-icon { font-size: 11px; }
.ds-badge-success { background: #E4F0E6; color: #2E6B3E; border-color: #E4F0E6; }
.ds-badge-warning { background: #FBF0DC; color: #8A5200; border-color: #FBF0DC; }
.ds-badge-critical { background: #FBE9E7; color: #B3261E; border-color: #FBE9E7; }
.ds-badge-info { background: #E3EEF8; color: #1F5C99; border-color: #E3EEF8; }
'@
    },
    [ordered]@{
        name        = 'Navigation Rail Item'
        kind        = 'nav'
        refersTo    = 'nav-rail'
        description = 'A destination in the leading-edge rail. Petrol when active; mirrors wholesale under RTL.'
        html        = '<a class="ds-nav-item ds-nav-item-active" href="#"><span class="ds-nav-icon">&#9636;</span>Sales</a>'
        css         = @'
.ds-nav-item { display: flex; align-items: center; gap: 8px; padding: 10px 8px; border-radius: 4px; color: #4A4E57; text-decoration: none; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; transition: background-color 0.12s ease-out; }
.ds-nav-item:hover { background: #EFECE6; }
.ds-nav-item-active { background: #DCEBEC; color: #16181D; font-weight: 600; }
.ds-nav-icon { font-size: 14px; }
'@
    },
    [ordered]@{
        name        = 'Card Surface'
        kind        = 'card'
        refersTo    = 'card-surface'
        description = 'A content container: raised paper over canvas, one hairline rule, no shadow.'
        html        = '<div class="ds-card"><h3 class="ds-card-title">Today</h3><p class="ds-card-body">Sales so far today.</p></div>'
        css         = @'
.ds-card { background: #FFFFFF; color: #16181D; border: 1px solid #E2DED6; border-radius: 6px; padding: 16px; }
.ds-card-title { margin: 0 0 8px; font-size: 16px; font-weight: 600; }
.ds-card-body { margin: 0; font-size: 14px; color: #4A4E57; }
'@
    },
    [ordered]@{
        name        = 'Data Grid Row'
        kind        = 'custom'
        refersTo    = 'data-grid-row'
        description = 'A 32 px ledger row: hairline bottom rule, alternating sunken fill, tabular figures aligned right.'
        html        = '<table class="ds-grid"><thead><tr><th>SKU</th><th class="ds-grid-num">Quantity</th></tr></thead><tbody><tr><td>SKU-1042</td><td class="ds-grid-num">12</td></tr></tbody></table>'
        css         = @'
.ds-grid { width: 100%; border-collapse: collapse; background: #FFFFFF; border: 1px solid #E2DED6; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 14px; }
.ds-grid th { background: #EFECE6; color: #4A4E57; font-size: 12px; font-weight: 600; text-align: left; padding: 8px 10px; border-bottom: 1px solid #E2DED6; }
.ds-grid td { padding: 0 10px; height: 32px; border-bottom: 1px solid #E2DED6; }
.ds-grid tbody tr:nth-child(even) { background: #EFECE6; }
.ds-grid tbody tr:hover { background: #DCEBEC; }
.ds-grid-num { text-align: right; font-variant-numeric: tabular-nums lining-nums; }
'@
    },
    [ordered]@{
        name        = 'Scan Chip'
        kind        = 'custom'
        refersTo    = 'scan-chip'
        description = 'The signature element: a confirmation chip that materialises in the cart quantity column after a successful scan.'
        html        = '<div class="ds-scan-chip"><span class="ds-scan-name">Basmati rice 1 kg</span><span class="ds-scan-price">85.00</span></div>'
        css         = @'
.ds-scan-chip { display: inline-flex; align-items: baseline; gap: 8px; background: #0F5E63; color: #FFFFFF; border-radius: 4px; padding: 2px 8px; font-family: "Segoe UI", "Sakkal Majalla", sans-serif; font-size: 12px; font-weight: 600; }
.ds-scan-price { font-variant-numeric: tabular-nums lining-nums; }
'@
    }
)

$narrative = [ordered]@{
    northStar = 'The Ledger Ink'
    overview  = 'StoreFlow is an operating surface, not a showcase. Its world is the shop''s own paper ledger: a warm off-white ground the colour of a receipt, near-black ink for anything the owner must read precisely, and a single deep petrol accent that appears only where the interface asks for a decision. Density is the discipline here - management screens run compact (32 px rows, 8 px spacing rhythm) so a thousand SKUs are scannable; the point-of-sale screen runs generous (44 px hit targets, a 34 px grand total) so a standing cashier reads it under shop lighting without a mouse. The interface is bilingual by construction rather than by a translation pass, so right-to-left is a first-class layout mode, not a mirror applied at the end. Nothing asks the user to translate meaning: status is always carried by an icon and a word as well as a colour, and every error states the next action instead of reporting a fault.'
    keyCharacteristics = @(
        'Warm paper ground, near-black ink, one deep petrol accent used sparingly.',
        'Zero literal colour in view code - every colour is a semantic role resolved at runtime.',
        'Compact management tables, generous point-of-sale targets, one density token family.',
        'Status always carries an icon and a text label; colour never carries meaning alone.',
        'Tabular figures in every money and quantity column, in both scripts.',
        'Right-to-left is a layout mode, not a cosmetic flip.'
    )
    rules = @(
        [ordered]@{ name = 'The Semantic-Only Rule';     body = 'A view may never contain a literal colour. Every colour in the product is a semantic role that the theme file fills in.'; section = 'colors' },
        [ordered]@{ name = 'The One Accent Rule';        body = 'Petrol covers no more than roughly a tenth of any screen. Its rarity is the point.'; section = 'colors' },
        [ordered]@{ name = 'The State Carries a Word Rule'; body = 'No state is communicated by colour alone. Every badge pairs its colour with an icon and a text label.'; section = 'colors' },
        [ordered]@{ name = 'The Ring Is Visible Rule';    body = 'Keyboard focus is always drawn as a 2 px petrol ring offset 3 px outside the control, never removed, never the only difference between focus and hover.'; section = 'colors' },
        [ordered]@{ name = 'The Scale Multiplies Rule';   body = 'Typography scale is a single multiplier in the theme file applied to 12 / 14 / 16 / 20 / 28 / 34 px. No screen carries its own font size.'; section = 'typography' },
        [ordered]@{ name = 'The Arabic Leads Rule';      body = 'Arabic sets with roughly 1.6x the Latin leading, and always with TextOptions.TextFormattingMode=Display - Ideal breaks the ligatures.'; section = 'typography' },
        [ordered]@{ name = 'The Figure Alignment Rule';   body = 'Any column of money, quantity, or percentage uses tabular figures and right alignment.'; section = 'typography' },
        [ordered]@{ name = 'The Flat-By-Default Rule';   body = 'Surfaces are flat at rest. Separation comes from tone and a hairline rule, never from a drop shadow.'; section = 'elevation' },
        [ordered]@{ name = 'The Border Means Pressable Rule'; body = 'A control with a visible rule-strong border is interactive; a container with a rule-subtle border is not. The two are never mixed.'; section = 'elevation' }
    )
    dos = @(
        'Do reference a semantic role for every colour, in every view.',
        'Do pair every status colour with an icon and a word.',
        'Do use the 8 px spacing rhythm and the density tokens rather than ad-hoc margins.',
        'Do set tabular figures and right alignment on every money and quantity column.',
        'Do render Arabic with TextOptions.TextFormattingMode=Display and mirror the shell through FlowDirection.',
        'Do keep the primary accent to one action per screen.',
        'Do draw the focus ring on every focusable control, including custom ones.',
        'Do add a new semantic role to the theme schema before using a new colour anywhere.'
    )
    donts = @(
        "Don't put a literal colour in a view, a control template, or a converter.",
        "Don't introduce a shadow, a gradient, a glass effect, or a glow.",
        "Don't communicate state with colour alone, and don't rely on hover as the only affordance.",
        "Don't mirror a logo or a non-directional icon under right-to-left.",
        "Don't put a SKU, barcode, phone number, or email inside an Arabic sentence without isolating it.",
        "Don't hard-code a font size in a view - change the theme scale instead.",
        "Don't show a raw exception string; state the condition and the next action in the user's language.",
        "Don't round money for display twice - format from minor units once."
    )
}

$sidecar = [ordered]@{
    schemaVersion = 2
    generatedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    title         = 'Design System: StoreFlow'
    extensions    = [ordered]@{
        colorMeta      = $colorMeta
        typographyMeta = $typographyMeta
        shadows        = @()
        motion         = @(
            [ordered]@{ name = 'state-fade'; value = 'opacity 120ms ease-out'; purpose = 'The only transition in the system. Hover and pressed states change opacity; nothing scales, lifts, or tweens colour.' }
        )
        breakpoints    = @()
    }
    components     = $components
    narrative      = $narrative
}

$dir = 'D:\Coding\UA Retail Store\.impeccable'
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$json = $sidecar | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText((Join-Path $dir 'design.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("wrote design.json bytes=" + $json.Length)
