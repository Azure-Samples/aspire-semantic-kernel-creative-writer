@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param principalId string

param principalType string

param bingGroundingKey string

param bingGroundingResourceId string

param aiProjectName string = take('aiProject-${uniqueString(resourceGroup().id)}', 64)

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: take('openAi-${uniqueString(resourceGroup().id)}', 64)
  location: location
  kind: 'OpenAI'
  properties: {
    customSubDomainName: toLower(take(concat('openAi', uniqueString(resourceGroup().id)), 24))
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
  sku: {
    name: 'S0'
  }
  tags: {
    'aspire-resource-name': 'openAi'
  }
}

resource openAi_CognitiveServicesOpenAIContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, principalId, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a001fd3d-188f-4b5d-821b-7da978bf7442'))
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a001fd3d-188f-4b5d-821b-7da978bf7442')
    principalType: principalType
  }
  scope: openAi
}

resource chatdeploymentnew 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  name: 'chatdeploymentnew'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-05-13'
    }
  }
  sku: {
    name: 'Standard'
    capacity: 10
  }
  parent: openAi
}

resource text_embedding_3_large 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  name: 'text-embedding-3-large'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
  sku: {
    name: 'Standard'
    capacity: 8
  }
  parent: openAi
  dependsOn: [
    chatdeploymentnew
  ]
}

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: take('aiHub-${uniqueString(resourceGroup().id)}', 64)
  location: location
  kind: 'Hub'
  properties: {
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    'aspire-resource-name': 'aiHub'
  }
  identity: {
    type: 'SystemAssigned'
  }

  resource bingConnection 'connections@2024-10-01' = {
    name: 'bingGrounding'
    properties: {
      category: 'ApiKey'
      credentials: {
        key: bingGroundingKey
      }
      isSharedToAll: true
      metadata: {
        type: 'bing_grounding'
        ApiType: 'Azure'
        ResourceId: bingGroundingResourceId
      }
      target: 'https://api.bing.microsoft.com/'
      authType: 'ApiKey'
    }
  }

  resource aiServicesConnection 'connections@2024-01-01-preview' = {
    name: 'AzureOpenAI'
    properties: {
      category: 'AzureOpenAI'
      target: openAi.properties.endpoint
      authType: 'ApiKey'
      isSharedToAll: true
      credentials: {
        key: '${listKeys(openAi.id, '2021-10-01').key1}'
      }
      metadata: {
        ApiType: 'Azure'
        ResourceId: openAi.id
      }
    }
  }
}

//for constructing project connection string
var subscriptionId = subscription().subscriptionId
var resourceGroupName = resourceGroup().name
var projectConnectionString = '${location}.api.azureml.ms;${subscriptionId};${resourceGroupName};${aiProjectName}'

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2023-08-01-preview' = {
  name: aiProjectName
  location: location
  tags: {
    ProjectConnectionString: projectConnectionString
    'aspire-resource-name': 'aiProject'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // dependent resources
    hubResourceId: aiHub.id 
  }
  kind: 'project'
}

output connectionString string = 'Endpoint=${openAi.properties.endpoint}'
output aiProjectConnectionString string = projectConnectionString
