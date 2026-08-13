# RAG Implementation & Setup Guide — ERP GPT

This document outlines the architecture, step-by-step setup, and operational responsibilities for the **Retrieval-Augmented Generation (RAG)** pipeline in `erp-gpt`.

---

## 🛠️ Stack Overview

| Component | Technology | Purpose |
|---|---|---|
| **RAG Framework** | **Microsoft Semantic Kernel** (`.NET C#`) | Orchestrates vector memory lookup, prompt composition, and LLM plugins in C# |
| **Vector DB** | **pgvector** (`PostgreSQL 16` extension) | Stores 384-dimensional embeddings of endpoint documentation for cosine search |
| **Embedding Model** | **`all-MiniLM-L6-v2`** (`SentenceTransformers`) | Converts text questions into 384-dim vectors at zero cost |
| **LLM Generation** | **Llama 3.1 8B** (via local Ollama) | Generates structured Query Plans / GraphQL requests |
| **Target API** | **HotChocolate GraphQL API** | Executes safe, compiled C# queries against Postgres |

---

## 🤝 Responsibility Breakdown: Who Does What?

### 🤖 What the AI Agent Builds & Executes Directly
- [x] **SQL Migrations (`db/01_init_pgvector.sql`)**: Creates the `pgvector` extension and indexing tables.
- [x] **Ingestion Script (`embeddings/ingest.py`)**: Python script to embed `kb/*.json` endpoint example questions and populate Postgres.
- [x] **C# Agent System (`api/ErpGpt.Agent/`)**:
  - `PgVectorMemoryStore.cs`: Connects Semantic Kernel to `pgvector`.
  - `ContextResolver.cs`: Retrieves top-3 matching endpoint docs.
  - `QueryPlanGenerator.cs`: Semantic Kernel plugin to prompt Llama 3.1.
  - `GraphQLValidatorAndBuilder.cs`: Validates and translates Query Plans into valid GraphQL strings.
  - `AgentPipeline.cs`: End-to-end execution loop.
- [x] **Build & Verification Commands**: Running `dotnet build`, database migration scripts, and ingestion tests.

### 👤 What YOU (The User) Need to Run Manually

1. **Start Docker Desktop**:
   - Ensure Docker Desktop is running on your Windows machine so the PostgreSQL database (`api-db-1`) is active.

2. **Install & Run Ollama (for Llama 3.1 8B)**:
   - Download and install [Ollama](https://ollama.com).
   - Open PowerShell / Terminal and pull the Llama 3.1 model (~4.7 GB):
     ```powershell
     ollama run llama3.1
     ```
   - This starts the local LLM server at `http://localhost:11434`.

---

## 🔄 End-to-End Execution Flow

```
┌────────────────────────┐
│ 1. User Prompt on UI   │ ("Who are our top customers in Canada?")
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│ 2. ContextResolver     │ Embeds prompt via all-MiniLM-L6-v2
└───────────┬────────────┘ Queries pgvector for top-3 matching endpoint docs
            │
            ▼
┌────────────────────────┐
│ 3. Semantic Kernel     │ Injects retrieved KB docs + user prompt
│    QueryPlanGenerator  │ Sends request to local Llama 3.1 (Ollama)
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│ 4. Llama 3.1 8B        │ Outputs structured JSON Query Plan:
└───────────┬────────────┘ { "entity": "Customer", "territory": "Canada", "limit": 3 }
            │
            ▼
┌────────────────────────┐
│ 5. Validation Gate     │ Validates fields & parameters
│    & GraphQL Builder   │ Converts Query Plan -> GraphQL query string
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│ 6. Execution           │ Executes POST against http://localhost:5000/graphql
│    & Answer Synthesis  │ Synthesizes raw JSON output into a natural response
└────────────────────────┘
```

---

## 🚀 Setup & Execution Instructions

### Step 1: Initialize Database & pgvector
Make sure your docker container is running:
```powershell
cd api
docker compose up -d
```
Enable `pgvector` and apply database tables:
```powershell
docker exec -i api-db-1 psql -U erpgpt -d erpgpt < db/01_init_pgvector.sql
```

### Step 2: Run Knowledge Base Ingestion
Install Python requirements and ingest vectors:
```powershell
pip install sentence-transformers psycopg2-binary pgvector
python embeddings/ingest.py
```

### Step 3: Run the Agent Service (.NET)
Build and run the C# agent pipeline:
```powershell
cd api/ErpGpt.Agent
dotnet run
```

---

## 🔍 Verification & Testing

You can test the RAG retrieval accuracy before connecting an LLM:
```powershell
python eval/score_retrieval.py
```
This tests your top-K vector retrieval accuracy against 50 sample user questions in `eval/questions.csv`.
