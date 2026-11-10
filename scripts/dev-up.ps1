<#
.SYNOPSIS
  Starts a local MongoDB (port 27018, so it does not clash with other local Mongo instances) for OneSpace development.
  Then run the API and UI in two terminals:
    dotnet run --project api/src/OneSpace.Api          # http://localhost:5256 (Nitro at /graphql)
    cd ui; npm start                                   # http://localhost:4200
#>
$ErrorActionPreference = 'Stop'
$name = 'onespace-mongo'

if (-not (docker ps -a --format '{{.Names}}' | Select-String -SimpleMatch $name)) {
    docker run -d --name $name -p 27018:27017 mongo:7.0 | Out-Null
} else {
    docker start $name | Out-Null
}

Write-Host "MongoDB running on mongodb://localhost:27018"
Write-Host "Set it for the API with:  `$env:Database__ConnectionString='mongodb://localhost:27018'"
