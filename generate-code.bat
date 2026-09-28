@echo off
chcp 65001 >nul
powershell -NoProfile -Command "$alphabet='ABCDEFGHJKMNPQRSTUVWXYZ23456789';$salt='HDREZKA-DONOR-v1';$payload=-join(1..8|ForEach-Object{$alphabet[(Get-Random -Maximum $alphabet.Length)]});$sha=[Security.Cryptography.SHA256]::Create();$hash=$sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($salt+$payload));$check=-join(0..3|ForEach-Object{$alphabet[$hash[$_]%%$alphabet.Length]});$code=$payload+$check;$pretty=$code -replace '(.{4})(.{4})(.{4})','$1-$2-$3';Set-Clipboard $pretty;$pretty"
echo.
echo Code copied to clipboard. Send it to the donor.
pause
