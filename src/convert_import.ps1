# Сконвертировать файл из CRLF в LF
$content = Get-Content .\keycloak\import.sh -Raw
$content = $content -replace "`r`n", "`n"
Set-Content .\keycloak\import.sh -Value $content -NoNewline

# Добавить пустую строку в конце
Add-Content .\keycloak\import.sh -Value "`n"
