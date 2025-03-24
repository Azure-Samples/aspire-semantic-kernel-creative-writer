targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention, the name of the resource group for your application will use this name, prefixed with rg-')
param environmentName string

@minLength(1)
@description('The location used for all deployed resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''


var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}
module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
  }
}

module aoiabing 'aoiabing/openAi_bingSearch.module.bicep' = {
  name: 'aoiabing'
  scope: rg
  params: {
    keyVaultName: resources.outputs.SERVICE_BINDING_KV7ADE0065_NAME
    location: location
    principalId: resources.outputs.MANAGED_IDENTITY_PRINCIPAL_ID
    principalType: 'ServicePrincipal'
  }
}
module vectorSearch 'vectorSearch/vectorSearch.module.bicep' = {
  name: 'vectorSearch'
  scope: rg
  params: {
    location: location
    principalId: resources.outputs.MANAGED_IDENTITY_PRINCIPAL_ID
    principalType: 'ServicePrincipal'
  }
}

output MANAGED_IDENTITY_CLIENT_ID string = resources.outputs.MANAGED_IDENTITY_CLIENT_ID
output MANAGED_IDENTITY_NAME string = resources.outputs.MANAGED_IDENTITY_NAME
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = resources.outputs.AZURE_LOG_ANALYTICS_WORKSPACE_NAME
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = resources.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
output AZURE_CONTAINER_REGISTRY_NAME string = resources.outputs.AZURE_CONTAINER_REGISTRY_NAME
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_NAME
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID
output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output SERVICE_BINDING_KV7ADE0065_ENDPOINT string = resources.outputs.SERVICE_BINDING_KV7ADE0065_ENDPOINT
output SERVICE_BINDING_KV7ADE0065_NAME string = resources.outputs.SERVICE_BINDING_KV7ADE0065_NAME
output AOIABING_AIPROJECTCONNECTIONSTRING string = aoiabing.outputs.aiProjectConnectionString
output AOIABING_CONNECTIONSTRING string = aoiabing.outputs.connectionString
output AOIABING_MODELDEPLOYMENT string = aoiabing.outputs.modelDeployment
output VECTORSEARCH_CONNECTIONSTRING string = vectorSearch.outputs.connectionString
