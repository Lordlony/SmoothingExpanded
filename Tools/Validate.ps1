param(
    [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$errors = [System.Collections.Generic.List[string]]::new()

$xmlFiles = Get-ChildItem -LiteralPath $ModRoot -Recurse -Filter '*.xml' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $xmlFiles) {
    try {
        [xml](Get-Content -LiteralPath $file.FullName -Raw) | Out-Null
    }
    catch {
        $errors.Add("Invalid XML: $($file.FullName): $($_.Exception.Message)")
    }
}

function Get-Placeholders([string]$text) {
    if ($null -eq $text) { return @() }
    return @([regex]::Matches($text, '\{\d+\}') |
        ForEach-Object Value | Sort-Object -Unique)
}

function Test-LanguageTree([string]$languagesRoot) {
    $englishRoot = Join-Path $languagesRoot 'English'
    if (-not (Test-Path -LiteralPath $englishRoot)) { return }

    $languageFolders = Get-ChildItem -LiteralPath $languagesRoot -Directory |
        Where-Object Name -ne 'English'
    $englishFiles = Get-ChildItem -LiteralPath $englishRoot -Recurse -Filter '*.xml' -File
    foreach ($englishFile in $englishFiles) {
        $relative = $englishFile.FullName.Substring($englishRoot.Length).TrimStart('\')
        [xml]$englishXml = Get-Content -LiteralPath $englishFile.FullName -Raw
        $englishElements = @($englishXml.LanguageData.ChildNodes |
            Where-Object NodeType -eq Element)
        $expectedKeys = @($englishElements | ForEach-Object Name)

        foreach ($language in $languageFolders) {
            $candidate = Join-Path $language.FullName $relative
            if (-not (Test-Path -LiteralPath $candidate)) {
                $errors.Add("Missing translation file: $candidate")
                continue
            }

            [xml]$candidateXml = Get-Content -LiteralPath $candidate -Raw
            $candidateElements = @($candidateXml.LanguageData.ChildNodes |
                Where-Object NodeType -eq Element)
            $actualKeys = @($candidateElements | ForEach-Object Name)
            foreach ($key in $expectedKeys) {
                if ($key -notin $actualKeys) {
                    $errors.Add("Missing key '$key' in $candidate")
                    continue
                }

                $source = $englishElements | Where-Object Name -eq $key | Select-Object -First 1
                $translated = $candidateElements | Where-Object Name -eq $key | Select-Object -First 1
                $sourcePlaceholders = @(Get-Placeholders $source.InnerText)
                $translatedPlaceholders = @(Get-Placeholders $translated.InnerText)
                if (($sourcePlaceholders -join ',') -ne ($translatedPlaceholders -join ',')) {
                    $errors.Add("Placeholder mismatch for '$key' in $candidate")
                }
            }
            foreach ($key in $actualKeys) {
                if ($key -notin $expectedKeys) {
                    $errors.Add("Unexpected key '$key' in $candidate")
                }
            }
        }
    }
}

Test-LanguageTree (Join-Path $ModRoot 'Languages')
Test-LanguageTree (Join-Path $ModRoot 'Optional\OptionalOdyssey\Languages')

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Validated $($xmlFiles.Count) XML files, translation keys and placeholders."
