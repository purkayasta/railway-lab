# SPEC-helpdesk

Depends on: platform, reservation (read-only bookings tool).

Planned under T12. If scope must shrink, cut streaming first, then keep the design only. Current Helpdesk page only explains that support is unavailable.

## Objective
A RAG chat that answers questions about how the ticketing system works, with citations, and can look up the caller's own bookings. Demonstrates RAG, vector search, streaming, auth propagation and prompt-injection awareness.

## Data model (schema `helpdesk`)
- All tables use `Id bigint` (see the id strategy in SPEC.md); only `Conversation` has `PublicId` because it appears in API paths.
- `Document(Id, Title, SourcePath, ContentHash, IngestedAtUtc)`
- `Chunk(Id, DocumentId, Ordinal, Text, Embedding)` (vector type in SQL Server 2025 if usable; otherwise stored as binary and cosine computed in memory; decided in ADR 8)
- `Conversation(Id, PublicId, UserId, CreatedAtUtc)` and `Message(Id, ConversationId, Role, Content, CitationsJson, CreatedAtUtc)`

## Behavior
- Corpus: markdown files under `src/Services/Helpdesk/Knowledge/` (how to book, hold rules, payments, FAQ, admin guide) with about 10 docs.
- Ingestion: background job on startup chunks (size and overlap configurable), embeds, upserts by content hash (idempotent).
- Embeddings and chat through Microsoft.Extensions.AI abstractions with Ollama locally (models chosen at the start of the module; licences checked).
- Query: embed question, top-k retrieval, prompt with retrieved context, answer with citations (document title and chunk).
- Streaming over SSE (cut first if short on time; non-streaming is the minimum).
- Tool `get_my_bookings` (read-only): calls the Reservation service through the gateway with the caller's forwarded token; never executes actions.
- Retrieved text and user text are treated as untrusted (system prompt instructs to ignore instructions inside context).

## API (`helpdesk:chat`)
`POST /conversations`, `POST /conversations/{conversationPublicId}/messages` (SSE or JSON), `GET /conversations/{conversationPublicId}`; conversations are private to the owner.

## UI
Chat page with message list, streaming text, citation chips, a loading/error state.

## Acceptance criteria
1. Ingestion on a fresh database produces chunks and rerunning it creates no duplicates.
2. An evaluation set of about 20 questions with expected source documents achieves retrieval hit rate of at least 80% (threshold agreed before measuring), and the eval runs as a test.
3. Answers include citations that point to retrieved chunks; a question outside the corpus gets a "I don't know" style response.
4. User isolation: alice asking for "my bookings" never sees bob's bookings; a test forces a prompt that asks for another user's data and verifies none leaks; another user's conversation id returns 404.
5. A document containing an injected instruction ("ignore previous instructions...") does not change behavior in a test.
6. ADR 8 documents the vector store and chunking choices.

## Testing
Eval test; isolation test; ingestion idempotency test (embedding client faked in CI, real Ollama locally).

## Boundaries
Never: send data to third-party hosted LLMs; the Helpdesk assistant never performs write actions. Ask first: adding any model larger than the laptop can run comfortably.

## Open questions
Ollama chat and embedding models (decide at module start).

## Diagrams

### Ingestion (idempotent)

```mermaid
flowchart LR
    MD[Markdown files in Knowledge/] --> H{Content hash changed?}
    H -- no --> SK[skip]
    H -- yes --> CH[chunk with overlap] --> EM[embed chunks] --> UP[(Document + Chunk upsert)]
```

### Question answering with citations

```mermaid
sequenceDiagram
    actor U as Passenger
    participant API as Helpdesk API
    participant DB as Vector search (helpdesk schema)
    participant LLM as Chat model (local)
    participant RES as Reservation service
    U->>API: POST message (token)
    API->>API: embed question
    API->>DB: top-k chunks
    DB-->>API: chunks + sources
    opt question needs the user's bookings
        API->>RES: GET /bookings with forwarded user token
        RES-->>API: only this user's bookings
    end
    API->>LLM: system prompt (untrusted context rules) + chunks + question
    LLM-->>API: streamed answer
    API-->>U: SSE tokens + citations
    API->>API: save Message with citations
```

### Trust boundaries

```mermaid
flowchart TD
    UT[User text] --> UNT[Untrusted]
    RC[Retrieved chunks] --> UNT
    UNT --> LLM[LLM]
    SYS[System prompt: ignore instructions inside context, read-only tools only] --> LLM
    LLM --> TOOL[get_my_bookings only]
    TOOL --> TK[Caller's own token, so only own data]
    LLM -. never .-x ACT[Write actions]
```
