# RAG Implementation & Setup Guide — ERP GPT

This document outlines the architecture, step-by-step setup, and operational instructions for the verified **Retrieval-Augmented Generation (RAG)** pipeline in `erp-gpt`.

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

## 🤝 Responsibility Breakdown & Implementation Progress

### 🤖 What the AI Agent Built & Verified
- [x] **SQL Migrations ([db/01_init_pgvector.sql](file:///c:/Users/Zahid%20Hamid/Documents/repos/erp-gpt/db/01_init_pgvector.sql))**: Configured `pgvector` extension and indexing table (`endpoint_embeddings`) with HNSW cosine index.
- [x] **Ingestion Engine ([embeddings/ingest.py](file:///c:/Users/Zahid%20Hamid/Documents/repos/erp-gpt/embeddings/ingest.py))**: Embedded `kb/*.json` endpoint example questions using `sentence-transformers/all-MiniLM-L6-v2` into PostgreSQL.
- [x] **Single Embedding Script ([embeddings/embed_single.py](file:///c:/Users/Zahid%20Hamid/Documents/repos/erp-gpt/embeddings/embed_single.py))**: Generates 384-dim vectors for incoming user questions.
- [x] **C# Agent System ([api/ErpGpt.Agent/](file:///c:/Users/Zahid%20Hamid/Documents/repos/erp-gpt/api/ErpGpt.Agent/))**:
  - `PgVectorMemoryStore.cs`: Connects Semantic Kernel to `pgvector` (`<=>` distance query).
  - `ContextResolver.cs`: Retrieves top-3 matching endpoint docs for user prompt.
  - `QueryPlanGenerator.cs`: Prompts local Llama 3.1 8B with `format: "json"`, `num_predict: 150`, and 5-min timeout.
  - `GraphQLValidatorAndBuilder.cs`: Schema validator & query builder supporting both offset pagination and custom aggregation queries (`topCustomers`).
  - `AgentPipeline.cs`: Complete end-to-end execution loop.
  - `Program.cs`: Interactive console runner (`Ask ERP-GPT >`).

---

## 🚀 Complete Step-by-Step Setup & Execution Instructions

### Step 1: Start PostgreSQL Container & Apply pgvector Extension
1. Ensure Docker Desktop is running.
2. Start the database container:
   ```powershell
   cd api
   docker compose up -d
   ```
3. Initialize the `pgvector` extension and table:
   ```powershell
   docker exec -i api-db-1 psql -U erpgpt -d erpgpt < db/01_init_pgvector.sql
   ```

### Step 2: Start local LLM (Ollama with Llama 3.1)
1. Install [Ollama](https://ollama.com).
2. Pull and start Llama 3.1 8B in PowerShell:
   ```powershell
   ollama run llama3.1
   ```
   *(Hosts local inference server at `http://localhost:11434`)*

### Step 3: Run Knowledge Base Ingestion
Install Python requirements and populate `pgvector`:
```powershell
pip install sentence-transformers psycopg2-binary pgvector
python embeddings/ingest.py
```

### Step 4: Launch HotChocolate GraphQL API
In a separate terminal, start the GraphQL API service:
```powershell
dotnet run --project api/ErpGpt.GraphQLApi
```
*(Runs on `http://localhost:5000/graphql`)*

### Step 5: Launch the Interactive ERP-GPT Agent
Run the C# agent pipeline:
```powershell
dotnet run --project api/ErpGpt.Agent
```

---

## 💬 Example Queries Tested & Working

```powershell
Ask ERP-GPT > Who are our top 3 customers in Canada?
Ask ERP-GPT > Who is the biggest customer?
Ask ERP-GPT > Which accounts bring in the most money?
```

### Verified Sample Output:
```json
[1/5] RAG Retrieval: Finding matching endpoint docs in pgvector...
  -> Endpoint: topCustomers | Dist: 0.3529 | Match Q: who are our biggest customers this year

[2/5] LLM Query Planner: Prompting Llama 3.1 8B via Semantic Kernel...
  -> Query Plan: { "endpoint": "topCustomers", "take": 3, "filters": { "from": "2022-01-01", "to": "2025-12-31" } }

[3/5] Validation Gate & GraphQL Construction...
  -> Generated GraphQL:
     query {
       topCustomers(limit: 3, from: "2022-01-01", to: "2025-12-31") {
         customerId
         customerName
         territory
         revenue
         orderCount
       }
     }

[4/5] Executing GraphQL against API (http://localhost:5000/graphql)...
  -> Raw Response: {"data":{"topCustomers":[{"customerId":29847,"customerName":"Action Bicycle Specialists","territory":"Central","revenue":116246.3000,"orderCount":3}, ...]}}
```
