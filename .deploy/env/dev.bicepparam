// Dev environment parameters. No secrets here: mongoAdminPassword comes from the pipeline secret store.
using '../infra/main.bicep'

param environmentName = 'dev'
param apiClientId = '<onespace-api-app-registration-client-id>'
param mongoAdminPassword = readEnvironmentVariable('ONESPACE_MONGO_ADMIN_PASSWORD')
param appServiceSku = 'B1'
