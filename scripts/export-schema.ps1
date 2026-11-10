<#
.SYNOPSIS
  Regenerates specs/graphql/schema.graphql from the API. Run after an API change has been agreed in the spec
  review, then commit the spec with the code. The contract test fails if the two differ.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project "$root/api/src/OneSpace.Api" -- schema export --output "$root/specs/graphql/schema.graphql"
Remove-Item "$root/specs/graphql/schema-settings.json" -ErrorAction SilentlyContinue
Write-Host "Updated specs/graphql/schema.graphql. Run 'npm run codegen' in ui/ to refresh UI types."
