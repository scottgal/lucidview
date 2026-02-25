# LLM Field Mapping Diagrams

## Overview Flow

```mermaid
flowchart TB
    subgraph Upload["1. File Upload"]
        FILE[CSV / XLSX / JSON / Parquet]
    end

    subgraph Profile["2. Column Profiling"]
        DUCK[DuckDB ingestion]
        PROF[Column profiler<br/>types, stats, patterns, top values]
    end

    subgraph Score["3. Deterministic Scoring"]
        TOK[Token similarity<br/>+ abbreviation expansion]
        TYPE[Type compatibility]
        PAT[Pattern matching]
        FEAT[Feature group scoring]
        FIT[Data fit scoring]
        LEARN_BOOST[Learned alias boost<br/>from prior acceptances]
    end

    subgraph Gates["3b. Safety Gates"]
        MOV{Margin of<br/>victory < 7%?}
    end

    subgraph LLM["4. LLM Verification"]
        GATE{Confidence<br/>< 80%?}
        OLLAMA[qwen3:0.6b<br/>via Ollama or LLamaSharp]
        CONFIRM[LLM confirms or<br/>suggests alternative]
    end

    subgraph UI["5. User Review"]
        READY[Ready mappings<br/>confidence >= 80%]
        REVIEW[Review mappings<br/>50-79% confidence]
        UNMAP[Unmapped<br/>< 50% confidence]
        ACCEPT[User clicks Accept]
    end

    subgraph Learning["6. Learning Loop"]
        API[POST /accept-mapping]
        DB[(learned_aliases<br/>PostgreSQL)]
    end

    FILE --> DUCK --> PROF
    PROF --> TOK & TYPE & PAT & FEAT & FIT
    DB -.->|load at start| LEARN_BOOST
    TOK & TYPE & PAT & FEAT & FIT & LEARN_BOOST --> MOV
    MOV -->|Yes: cap to 79%| GATE
    MOV -->|No| GATE
    GATE -->|No: >= 80%| READY
    GATE -->|Yes: < 80%| OLLAMA --> CONFIRM --> REVIEW
    REVIEW --> ACCEPT --> API --> DB
    READY --> ACCEPT
```

## Scoring Pipeline

```mermaid
flowchart LR
    SRC["Source column:<br/>&quot;ln_amt&quot;"]

    subgraph Tokenize["Tokenization"]
        T1["Tokenize: [ln, amt]"]
        T2["Expand abbreviations:<br/>[loan, amount]"]
    end

    subgraph Match["Multi-Strategy Matching"]
        M1["vs target tokens"]
        M2["vs target aliases"]
        M3["vs learned aliases<br/>(x0.80 if 1 accept,<br/>x0.95 if 2+ accepts)"]
        M4["Entity-stripped<br/>matching"]
    end

    subgraph Signals["Scoring Signals"]
        S1["Header score<br/>(best of strategies)"]
        S2["Type compatibility"]
        S3["Pattern match"]
        S4["Feature group"]
        S5["Data fit"]
        S6["Semantic penalty"]
    end

    subgraph Calc["Confidence Calculator"]
        W["Dynamic weights<br/>based on data availability"]
        FLOOR["Confidence floors<br/>for strong name matches"]
        FINAL["Final confidence<br/>0.0 - 1.0"]
    end

    SRC --> T1 --> T2
    T2 --> M1 & M2 & M3 & M4
    M1 & M2 & M3 & M4 --> S1
    S1 & S2 & S3 & S4 & S5 & S6 --> W --> FLOOR --> FINAL
```

## Margin-of-Victory Gate

```mermaid
flowchart LR
    SCORE["Top candidate:<br/>loan.rate = 83%"]
    RUNNER["Runner-up:<br/>loan.interest_rate = 79%"]
    MARGIN{"Margin:<br/>83% - 79% = 4%<br/> < 7%?"}
    CAP["Cap to 79%<br/>Force REVIEW"]
    KEEP["Keep 83%<br/>Mark READY"]

    SCORE --> MARGIN
    RUNNER --> MARGIN
    MARGIN -->|Yes: too close| CAP
    MARGIN -->|No: clear winner| KEEP
```

## LLM Verification Flow

```mermaid
sequenceDiagram
    participant Engine as Mapping Engine
    participant Helper as LlmPromptHelper
    participant LLM as qwen3:0.6b<br/>(Ollama / LLamaSharp)

    Engine->>Helper: BuildPrompt(profile, bestMatch, alternatives)
    Helper-->>Engine: Structured prompt

    Note over Helper: Prompt includes:<br/>- Column name + type<br/>- 3 sample values<br/>- Current best match + confidence<br/>- Top 2 alternatives

    Engine->>LLM: POST /api/generate

    Note over LLM: temperature=0.1<br/>think=false<br/>num_predict=256

    LLM-->>Engine: JSON response

    Engine->>Helper: ParseResponse(json)

    alt LLM confirms match
        Helper-->>Engine: IsConfidentMatch=true
        Engine->>Engine: Boost confidence +10%<br/>(capped at 1.0)
    else LLM suggests alternative
        Helper-->>Engine: ShouldUseAlternative=true<br/>AlternativeFieldId="loan.rate"
        Engine->>Engine: Switch to alternative candidate
    else LLM uncertain
        Helper-->>Engine: IsConfidentMatch=false
        Engine->>Engine: Keep original, add warning
    end
```

## Learning System Flow

```mermaid
sequenceDiagram
    participant User as User (Browser)
    participant React as React Frontend
    participant API as POST /accept-mapping
    participant Repo as LearnedAliasRepository
    participant DB as PostgreSQL<br/>learned_aliases

    Note over User,React: Import #1: "ln_num" scores 67% -> loan.loan_id

    User->>React: Clicks Accept on "ln_num"
    React->>React: Set confidence = 1.0 (local state)
    React->>API: { sourceColumn: "ln_num",<br/>targetField: "loan.loan_id",<br/>confidence: 0.67 }
    API->>Repo: UpsertAsync(tenant, schema,<br/>"loan.loan_id", "ln_num", 0.67)
    Repo->>DB: INSERT learned_alias<br/>AcceptedCount = 1

    Note over User,DB: Import #2: new file also has "ln_num"

    DB-->>Repo: Load aliases for tenant+schema
    Repo-->>React: "ln_num" -> "loan.loan_id" (learned)

    Note over React: Scoring: token similarity of<br/>"ln_num" vs learned alias "ln_num"<br/>= 1.0 * 0.80 = 80% (1 accept)<br/>or 1.0 * 0.95 = 95% (2+ accepts)

    React-->>User: "ln_num" -> loan.loan_id<br/>at 80% (Review) or 95% (Ready)
```

## Learning Lifecycle (Graduated Trust)

```mermaid
flowchart TD
    subgraph First["Import #1"]
        A1["Source: &quot;ln_num&quot;"]
        A2["Token match: loan + number<br/>Score: 67%"]
        A3["Status: REVIEW"]
        A4["User clicks Accept"]
        A5["Stored: ln_num -> loan.loan_id<br/>AcceptedCount: 1"]
    end

    subgraph Second["Import #2 (same column)"]
        B1["Source: &quot;ln_num&quot;"]
        B2["Learned alias: 1.0 * 0.80<br/>Score: 80% (1 accept)"]
        B3["Status: REVIEW<br/>(still needs confirmation)"]
        B4["User clicks Accept again"]
        B5["Updated: AcceptedCount: 2"]
    end

    subgraph Third["Import #3 (same column)"]
        C1["Source: &quot;ln_num&quot;"]
        C2["Learned alias: 1.0 * 0.95<br/>Score: 95% (2+ accepts)"]
        C3["Status: READY"]
        C4["No user action needed"]
    end

    A1 --> A2 --> A3 --> A4 --> A5
    A5 -.->|loaded at scoring time| B2
    B1 --> B2 --> B3 --> B4 --> B5
    B5 -.->|loaded at scoring time| C2
    C1 --> C2 --> C3 --> C4
```

## End-to-End Data Flow

```mermaid
flowchart TD
    subgraph Input
        FILE["Upload file<br/>(CSV/XLSX/JSON/Parquet)"]
    end

    subgraph Profiling
        DUCK["DuckDB ingestion"]
        PROF["Column profiler"]
    end

    subgraph Scoring
        ALIAS_LOAD["Load learned aliases<br/>for tenant + schema"]
        SCORE["Score each column<br/>against all target fields"]
        RANK["Rank candidates<br/>by confidence"]
    end

    subgraph Verification
        CHECK{Confidence<br/>< 80%?}
        LLM["LLM verification<br/>(qwen3:0.6b)"]
        SKIP["Keep score as-is"]
    end

    subgraph Pipeline
        MAPPER["Detect content mappers<br/>(date format, decimal, etc.)"]
        VALID["Resolve validators"]
        DERIVED["Detect derived mappings<br/>(address split, name concat)"]
    end

    subgraph Review
        READY["Ready >= 80%"]
        REV["Review 50-79%"]
        UNMAP["Unmapped < 50%"]
    end

    subgraph Feedback
        ACCEPT["User accepts mapping"]
        PERSIST["Persist learned alias"]
        AUDIT["Audit log<br/>(JSON file, async)"]
    end

    FILE --> DUCK --> PROF
    PROF --> ALIAS_LOAD --> SCORE --> RANK
    RANK --> CHECK
    CHECK -->|Yes + LLM enabled| LLM --> MAPPER
    CHECK -->|No| SKIP --> MAPPER
    MAPPER --> VALID --> DERIVED
    DERIVED --> READY & REV & UNMAP
    REV -->|User action| ACCEPT --> PERSIST
    READY -->|User action| ACCEPT
    DERIVED --> AUDIT
```
