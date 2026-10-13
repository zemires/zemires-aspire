extension microsoftGraphV1

@description('Specifies the name of cloud environment to run this deployment in.')
param cloudEnvironment string = environment().name

// NOTE: Microsoft Graph Bicep file deployment is only supported in Public Cloud
@description('Audience uris for public and national clouds')
param audiences object = {
  AzureCloud: {
    uri: 'api://AzureADTokenExchange'
  }
  AzureUSGovernment: {
    uri: 'api://AzureADTokenExchangeUSGov'
  }
  USNat: {
    uri: 'api://AzureADTokenExchangeUSNat'
  }
  USSec: {
    uri: 'api://AzureADTokenExchangeUSSec'
  }
  AzureChinaCloud: {
    uri: 'api://AzureADTokenExchangeChina'
  }
}

@description('Specifies the ID of the user-assigned managed identity.')
param webAppIdentityId string = ''

@description('Specifies the unique name for the client application.')
param clientAppName string

@description('Specifies the display name for the client application')
param clientAppDisplayName string

@description('Specifies the scopes that the client application requires.')
param clientAppScopes array = ['offline_access', 'openid', 'profile', 'email']

param serviceManagementReference string = ''

param issuer string = ''

param webAppEndpoint string

param location string = ''

param now string = utcNow()

// Get the MS Graph Service Principal based on its application ID:
// https://learn.microsoft.com/troubleshoot/entra/entra-id/governance/verify-first-party-apps-sign-in
var msGraphAppId = '00000003-0000-0000-c000-000000000000'
resource msGraphSP 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: msGraphAppId
}

var graphScopes = msGraphSP.oauth2PermissionScopes
resource clientApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: clientAppName
  displayName: clientAppDisplayName
  signInAudience: 'AzureADMyOrg'
  serviceManagementReference: empty(serviceManagementReference) ? null : serviceManagementReference
  web: {
    redirectUris: [
      'https://localhost:5678/auth/oidc/callback'
      '${webAppEndpoint}/auth/oidc/callback'
    ]
    implicitGrantSettings: { enableIdTokenIssuance: true }
  }
  requiredResourceAccess: [
    {
      resourceAppId: msGraphAppId
      resourceAccess: [
        for (scope, i) in clientAppScopes: {
          id: filter(graphScopes, graphScopes => graphScopes.value == scope)[0].id
          type: 'Scope'
        }
      ]
    }
  ]
  // passwordCredentials: [
  //   {
  //     displayName: 'Client App Secret'
  //     endDateTime: endDateTime
  //   }
  // ]

  optionalClaims: {
    idToken: [
    {
        name: 'email'
        essential: false
        source: null
    }
    ]
  }

  resource clientAppFic 'federatedIdentityCredentials@v1.0' = if (!empty(webAppIdentityId)) {
    name: '${clientApp.uniqueName}/miAsFic'
    audiences: [
      audiences[cloudEnvironment].uri
   ]
    issuer: issuer
    subject: webAppIdentityId
  }
}

resource clientSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: clientApp.appId
}

output clientAppId string = clientApp.appId
output clientSpId string = clientSp.id
