using Azure.AI.Agents.Persistent;
using Azure.Identity;

namespace CRUDTasksWithAgent.Services
{
    // This provider exists so that the Foundry agent client could be injected directly in Program.cs,
    // but we want to expose IsConfigured so Blazor components can check if the required environment variables are set.
    // This enables showing a friendly message in the UI if the client is not configured.

    // To keep the scenario simple, just create a new thread for each injected provider (scoped to the 
    // browser session). You can manage the threads in the Azure AI Foundry portal, or add thread 
    // management features in your application code.

    public interface IFoundryAgentProvider
    {
        bool IsConfigured { get; } // Indicates if the provider is ready for use
        public PersistentAgent? Agent { get; } // An agent instance
        PersistentAgentsClient? Client { get; } // The agents client
        public string? ThreadId { get; } // The agent thread ID
        Task<PersistentAgent?> GetOrCreateAgentAsync(string agentName, string? instructions = null);
        Task<PersistentAgent?> GetAgentByNameAsync(string agentName);
    }

    public class FoundryAgentProvider : IFoundryAgentProvider
    {
        private readonly ILogger<FoundryAgentProvider> _logger;
        private readonly IConfiguration _config;
        
        public bool IsConfigured { get; }
        public PersistentAgentsClient? Client { get; }
        public string? ThreadId { get; }
        public PersistentAgent? Agent { get; private set; }

        public FoundryAgentProvider(IConfiguration config, ILogger<FoundryAgentProvider> logger)
        {
            _logger = logger;
            _config = config;
            IsConfigured = false;

            // Create a new client instance
            var endpoint = config["AzureAIFoundryProjectEndpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning("AzureAIFoundryProjectEndpoint not configured");
                return; // Fail gracefully
            }

            try
            {
                Client = new PersistentAgentsClient(endpoint, new DefaultAzureCredential());

                // Create a new thread. 
                PersistentAgentThread agentThread = Client.Threads.CreateThread();
                ThreadId = agentThread.Id;

                // Try to get existing agent by ID first, then fallback to creating by name
                var agentId = config["AzureAIFoundryAgentId"];
                if (!string.IsNullOrWhiteSpace(agentId))
                {
                    try
                    {
                        Agent = Client.Administration.GetAgent(agentId);
                        _logger.LogInformation("Retrieved existing agent with ID: {AgentId}", agentId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to retrieve agent by ID {AgentId}", agentId);
                    }
                }

                IsConfigured = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FoundryAgentProvider");
            }
        }

        public async Task<PersistentAgent?> GetOrCreateAgentAsync(string agentName, string? instructions = null)
        {
            if (!IsConfigured || Client == null)
            {
                _logger.LogError("FoundryAgentProvider is not configured");
                return null;
            }

            try
            {
                // First try to find existing agent by name
                var existingAgent = await GetAgentByNameAsync(agentName);
                if (existingAgent != null)
                {
                    _logger.LogInformation("Found existing agent: {AgentName}", agentName);
                    Agent = existingAgent;
                    return existingAgent;
                }

                // Create new agent if not found
                var modelDeployment = _config["ModelDeployment"];
                if (string.IsNullOrWhiteSpace(modelDeployment))
                {
                    _logger.LogError("ModelDeployment configuration is required to create agents");
                    return null;
                }

                var agentInstructions = instructions ?? "You are a helpful assistant.";
                
                _logger.LogInformation("Creating new agent: {AgentName}", agentName);
                var newAgent = Client.Administration.CreateAgent(
                    model: modelDeployment,
                    name: agentName,
                    instructions: agentInstructions);

                _logger.LogInformation("Created agent {AgentName} with ID: {AgentId}", agentName, newAgent.Value.Id);
                Agent = newAgent.Value;
                return newAgent.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating or retrieving agent: {AgentName}", agentName);
                return null;
            }
        }

        public Task<PersistentAgent?> GetAgentByNameAsync(string agentName)
        {
            if (!IsConfigured || Client == null)
            {
                _logger.LogError("FoundryAgentProvider is not configured");
                return Task.FromResult<PersistentAgent?>(null);
            }

            try
            {
                // List all agents and find by name
                var agents = Client.Administration.GetAgents();
                
                foreach (var agent in agents)
                {
                    if (string.Equals(agent.Name, agentName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Found agent by name: {AgentName} (ID: {AgentId})", agentName, agent.Id);
                        return Task.FromResult<PersistentAgent?>(agent);
                    }
                }

                _logger.LogInformation("No agent found with name: {AgentName}", agentName);
                return Task.FromResult<PersistentAgent?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying agent by name: {AgentName}", agentName);
                return Task.FromResult<PersistentAgent?>(null);
            }
        }
    }
}
