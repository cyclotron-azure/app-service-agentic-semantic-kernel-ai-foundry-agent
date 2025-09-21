using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Azure.Identity;
using CRUDTasksWithAgent.Plugins;

namespace CRUDTasksWithAgent.Services
{
    // This provider exists so that the Semantic Kernel agent could be injected directly in Program.cs,
    // but we want to expose IsConfigured so Blazor components can check if the required environment variables are set.
    // This enables showing a friendly message in the UI if the agent is not configured.

    public interface ISemanticKernelAgentProvider
    {
        bool IsConfigured { get; }
        ChatCompletionAgent? Agent { get; }
    }

    public class SemanticKernelAgentProvider : ISemanticKernelAgentProvider
    {
        public bool IsConfigured { get; }
        public ChatCompletionAgent? Agent { get; }

        public SemanticKernelAgentProvider(IConfiguration config, IServiceProvider sp)
        {
            IsConfigured = false;
            Agent = null;

            // Initialize a semantic kernel
            var deployment = config["ModelDeployment"];
            var endpoint = config["AzureOpenAIEndpoint"];
            if (string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(endpoint))
            {
                return;
            }
            var kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    deploymentName: deployment,
                    endpoint: endpoint,
                    new DefaultAzureCredential()
                ).Build();
            // Add the tasks CRUD plugin
            kernel.Plugins.AddFromType<TaskCrudPlugin>(serviceProvider: sp);

            // Create a chat completion agent
            Agent = new ChatCompletionAgent
            {
                Name = "TaskManagerAgent",
                Instructions =
                    @"""
                    Your are an agent that manages tasks using CRUD operations. 
                    Use the TaskCrudPlugin functions to create, read, update, and delete tasks. 
                    Always call the appropriate plugin function for any task management request.
                    Don't try to handle any requests that are not related to task management.
                    When handling requests, if you're missing any information, don't make it up but prompt the user for it instead.
                    """,
                Kernel = kernel,
                Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
            };

            IsConfigured = true;
        }
    }
}
