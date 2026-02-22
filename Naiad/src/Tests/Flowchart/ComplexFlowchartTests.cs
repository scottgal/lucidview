/// <summary>
/// Complex real-world flowchart tests to verify edge connection,
/// subgraph layout, fan-in/fan-out, and cross-subgraph edges.
/// </summary>
public class ComplexFlowchartTests : TestBase
{
    [Test]
    public Task LlmFieldMapping()
    {
        const string input =
            """
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
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CiCdPipeline()
    {
        const string input =
            """
            flowchart LR
                subgraph Trigger["Trigger"]
                    PUSH[Git Push]
                    PR[Pull Request]
                    SCHED[Scheduled]
                end

                subgraph Build["Build Stage"]
                    RESTORE[dotnet restore]
                    BUILD[dotnet build]
                    LINT[Code analysis]
                end

                subgraph Test["Test Stage"]
                    UNIT[Unit tests]
                    INT[Integration tests]
                    E2E[E2E tests]
                    COV{Coverage<br/>> 80%?}
                end

                subgraph Deploy["Deploy Stage"]
                    STAGING[Deploy to staging]
                    SMOKE[Smoke tests]
                    APPROVE{Manual<br/>approval?}
                    PROD[Deploy to prod]
                end

                subgraph Notify["Notifications"]
                    SLACK[Slack notification]
                    EMAIL[Email report]
                end

                PUSH & PR & SCHED --> RESTORE --> BUILD --> LINT
                LINT --> UNIT & INT & E2E
                UNIT & INT & E2E --> COV
                COV -->|Yes| STAGING --> SMOKE --> APPROVE
                COV -->|No| SLACK
                APPROVE -->|Yes| PROD --> SLACK & EMAIL
                APPROVE -->|No| SLACK
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MicroservicesArchitecture()
    {
        const string input =
            """
            flowchart TD
                CLIENT[Browser / Mobile App]
                CDN[CDN / Static Assets]

                subgraph Gateway["API Gateway"]
                    GW[Kong / Nginx]
                    AUTH[Auth middleware]
                    RATE[Rate limiter]
                end

                subgraph Services["Microservices"]
                    USER[User Service]
                    ORDER[Order Service]
                    PAYMENT[Payment Service]
                    NOTIFY[Notification Service]
                    SEARCH[Search Service]
                end

                subgraph Data["Data Layer"]
                    PG[(PostgreSQL)]
                    REDIS[(Redis Cache)]
                    ES[(Elasticsearch)]
                    S3[(S3 / Blob Storage)]
                end

                subgraph Messaging["Event Bus"]
                    KAFKA[Apache Kafka]
                    DLQ[Dead Letter Queue]
                end

                CLIENT --> CDN
                CLIENT --> GW --> AUTH --> RATE
                RATE --> USER & ORDER & PAYMENT & SEARCH
                USER --> PG
                USER --> REDIS
                ORDER --> PG
                ORDER --> KAFKA
                PAYMENT --> PG
                PAYMENT --> KAFKA
                SEARCH --> ES
                NOTIFY --> S3
                KAFKA --> NOTIFY
                KAFKA --> DLQ
                KAFKA --> SEARCH
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DiamondCascade()
    {
        const string input =
            """
            flowchart TD
                START([Start]) --> CHECK1{Is user<br/>authenticated?}
                CHECK1 -->|No| LOGIN[Show login page]
                CHECK1 -->|Yes| CHECK2{Has admin<br/>role?}
                LOGIN --> AUTH[Authenticate] --> CHECK1
                CHECK2 -->|No| CHECK3{Has editor<br/>role?}
                CHECK2 -->|Yes| ADMIN[Admin dashboard]
                CHECK3 -->|No| VIEWER[Read-only view]
                CHECK3 -->|Yes| EDITOR[Editor view]
                ADMIN --> DONE([End])
                EDITOR --> DONE
                VIEWER --> DONE
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HeavyFanInFanOut()
    {
        const string input =
            """
            flowchart TD
                SOURCE[Data Source]
                SOURCE --> A[Transform A]
                SOURCE --> B[Transform B]
                SOURCE --> C[Transform C]
                SOURCE --> D[Transform D]
                SOURCE --> E[Transform E]
                SOURCE --> F[Transform F]
                A --> MERGE[Merge Results]
                B --> MERGE
                C --> MERGE
                D --> MERGE
                E --> MERGE
                F --> MERGE
                MERGE --> OUT1[Output 1]
                MERGE --> OUT2[Output 2]
                MERGE --> OUT3[Output 3]
                MERGE --> OUT4[Output 4]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CrossSubgraphEdges()
    {
        const string input =
            """
            flowchart TD
                subgraph Frontend["Frontend"]
                    REACT[React App]
                    STORE[Redux Store]
                    HOOKS[Custom Hooks]
                end

                subgraph Backend["Backend API"]
                    CTRL[Controllers]
                    SVC[Services]
                    REPO[Repositories]
                end

                subgraph Infrastructure["Infrastructure"]
                    DB[(Database)]
                    CACHE[(Redis)]
                    QUEUE[Message Queue]
                end

                REACT --> STORE --> HOOKS
                HOOKS --> CTRL
                CTRL --> SVC --> REPO
                REPO --> DB
                SVC --> CACHE
                SVC --> QUEUE
                QUEUE -.-> SVC
                CACHE -.-> SVC
                DB -.->|events| QUEUE
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedShapesAndEdgeTypes()
    {
        const string input =
            """
            flowchart LR
                A([Stadium]) --> B[Rectangle]
                B --> C{Diamond}
                C -->|Path 1| D((Circle))
                C -->|Path 2| E[(Cylinder)]
                C -.->|Dotted| F>Asymmetric]
                D ==> G[[Subroutine]]
                E ==> G
                F -.-> G
                G --> H{{Hexagon}}
                H --> I[/Parallelogram/]
                H --> J[\Reverse Parallelogram\]
                I & J --> K[/Trapezoid\]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedSubgraphs()
    {
        const string input =
            """
            flowchart TD
                subgraph Cloud["AWS Cloud"]
                    subgraph VPC["VPC"]
                        subgraph Public["Public Subnet"]
                            ALB[Application<br/>Load Balancer]
                            NAT[NAT Gateway]
                        end
                        subgraph Private["Private Subnet"]
                            ECS[ECS Fargate<br/>Task]
                            RDS[(RDS<br/>PostgreSQL)]
                            ELASTICACHE[(ElastiCache<br/>Redis)]
                        end
                    end
                    S3[(S3 Bucket)]
                    SQS[SQS Queue]
                end

                USER[Users] --> ALB
                ALB --> ECS
                ECS --> RDS
                ECS --> ELASTICACHE
                ECS --> S3
                ECS --> SQS
                SQS --> ECS
                ECS --> NAT --> INTERNET[Internet APIs]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BackEdgesAndCycles()
    {
        const string input =
            """
            flowchart TD
                INIT[Initialize] --> FETCH[Fetch data]
                FETCH --> PARSE[Parse response]
                PARSE --> VALIDATE{Valid?}
                VALIDATE -->|Yes| PROCESS[Process records]
                VALIDATE -->|No| RETRY{Retries<br/>left?}
                RETRY -->|Yes| FETCH
                RETRY -->|No| ERROR[Log error]
                PROCESS --> SAVE[Save to DB]
                SAVE --> MORE{More<br/>pages?}
                MORE -->|Yes| FETCH
                MORE -->|No| COMPLETE[Complete]
                ERROR --> COMPLETE
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EndToEndDataFlow()
    {
        const string input =
            """
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
            """;

        return VerifySvg(input);
    }
}
