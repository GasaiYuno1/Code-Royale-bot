#!/usr/bin/env bash
# Склейка + проверка, что dist/codingame.cs собирается как единственный файл проекта (как на CodinGame).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
python3 "$ROOT/tools/bundle.py"
TMP=$ROOT/build/bundle-check
rm -rf "$TMP" && mkdir -p "$TMP"
cp "$ROOT/dist/codingame.cs" "$TMP/Player.cs"
cp "$ROOT/nuget.config" "$TMP/"
cat > "$TMP/BundleCheck.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>10</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
CSPROJ
dotnet build "$TMP/BundleCheck.csproj" -c Release -nologo -v q 2>&1 | grep -E "error|Build succeeded|Error\(s\)" || true
