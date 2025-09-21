# Agentic Azure App Service app with Semantic Kernel and Azure AI Foundry Agent Service

This repository demonstrates how to build a modern .NET web application that integrates with both Azure AI Foundry Agents and Semantic Kernel Agents. It provides a simple CRUD task list and two interactive chat agents.

## Getting Started

See [Tutorial: Build an agentic web app in Azure App Service with Semantic Kernel or Azure AI Foundry Agent Service (.NET)](https://learn.microsoft.com/en-us/azure/app-service/tutorial-ai-agent-web-app-semantic-kernel-foundry-dotnet).

## Features

- **Task List**: Simple CRUD web app application.
- **Semantic Kernel Agent**: Chat with an agent powered by Semantic Kernel.
- **Azure AI Foundry Agent**: Chat with an agent powered by Azure AI Foundry Agent Service.
- **OpenAPI Schema**: Enables integration with Azure AI Foundry agents.

## Project Structure

- `Components/Layout/NavMenu.razor` — Sidebar navigation menu.
- `Components/Layout/MainLayout.razor` — Main layout with sidebar and content area.
- `Components/Pages/Home.razor` — Task list CRUD UI.
- `Components/Pages/SemanticKernelAgent.razor` — Semantic Kernel chat agent UI.
- `Components/Pages/FoundryAgent.razor` — Azure AI Foundry chat agent UI.
- `Models/` — Data models for tasks and chat messages.
- `Services/` — Service classes for task management and agent providers.
- `Plugins/` — Example plugin for task CRUD operations.
- `infra/` — Bicep and parameter files for Azure deployment.
