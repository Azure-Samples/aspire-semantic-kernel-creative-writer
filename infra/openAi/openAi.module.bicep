@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param principalId string

param principalType string

param bingGroundingKey string

param bingGroundingResourceId string

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

resource aiFoundry 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: take('aiFoundry-${uniqueString(resourceGroup().id)}', 64)
  location: location
  kind: 'AIServices'
  properties: {
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
  sku: {
    name: 'S0'
  }
  tags: {
    'aspire-resource-name': 'aiFoundry'
  }

  resource bingConnection 'connections' = {
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
}

// resource aiFoundryAsWorkspace 'Microsoft.MachineLearning/workspaces@2019-10-01' existing = {
//   dependsOn: [
//     aiFoundry
//   ]
//   name: resourceId('Microsoft.MachineLearningServices/workspaces', aiFoundry.name)
// }

// resource bingConnection 'Microsoft.MachineLearningServices/workspaces/connections@2025-01-01-preview' = {
//   parent: aiFoundryAsWorkspace
//   name: 'bingGrounding'
//   properties: {
//     category: 'ApiKey'
//     credentials: {
//       key: bingGroundingKey
//     }
//     isSharedToAll: true
//     metadata: {
//       type: 'bing_grounding'
//       ApiType: 'Azure'
//       ResourceId: bingGroundingResourceId
//     }
//     target: 'https://api.bing.microsoft.com/'
//     authType: 'ApiKey'
//   }
// }

output connectionString string = 'Endpoint=${openAi.properties.endpoint}'
output aiFoundryConnectionString string = 'Endpoint=${aiFoundry.properties.endpoint}'
