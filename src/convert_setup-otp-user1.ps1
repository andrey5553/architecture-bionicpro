# convert_setup-otp-user1.ps1 (исправленная версия)
$file = "\\keycloak\\setup-otp-user1.sh"

# Читаем как бинарные данные
$content = [System.IO.File]::ReadAllBytes($file)

# Заменяем CRLF (13,10) на LF (10)
$newContent = @()
$i = 0
while ($i -lt $content.Length) {
    if ($i + 1 -lt $content.Length -and $content[$i] -eq 13 -and $content[$i+1] -eq 10) {
        $newContent += 10  # только LF
        $i += 2
    } else {
        $newContent += $content[$i]
        $i += 1
    }
}

# Сохраняем
[System.IO.File]::WriteAllBytes($file, $newContent)

Write-Host "File converted to LF format" -ForegroundColor Green
