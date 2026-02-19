# Complex Subgraph Flowchart Test

This diagram has 7 subgraphs with cross-subgraph edges, fan-out patterns, and back-edges.

```mermaid
flowchart TB
    subgraph Upload["Upload Pipeline"]
        UP[Upload] --> VAL[Validate]
        VAL --> STORE[Store]
    end

    subgraph Profile["Document Profiling"]
        TOK[Tokenize]
        TYPE[Classify Type]
        PAT[Pattern Match]
        FEAT[Extract Features]
        FIT[Fitness Score]
    end

    subgraph Score["Scoring Engine"]
        SC1[Quality Score]
        SC2[Relevance Score]
        SC3[Confidence Score]
        AGG[Aggregate]
    end

    subgraph Gates["Decision Gates"]
        G1{Pass Quality?}
        G2{Pass Relevance?}
        G3{Pass Confidence?}
    end

    subgraph LLM["LLM Processing"]
        PROMPT[Build Prompt]
        CALL[API Call]
        PARSE[Parse Response]
    end

    subgraph UI["User Interface"]
        DASH[Dashboard]
        NOTIFY[Notifications]
        REPORT[Reports]
    end

    subgraph Learning["Feedback Loop"]
        FB[Collect Feedback]
        RETRAIN[Retrain Model]
    end

    STORE --> TOK
    STORE --> TYPE
    STORE --> PAT
    TOK --> FEAT
    TYPE --> FEAT
    PAT --> FEAT
    FEAT --> FIT

    FIT --> SC1
    FIT --> SC2
    FIT --> SC3
    SC1 --> AGG
    SC2 --> AGG
    SC3 --> AGG

    AGG --> G1
    G1 -->|Yes| G2
    G1 -->|No| NOTIFY
    G2 -->|Yes| G3
    G2 -->|No| NOTIFY
    G3 -->|Yes| PROMPT
    G3 -->|No| NOTIFY

    PROMPT --> CALL
    CALL --> PARSE

    PARSE --> DASH
    PARSE --> REPORT
    NOTIFY --> DASH

    PARSE --> FB
    FB --> RETRAIN
    RETRAIN -.-> FIT
```

## Simple Flowchart (no subgraphs - should still use orthogonal routing)

```mermaid
flowchart LR
    A[Start] --> B{Decision}
    B -->|Yes| C[OK]
    B -->|No| D[Cancel]
    C --> E[End]
    D --> E
```
