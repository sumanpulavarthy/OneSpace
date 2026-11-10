// OneSpace Phase 0 infrastructure: one App Service (API), a Static Web App (UI), Key Vault,
// Application Insights and Azure Cosmos DB for MongoDB (vCore). Secrets live only in Key Vault.
targetScope = 'resourceGroup'

@description('Short environment name, e.g. dev, test, prod.')
param environmentName string

param location string = resourceGroup().location

@description('Entra ID tenant for token validation and on-behalf-of.')
param tenantId string = subscription().tenantId

@description('Client id of the OneSpace API app registration (public identifier).')
param apiClientId string

@description('Cosmos DB for MongoDB vCore administrator login.')
param mongoAdminLogin string = 'onespaceadmin'

@secure()
@description('Cosmos DB administrator password. Pass from the pipeline secret store; never commit.')
param mongoAdminPassword string

param appServiceSku string = 'B1'

var prefix = 'onespace-${environmentName}'
var tags = { app: 'onespace', env: environmentName }

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-ai'
  location: location
  kind: 'web'
  tags: tags
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take(replace('${prefix}-kv', '-', ''), 24)
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: true
  }
}

resource mongo 'Microsoft.DocumentDB/mongoClusters@2024-07-01' = {
  name: '${prefix}-mongo'
  location: location
  tags: tags
  properties: {
    administrator: { userName: mongoAdminLogin, password: mongoAdminPassword }
    serverVersion: '7.0'
    compute: { tier: 'M10' }
    storage: { sizeGb: 32 }
    sharding: { shardCount: 1 }
    highAvailability: { targetMode: 'Disabled' } // PRD NFR: best-effort availability in Phase 0
    publicNetworkAccess: 'Enabled'
  }
}

resource mongoFromAzure 'Microsoft.DocumentDB/mongoClusters/firewallRules@2024-07-01' = {
  parent: mongo
  name: 'allow-azure-services'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// Connection string stored as a secret; the API reads it via Key Vault configuration (Database--ConnectionString).
resource mongoSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'Database--ConnectionString'
  properties: {
    value: 'mongodb+srv://${mongoAdminLogin}:${uriComponent(mongoAdminPassword)}@${mongo.name}.global.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000'
  }
}

// Key-encryption key for ASP.NET Core Data Protection: wraps the key ring that encrypts employees'
// Zoho refresh tokens at rest. A database copy alone cannot decrypt the tokens.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [ 'wrapKey', 'unwrapKey' ]
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: { reserved: true }
}

resource ui 'Microsoft.Web/staticSites@2023-12-01' = {
  name: '${prefix}-ui'
  location: location
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {}
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'KeyVault__Uri', value: vault.properties.vaultUri }
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: apiClientId }
        { name: 'Integrations__Microsoft365__TenantId', value: tenantId }
        { name: 'Integrations__Microsoft365__ClientId', value: apiClientId }
        { name: 'Cors__AllowedOrigins__0', value: 'https://${ui.properties.defaultHostname}' }
        { name: 'DataProtection__KeyVaultKeyUri', value: dataProtectionKey.properties.keyUri }
        // Zoho OAuth: callback on the API, then back to the UI. Both fixed here (no open redirect).
        { name: 'Integrations__ZohoMcp__OAuth__RedirectUri', value: 'https://${prefix}-api.azurewebsites.net/oauth/zoho/callback' }
        { name: 'Integrations__ZohoMcp__OAuth__PostConnectRedirectUri', value: 'https://${ui.properties.defaultHostname}/assistant' }
        // Remaining secrets (Assistant--ApiKey, Integrations--ZohoMcp--Endpoint,
        // Integrations--Microsoft365--ClientSecret) are added to Key Vault out of band.
      ]
    }
  }
}

// The API's managed identity may read secrets, nothing more.
resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, api.id, 'kv-secrets-user')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Wrap/unwrap with the Data Protection key (not read secrets, not manage keys).
resource kvCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, api.id, 'kv-crypto-user')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output apiUrl string = 'https://${api.properties.defaultHostName}'
output uiUrl string = 'https://${ui.properties.defaultHostname}'
output keyVaultName string = vault.name
