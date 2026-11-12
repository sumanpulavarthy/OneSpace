<#
.SYNOPSIS
  Creates (or updates) the OneSpace Entra ID app registrations for local development and wires them up:
    - "OneSpace API": access_as_user scope, v2 tokens, delegated Graph permissions, client secret
    - "OneSpace UI":  SPA redirect URIs, pre-authorized for the API scope
  Then grants tenant-wide admin consent (if your account is allowed to), stores the API values in
  dotnet user-secrets, and writes the public UI values to ui/src/assets/config/app-config.json.

  Safe to re-run: existing apps are reused. A new client secret is created only when none is stored
  locally, or with -RotateSecret.

.EXAMPLE
  ./scripts/setup-entra.ps1
  ./scripts/setup-entra.ps1 -TenantId <tenant-guid> -RotateSecret
#>
[CmdletBinding()]
param(
    [string]$TenantId,
    [string]$ApiName = 'OneSpace API',
    [string]$UiName = 'OneSpace UI',
    [string]$UiOrigin = 'http://localhost:4200',
    [switch]$RotateSecret
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repo 'api/src/OneSpace.Api'
$uiConfig = Join-Path $repo 'ui/src/assets/config/app-config.json'

$graphAppId = '00000003-0000-0000-c000-000000000000'
$graphScopes = 'User.Read', 'Calendars.Read', 'Mail.Read', 'Files.Read.All', 'Team.ReadBasic.All', 'Chat.Read', 'Tasks.Read', 'Presence.Read'

# --- Modules ---------------------------------------------------------------------------------------
$modules = 'Microsoft.Graph.Authentication', 'Microsoft.Graph.Applications', 'Microsoft.Graph.Identity.SignIns'
foreach ($m in $modules) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Write-Host "Installing $m (CurrentUser)..."
        Install-Module $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m
}

# --- Sign in ---------------------------------------------------------------------------------------
# Windows' built-in sign-in (WAM) silently picks the account signed in to Windows, which is often a
# personal Microsoft account. Use the browser instead and forget any cached sign-in.
Set-MgGraphOption -DisableLoginByWAM $true
Disconnect-MgGraph -ErrorAction SilentlyContinue | Out-Null

$connect = @{ Scopes = 'Application.ReadWrite.All', 'DelegatedPermissionGrant.ReadWrite.All'; NoWelcome = $true; ContextScope = 'Process' }
if ($TenantId) { $connect.TenantId = $TenantId }
Write-Host 'A browser window will open: sign in with your WORK account (not a personal Microsoft account).'
Connect-MgGraph @connect
$context = Get-MgContext
if (-not $context.TenantId -or -not $context.Account) {
    throw 'Signed in with a personal Microsoft account. App registrations need a work/school (Entra ID) account. Re-run and choose your work account, or pass -TenantId <your-company-domain>.'
}
$TenantId = $context.TenantId
Write-Host "Signed in to tenant $TenantId as $($context.Account)"

function Get-AppByName([string]$name) {
    @(Get-MgApplication -Filter "displayName eq '$($name.Replace("'", "''"))'" -All)[0]
}

function Get-OrCreateServicePrincipal([string]$appId) {
    $sp = Get-MgServicePrincipal -Filter "appId eq '$appId'"
    if (-not $sp) { $sp = New-MgServicePrincipal -AppId $appId }
    $sp
}

function Grant-AdminConsent($clientSp, $resourceSp, [string]$scope) {
    $existing = Get-MgOauth2PermissionGrant -Filter "clientId eq '$($clientSp.Id)' and resourceId eq '$($resourceSp.Id)' and consentType eq 'AllPrincipals'"
    if ($existing) {
        Update-MgOauth2PermissionGrant -OAuth2PermissionGrantId $existing.Id -Scope $scope
    } else {
        New-MgOauth2PermissionGrant -BodyParameter @{
            clientId = $clientSp.Id; consentType = 'AllPrincipals'; resourceId = $resourceSp.Id; scope = $scope
        } | Out-Null
    }
}

# --- Graph permission ids --------------------------------------------------------------------------
$graphSp = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"
$graphAccess = foreach ($s in $graphScopes) {
    $p = $graphSp.Oauth2PermissionScopes | Where-Object Value -eq $s
    if (-not $p) { throw "Graph delegated permission '$s' not found." }
    @{ Id = $p.Id; Type = 'Scope' }
}

# --- API app ---------------------------------------------------------------------------------------
$api = Get-AppByName $ApiName
if (-not $api) {
    Write-Host "Creating app registration '$ApiName'..."
    $api = New-MgApplication -DisplayName $ApiName -SignInAudience 'AzureADMyOrg'
} else {
    Write-Host "Reusing app registration '$ApiName' ($($api.AppId))"
}

$scope = $api.Api.Oauth2PermissionScopes | Where-Object Value -eq 'access_as_user'
$scopeId = if ($scope) { $scope.Id } else { [guid]::NewGuid().ToString() }
$scopes = @($api.Api.Oauth2PermissionScopes | Where-Object Value -ne 'access_as_user' | ForEach-Object {
    @{ Id = $_.Id; Value = $_.Value; Type = $_.Type; IsEnabled = $_.IsEnabled
       AdminConsentDisplayName = $_.AdminConsentDisplayName; AdminConsentDescription = $_.AdminConsentDescription
       UserConsentDisplayName = $_.UserConsentDisplayName; UserConsentDescription = $_.UserConsentDescription }
})
$scopes += @{
    Id = $scopeId; Value = 'access_as_user'; Type = 'User'; IsEnabled = $true
    AdminConsentDisplayName = 'Access OneSpace as the signed-in user'
    AdminConsentDescription = 'Lets the OneSpace UI call the OneSpace API on behalf of the signed-in employee.'
    UserConsentDisplayName = 'Access OneSpace as you'
    UserConsentDescription = 'Lets the OneSpace UI call the OneSpace API on your behalf.'
}

Update-MgApplication -ApplicationId $api.Id `
    -IdentifierUris @("api://$($api.AppId)") `
    -Api @{ RequestedAccessTokenVersion = 2; Oauth2PermissionScopes = $scopes } `
    -RequiredResourceAccess @(@{ ResourceAppId = $graphAppId; ResourceAccess = @($graphAccess) })
$apiSp = Get-OrCreateServicePrincipal $api.AppId

# --- UI app ----------------------------------------------------------------------------------------
$redirects = @($UiOrigin, "$UiOrigin/assistant", "$UiOrigin/login")
$uiAccess = @(
    @{ ResourceAppId = $api.AppId; ResourceAccess = @(@{ Id = $scopeId; Type = 'Scope' }) },
    @{ ResourceAppId = $graphAppId; ResourceAccess = @($graphAccess | Where-Object { $_.Id -eq ($graphSp.Oauth2PermissionScopes | Where-Object Value -eq 'User.Read').Id }) }
)
$ui = Get-AppByName $UiName
if (-not $ui) {
    Write-Host "Creating app registration '$UiName'..."
    $ui = New-MgApplication -DisplayName $UiName -SignInAudience 'AzureADMyOrg' -Spa @{ RedirectUris = $redirects } -RequiredResourceAccess $uiAccess
} else {
    Write-Host "Reusing app registration '$UiName' ($($ui.AppId))"
    $merged = @($ui.Spa.RedirectUris) + $redirects | Where-Object { $_ } | Select-Object -Unique
    Update-MgApplication -ApplicationId $ui.Id -Spa @{ RedirectUris = @($merged) } -RequiredResourceAccess $uiAccess
}
$uiSp = Get-OrCreateServicePrincipal $ui.AppId

# Pre-authorize the UI so employees never see a consent prompt for the API scope.
Update-MgApplication -ApplicationId $api.Id -Api @{
    RequestedAccessTokenVersion = 2
    Oauth2PermissionScopes = $scopes
    PreAuthorizedApplications = @(@{ AppId = $ui.AppId; DelegatedPermissionIds = @($scopeId) })
}

# --- Admin consent ---------------------------------------------------------------------------------
try {
    Grant-AdminConsent $apiSp $graphSp ($graphScopes -join ' ')
    Grant-AdminConsent $uiSp $apiSp 'access_as_user'
    Grant-AdminConsent $uiSp $graphSp 'User.Read'
    Write-Host 'Admin consent granted.' -ForegroundColor Green
} catch {
    Write-Warning "Could not grant admin consent ($($_.Exception.Message))."
    Write-Warning "Ask an Entra admin to open this link and click 'Grant admin consent':"
    Write-Warning "  https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$($api.AppId)"
}

# --- Local configuration ---------------------------------------------------------------------------
$existingSecrets = dotnet user-secrets list --project $apiProject 2>$null
$hasSecret = $existingSecrets -match '^Integrations:Microsoft365:ClientSecret = '

function Set-UserSecret([string]$key, [string]$value) {
    dotnet user-secrets set $key $value --project $apiProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet user-secrets set $key failed" }
}

Set-UserSecret 'AzureAd:TenantId' $TenantId
Set-UserSecret 'AzureAd:ClientId' $api.AppId
Set-UserSecret 'Integrations:Microsoft365:TenantId' $TenantId
Set-UserSecret 'Integrations:Microsoft365:ClientId' $api.AppId

if ($RotateSecret -or -not $hasSecret) {
    $secret = Add-MgApplicationPassword -ApplicationId $api.Id -PasswordCredential @{
        DisplayName = "OneSpace local dev ($env:COMPUTERNAME)"; EndDateTime = (Get-Date).AddMonths(6)
    }
    Set-UserSecret 'Integrations:Microsoft365:ClientSecret' $secret.SecretText
    Write-Host "New client secret stored in user-secrets (expires $($secret.EndDateTime.ToString('yyyy-MM-dd')))."
} else {
    Write-Host 'Client secret already in user-secrets; kept it (use -RotateSecret for a new one).'
}

$config = if (Test-Path $uiConfig) { Get-Content $uiConfig -Raw | ConvertFrom-Json } else { [pscustomobject]@{ apiBaseUrl = '' } }
$config | Add-Member -NotePropertyName azureAd -NotePropertyValue ([ordered]@{
    clientId = $ui.AppId; tenantId = $TenantId; apiScopes = @("api://$($api.AppId)/access_as_user")
}) -Force
[IO.File]::WriteAllText($uiConfig, ($config | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host "  Tenant:      $TenantId"
Write-Host "  API app id:  $($api.AppId)"
Write-Host "  UI app id:   $($ui.AppId)"
Write-Host "  UI config:   $uiConfig"
Write-Host 'Restart the API and the UI, then sign in at the UI.'
