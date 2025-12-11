# Migration Plan: Semantic Kernel to Microsoft Agent Framework

## Overview

This document outlines the migration from Semantic Kernel Agents to Microsoft Agent Framework for the Creative Writer application. Microsoft Agent Framework is the next generation combining Semantic Kernel and AutoGen, created by the same teams.

## Current State Analysis

### Current Dependencies
The application currently uses:
- `Microsoft.SemanticKernel` (1.42.0)
- `Microsoft.SemanticKernel.Agents.Core` (1.42.0-preview)
- `Microsoft.SemanticKernel.Agents.AzureAI` (1.42.0-preview)
- `Microsoft.SemanticKernel.Agents.OpenAI` (1.42.0-preview)
- `Microsoft.SemanticKernel.Connectors.AzureAISearch` (1.42.0-preview)
- `Microsoft.SemanticKernel.Plugins.Web` (1.42.0-alpha)
- `Microsoft.SemanticKernel.Yaml` (1.42.0)

### Current Agent Architecture
The application has 4 agents in a multi-agent orchestration:
1. **Researcher Agent** (AzureAIAgent) - Uses Bing Search for grounding
2. **Marketing Agent** (ChatCompletionAgent) - Uses vector search for product knowledge
3. **Writer Agent** (ChatCompletionAgent) - Creates content based on research
4. **Editor Agent** (ChatCompletionAgent) - Reviews and provides feedback

### Current Patterns
- Uses `Kernel` object for dependency injection and service management
- Uses `AgentGroupChat` with `SequentialSelectionStrategy` for orchestration
- Uses `AzureAIAgent` for Azure AI Agent Service integration
- Uses `ChatCompletionAgent` for local LLM-based agents
- Custom `TerminationStrategy` for workflow completion

## Target State

### Target Dependencies
Replace with:
- `Microsoft.Agents.AI` (latest)
- `Microsoft.Extensions.AI` (latest)
- `Microsoft.Extensions.AI.OpenAI` (latest)

### Key Differences

| Semantic Kernel Agents | Microsoft Agent Framework |
|------------------------|---------------------------|
| `Kernel` object | `IChatClient` with DI |
| `ChatCompletionAgent` | `ChatClientAgent` |
| `AzureAIAgent` | `ChatClientAgent` with Azure AI |
| `AgentGroupChat` | Workflow with executors/edges |
| `PromptTemplateConfig` | Direct instructions string |
| `KernelArguments` | `ChatOptions` |
| `ChatMessageContent` | `ChatMessage` |
| `TerminationStrategy` | Workflow conditional edges |

## Migration Steps

### Step 1: Update Package References

**File:** `src/ChatApp.WebApi/ChatApp.WebApi.csproj`

Remove:
```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.42.0" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.42.0-preview" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.42.0-preview" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.42.0-preview" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.AzureAISearch" Version="1.42.0-preview" />
<PackageReference Include="Microsoft.SemanticKernel.Plugins.Web" Version="1.42.0-alpha" />
<PackageReference Include="Microsoft.SemanticKernel.Yaml" Version="1.42.0" />
```

Add:
```xml
<PackageReference Include="Microsoft.Agents.AI" />
<PackageReference Include="Microsoft.Extensions.AI" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
<PackageReference Include="Azure.Search.Documents" />
```

### Step 2: Update Program.cs

Replace Kernel registration with IChatClient:

**Before:**
```csharp
builder.Services.AddKernel()
    .AddAzureOpenAIChatCompletion(builder.Configuration["AzureDeployment"]!)
    .AddAzureAISearchVectorStore()
    .AddAzureOpenAITextEmbeddingGeneration(builder.Configuration["EmbeddingModelDeployment"]!)
    .ConfigureOpenTelemetry(builder.Configuration);
```

**After:**
```csharp
// Register IChatClient
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var openAiClient = sp.GetRequiredService<Azure.AI.OpenAI.AzureOpenAIClient>();
    return openAiClient.GetChatClient(builder.Configuration["AzureDeployment"]!)
        .AsIChatClient();
});

// Keep Azure Search and embedding services separate
builder.AddAzureSearchClient("vectorSearch", configureSettings: settings =>
{
    settings.Credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeVisualStudioCredential = true });
});
```

### Step 3: Update CreativeWriterApp.cs

**Key Changes:**
1. Replace `Kernel` with `IChatClient`
2. Replace `ChatCompletionAgent` with `ChatClientAgent`
3. Replace `AgentGroupChat` with Workflow
4. Remove YAML template loading (use direct instructions)
5. Update agent creation patterns

**Before Pattern:**
```csharp
ChatCompletionAgent writerAgent = new(
    ReadFileForPromptTemplateConfig("./Agents/Prompts/writer.yaml"), 
    templateFactory: new KernelPromptTemplateFactory())
{
    Name = WriterName,
    Kernel = defaultKernel,
    Arguments = [],
    LoggerFactory = defaultKernel.LoggerFactory
};
```

**After Pattern:**
```csharp
var writerAgent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions(
        instructions: "You are a professional writer..."
    )
);
```

### Step 4: Update CreativeWriterSession.cs

Replace sequential agent invocation with Workflow:

**Before:**
```csharp
AgentGroupChat chat = new(writerAgent, editorAgent)
{
    LoggerFactory = kernel.LoggerFactory,
    ExecutionSettings = new AgentGroupChatSettings
    {
        SelectionStrategy = new SequentialSelectionStrategy() { InitialAgent = writerAgent },
        TerminationStrategy = new NoFeedbackLeftTerminationStrategy()
    }
};
```

**After:**
```csharp
var workflow = new WorkflowBuilder(writerExecutor)
    .AddEdge(writerExecutor, editorExecutor)
    .AddEdge(editorExecutor, writerExecutor, condition: ctx => !IsCompleted(ctx))
    .WithOutputFrom(editorExecutor)
    .Build();
```

### Step 5: Update Vector Search Integration

Migrate from Semantic Kernel vector search to direct Azure AI Search integration using Microsoft.Extensions.AI patterns.

### Step 6: Update Evaluation Tests

The evaluation tests already use `Microsoft.Extensions.AI`, but need updates to work with the new agent implementation.

### Step 7: Update Notebooks

Update experiment notebooks to use the new Agent Framework packages and patterns.

## Breaking Changes

1. **No more Kernel object** - Use `IChatClient` instead
2. **Agent construction** - Simpler with `ChatClientAgent`
3. **Orchestration** - Workflows replace `AgentGroupChat`
4. **Termination logic** - Built into workflow edges
5. **YAML templates** - Direct instructions instead

## Benefits of Migration

1. **Unified Framework** - Single framework combining SK and AutoGen
2. **Better Type Safety** - Strongly typed workflows
3. **Improved Orchestration** - Graph-based workflows with explicit control
4. **Future-Proof** - Microsoft's recommended path forward
5. **Simplified API** - Fewer abstractions, clearer patterns

## Testing Strategy

1. Run evaluation tests after each step
2. Verify agent responses match expected patterns
3. Test vector search integration
4. Validate streaming responses
5. Performance comparison

## Rollback Plan

Keep the current code in a separate branch until migration is complete and validated.
