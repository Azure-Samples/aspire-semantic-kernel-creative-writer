@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param principalId string

param principalType string

param keyVaultName string

param aiProjectName string = take('aiProject-${uniqueString(resourceGroup().id)}', 64)

resource bingSearchService 'Microsoft.Bing/accounts@2020-06-10' = {
  name: 'bing-grounding-${uniqueString(resourceGroup().id)}'
  location: 'global'
  sku: {
    name: 'G1'
  }
  kind: 'Bing.Grounding'
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName

  resource secret 'secrets@2023-07-01' = {
    name: 'bingAPIKey'
    properties: {
      value: bingSearchService.listKeys().key1
    }
  }
}

output bingGroundingResourceId string = bingSearchService.id

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
  name: guid(
    openAi.id,
    principalId,
    subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a001fd3d-188f-4b5d-821b-7da978bf7442')
  )
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a001fd3d-188f-4b5d-821b-7da978bf7442'
    )
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
    capacity: 50
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
        key: bingSearchService.listKeys().key1
      }
      isSharedToAll: true
      metadata: {
        type: 'bing_grounding'
        ApiType: 'Azure'
        ResourceId: bingSearchService.id
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
      authType: 'AAD'
      isSharedToAll: true
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

// Azure AI Developer for App MSI over AI Project
resource aiProject_AzureAIDeveloper 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(
    aiProject.id,
    principalId,
    subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '64702f94-c441-49e6-a78b-ef80e0188fee')
  )
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '64702f94-c441-49e6-a78b-ef80e0188fee'
    )
    principalType: principalType
  }
  scope: aiProject
}

// Azure AI Developer for AI Project over AOAI
resource aoai_AzureAIDeveloper 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(
    openAi.id,
    subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '64702f94-c441-49e6-a78b-ef80e0188fee')
  )
  properties: {
    principalId: aiProject.identity.principalId
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '64702f94-c441-49e6-a78b-ef80e0188fee'
    )
    principalType: 'ServicePrincipal'
  }
  scope: openAi
}

output modelDeployment string = chatdeploymentnew.name
output connectionString string = openAi.properties.endpoint
output aiProjectConnectionString string = projectConnectionString
