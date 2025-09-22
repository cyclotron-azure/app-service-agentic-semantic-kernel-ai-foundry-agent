using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CRUDTasksWithAgent.Services;
using System.Text.Json;
using System.Linq;

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
        Task<List<PersistentAgent>> GetAllAgentsAsync();
        Task<string> HandleFunctionCallAsync(string functionName, string arguments);
    }

    public class FoundryAgentProvider : IFoundryAgentProvider
    {
        private readonly ILogger<FoundryAgentProvider> _logger;
        private readonly IConfiguration _config;
        private readonly TaskService _taskService;
        
        public bool IsConfigured { get; }
        public PersistentAgentsClient? Client { get; }
        public string? ThreadId { get; }
        public PersistentAgent? Agent { get; private set; }

        public FoundryAgentProvider(IConfiguration config, ILogger<FoundryAgentProvider> logger, TaskService taskService)
        {
            _logger = logger;
            _config = config;
            _taskService = taskService;
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

                var agentInstructions = instructions ?? "You are a helpful assistant that can manage tasks. You have access to task management functions to create, read, update, and delete tasks.";
                var tools = CreateTaskServiceTools();
                
                _logger.LogInformation("Creating new agent: {AgentName} with {ToolCount} tools", agentName, tools.Count);
                var newAgent = Client.Administration.CreateAgent(
                    model: modelDeployment,
                    name: agentName,
                    instructions: agentInstructions,
                    tools: tools);

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

        public Task<List<PersistentAgent>> GetAllAgentsAsync()
        {
            if (!IsConfigured || Client == null)
            {
                _logger.LogError("FoundryAgentProvider is not configured");
                return Task.FromResult(new List<PersistentAgent>());
            }

            try
            {
                // Get all agents and convert to list
                var agents = Client.Administration.GetAgents();
                var agentList = new List<PersistentAgent>();
                
                foreach (var agent in agents)
                {
                    agentList.Add(agent);
                }

                _logger.LogInformation("Retrieved {Count} agents from Azure AI Foundry", agentList.Count);
                return Task.FromResult(agentList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all agents");
                return Task.FromResult(new List<PersistentAgent>());
            }
        }

        private List<ToolDefinition> CreateTaskServiceTools()
        {
            var tools = new List<ToolDefinition>();

            // Create Task Function
            var createTaskTool = new FunctionToolDefinition(
                name: "create_task",
                description: "Creates a new task with a title and optional completion status",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Title of the task" },
                        isComplete = new { type = "boolean", description = "Whether the task is complete", @default = false }
                    },
                    required = new[] { "title" }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // Read Tasks Function
            var readTasksTool = new FunctionToolDefinition(
                name: "read_tasks",
                description: "Reads all tasks, or a single task if an id is provided",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Id of the task to read (optional)" }
                    },
                    required = new string[0]
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // Update Task Function
            var updateTaskTool = new FunctionToolDefinition(
                name: "update_task",
                description: "Updates the specified task fields by id",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Id of the task to update" },
                        title = new { type = "string", description = "New title (optional)" },
                        isComplete = new { type = "boolean", description = "New completion status (optional)" }
                    },
                    required = new[] { "id" }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // Delete Task Function
            var deleteTaskTool = new FunctionToolDefinition(
                name: "delete_task",
                description: "Deletes a task by id",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Id of the task to delete" }
                    },
                    required = new[] { "id" }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            tools.Add(createTaskTool);
            tools.Add(readTasksTool);
            tools.Add(updateTaskTool);
            tools.Add(deleteTaskTool);

            return tools;
        }

        public async Task<string> HandleFunctionCallAsync(string functionName, string arguments)
        {
            try
            {
                var parsedArgs = JsonDocument.Parse(arguments);
                var args = parsedArgs.RootElement;

                return functionName switch
                {
                    "create_task" => await HandleCreateTaskAsync(args),
                    "read_tasks" => await HandleReadTasksAsync(args),
                    "update_task" => await HandleUpdateTaskAsync(args),
                    "delete_task" => await HandleDeleteTaskAsync(args),
                    _ => $"Unknown function: {functionName}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling function call: {FunctionName}", functionName);
                return $"Error executing function {functionName}: {ex.Message}";
            }
        }

        private async Task<string> HandleCreateTaskAsync(JsonElement args)
        {
            var title = args.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : "";
            var isComplete = args.TryGetProperty("isComplete", out var completeElement) ? completeElement.GetBoolean() : false;

            if (string.IsNullOrWhiteSpace(title))
                return "Error: Task title is required";

            var task = await _taskService.AddTaskAsync(title);
            if (isComplete)
            {
                await _taskService.SetTaskCompletionAsync(task, true);
            }

            return $"Task created successfully: ID {task.Id}, Title '{task.Title}', Complete: {task.IsComplete}";
        }

        private async Task<string> HandleReadTasksAsync(JsonElement args)
        {
            if (args.TryGetProperty("id", out var idElement) && 
                !string.IsNullOrWhiteSpace(idElement.GetString()) &&
                int.TryParse(idElement.GetString(), out var taskId))
            {
                var task = await _taskService.GetTaskByIdAsync(taskId);
                if (task == null)
                    return $"Task with ID {taskId} not found";
                
                return FormatTask(task);
            }

            var tasks = await _taskService.GetAllTasksAsync();
            if (!tasks.Any())
                return "No tasks found";

            return string.Join("\n\n", tasks.Select(FormatTask));
        }

        private async Task<string> HandleUpdateTaskAsync(JsonElement args)
        {
            if (!args.TryGetProperty("id", out var idElement) || 
                !int.TryParse(idElement.GetString(), out var taskId))
                return "Error: Valid task ID is required";

            var task = await _taskService.GetTaskByIdAsync(taskId);
            if (task == null)
                return $"Task with ID {taskId} not found";

            var updated = false;

            if (args.TryGetProperty("title", out var titleElement) && 
                !string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                task.Title = titleElement.GetString()!;
                updated = true;
            }

            if (args.TryGetProperty("isComplete", out var completeElement))
            {
                task.IsComplete = completeElement.GetBoolean();
                updated = true;
            }

            if (updated)
            {
                await _taskService.UpdateTaskAsync(task);
                return $"Task {taskId} updated successfully: {FormatTask(task)}";
            }

            return "No updates provided";
        }

        private async Task<string> HandleDeleteTaskAsync(JsonElement args)
        {
            if (!args.TryGetProperty("id", out var idElement) || 
                !int.TryParse(idElement.GetString(), out var taskId))
                return "Error: Valid task ID is required";

            var task = await _taskService.GetTaskByIdAsync(taskId);
            if (task == null)
                return $"Task with ID {taskId} not found";

            await _taskService.DeleteTaskAsync(task);
            return $"Task {taskId} '{task.Title}' deleted successfully";
        }

        private static string FormatTask(Models.TaskItem task) =>
            $"ID: {task.Id}\nTitle: {task.Title}\nComplete: {(task.IsComplete ? "Yes" : "No")}";
    }
}
