# Architecture Diagram# Architecture Diagram Logs Page]

    InternalLogsPage --> InternalLogsApi[Internal Activity Logs API]
    InternalLogsApi --> PostgreSQL
```

### Notes

```text
- Client apps consume REST API over HTTP/HTTPS.
- API uses JWT Bearer authentication.
- Swagger is available for development/demo.
- Internal Activity Logs Page is used by Admin/Ops for tracing.
- PostgreSQL is the system of record.
```

---

## 2. Solution Layer Architecture

```mermaid
flowchart TD
    subgraph ApiLayer[OrderManagement.Api]
        Controllers[Controllers<br/>13 controllers]
        Contracts[HTTP Contracts]
        Middlewares[Middlewares]
        SwaggerSetup[Swagger Setup]
        AuthSetup[Authentication / Authorization Setup]
        InternalLogPage[Internal Activity Logs Page]
    end

    subgraph ApplicationLayer[OrderManagement.Application]
        AppServices[Application Services<br/>13 services]
        Validators[FluentValidation Validators]
        AppExceptions[Application Exceptions]
        AppDtos[DTOs / Commands / Results]
        AppAbstractions[Application Abstractions<br/>18 interfaces]
        CancellationPolicy[Order Cancellation Policy]
    end

    subgraph DomainLayer[OrderManagement.Domain]
        BaseClasses[Base Classes<br/>Entity, AuditableEntity]
        Entities[Entities<br/>User, Product, Order, OrderItem,<br/>Payment, InventoryMovement,<br/>OrderStatusHistory, IdempotencyRecord,<br/>Store, StoreMember]
        Enums[Enums<br/>UserRole, OrderStatus, StoreMemberRole,<br/>InventoryMovementType, StockAdjustmentType,<br/>IdempotencyStatus, PaymentStatus,<br/>OrderCancellationReason]
        ValueObjects[Value Objects<br/>Money, OrderNumber, Sku]
        RuleFacts[Rule Facts<br/>OrderTransitionFact, CancelOrderFact, PaymentFact]
        RuleResults[Rule Results]
        DomainConstants[Domain Constants]
    end

    subgraph InfrastructureLayer[OrderManagement.Infrastructure]
        Repositories[Dapper Repositories]
        MigrationRunner[Database Migration Runner]
        ConnectionFactory[PostgreSQL Connection Factory]
        JwtGenerator[JWT Token Generator]
        PasswordHasher[BCrypt Password Hasher]
        CurrentUserContext[Current User Context]
        NRulesService[NRules Order Rules Service<br/>8 rules]
        IdempotencyInfra[Idempotency Service / Request Hash Service]
        ActivityLogInfra[Activity Log Queue / Worker / Repository]
        ImageStorage[Local Product Image Storage Service]
        FileUploadOptions[File Upload Options]
    end

    ApiLayer --> ApplicationLayer
    ApiLayer --> InfrastructureLayer
    ApiLayer --> DomainLayer

    ApplicationLayer --> DomainLayer

    InfrastructureLayer --> ApplicationLayer
    InfrastructureLayer --> DomainLayer

    InfrastructureLayer --> Database[(PostgreSQL)]
```

### Layer Responsibilities

```text
OrderManagement.Api:
  HTTP concerns only: controllers (13), contracts, middleware, Swagger, auth setup, internal HTML page.

OrderManagement.Application:
  Use case orchestration, validation, application-level authorization, exceptions, command/result DTOs,
  abstractions (18 interfaces), order cancellation policy, demo service, backoffice services, store services.

OrderManagement.Domain:
  Domain entities (10), enums (8), value objects (3), base classes (Entity, AuditableEntity),
  business rule facts/results.

OrderManagement.Infrastructure:
  Dapper persistence, PostgreSQL migration, JWT, BCrypt, NRules integration (8 rules),
  idempotency persistence, request hash service, activity logging infrastructure,
  local product image storage, file upload configuration.
```

---

## 3. Dependency Direction

```mermaid
flowchart LR
    API[OrderManagement.Api] --> APP[OrderManagement.Application]
    API --> INFRA[OrderManagement.Infrastructure]
    API --> DOMAIN[OrderManagement.Domain]

    APP --> DOMAIN

    INFRA --> APP
    INFRA --> DOMAIN

    DOMAIN -. no dependency .-> APP
    DOMAIN -. no dependency .-> INFRA
    DOMAIN -. no dependency .-> API

    APP -. no dependency .-> INFRA
    APP -. no dependency .-> API

    INFRA -. no dependency .-> API
```

### Dependency Rules

```text
Allowed:
- Api -> Application
- Api -> Infrastructure
- Api -> Domain
- Application -> Domain
- Infrastructure -> Application
- Infrastructure -> Domain

Not allowed:
- Domain -> any other layer
- Application -> Infrastructure
- Application -> Api
- Infrastructure -> Api
```

---

## 4. Runtime Request Pipeline

```mermaid
sequenceDiagram
    participant Client as Client
    participant Pipeline as ASP.NET Core Pipeline
    participant Corr as CorrelationIdMiddleware
    participant ReqLog as RequestLoggingMiddleware
    participant Ex as GlobalExceptionHandlingMiddleware
    participant Auth as Authentication/Authorization
    participant Ctrl as Controller
    participant App as Application Service
    participant Repo as Repository / Infrastructure
    participant DB as PostgreSQL
    participant Queue as ActivityLogQueue
    participant Worker as ActivityLogBackgroundWorker

    Client->>Pipeline: HTTP Request
    Pipeline->>Corr: Read or generate X-Correlation-ID
    Corr->>ReqLog: Continue
    ReqLog->>Ex: Continue
    Ex->>Auth: Authenticate / authorize
    Auth->>Ctrl: Execute action
    Ctrl->>App: Execute use case
    App->>Repo: Call abstraction implementation
    Repo->>DB: Query / transaction
    App->>Queue: Enqueue business activity
    Queue-->>App: Return immediately
    App-->>Ctrl: Result
    Ctrl-->>Ex: HTTP response
    Ex-->>ReqLog: Formatted response or error
    ReqLog->>Queue: Enqueue RequestCompleted / RequestFailed
    ReqLog-->>Client: Response + X-Correlation-ID
    Worker->>Queue: Read batch
    Worker->>DB: Insert activity_logs batch
```

### Middleware Order

```text
1. CorrelationIdMiddleware
2. RequestLoggingMiddleware
3. GlobalExceptionHandlingMiddleware
4. Authentication
5. Authorization
6. Controllers
```

Important:

```text
CorrelationIdMiddleware → RequestLoggingMiddleware → GlobalExceptionHandlingMiddleware.
RequestLogging wraps GlobalExceptionHandling so its finally block can observe the final
response status code after GlobalExceptionHandling has mapped exceptions.
```

---

## 5. Main API Modules

```mermaid
flowchart TD
    API[OrderManagement.Api Controllers]

    API --> AuthController[AuthController]
    API --> ProductsController[ProductsController]
    API --> OrdersController[OrdersController]
    API --> PaymentsController[PaymentsController]
    API --> StoresController[StoresController]
    API --> StoreOperatorsController[StoreOperatorsController]
    API --> BackofficeOrdersController[BackofficeOrdersController]
    API --> BackofficeProductsController[BackofficeProductsController]
    API --> BackofficeDashboardController[BackofficeDashboardController]
    API --> DemoController[DemoController]
    API --> DiagnosticsController[DiagnosticsController]
    API --> ActivityLogsController[ActivityLogsController]
    API --> InternalLogsTestController[InternalActivityLogsTestController]

    AuthController --> AuthService[IAuthService / AuthService]
    ProductsController --> ProductService[IProductService / ProductService]
    OrdersController --> OrderService[IOrderService / OrderService]
    PaymentsController --> PaymentService[IPaymentService / PaymentService]
    StoresController --> StoreService[IStoreService / StoreService]
    StoreOperatorsController --> StoreOperatorService[IStoreOperatorService / StoreOperatorService]
    BackofficeOrdersController --> BackofficeOrderService[IBackofficeOrderService / BackofficeOrderService]
    BackofficeProductsController --> ProductMgmtService[IProductManagementService / ProductManagementService]
    BackofficeDashboardController --> DashboardService[IBackofficeDashboardService / BackofficeDashboardService]
    DemoController --> DemoService[IDemoService / DemoService]
    ActivityLogsController --> ActivityLogQueryService[IActivityLogQueryService / ActivityLogQueryService]
    InternalLogsTestController --> ActivityLogWriter[IActivityLogWriter]

    AuthService --> UserRepository[UserRepository]
    ProductService --> ProductRepository[ProductRepository]
    OrderService --> OrderRepository[OrderRepository]
    PaymentService --> PaymentRepository[PaymentRepository]
    StoreService --> StoreRepository[StoreRepository]
    StoreOperatorService --> StoreMemberRepository[StoreMemberRepository]
    BackofficeOrderService --> OrderRepository
    BackofficeOrderService --> PaymentRepository
    ProductMgmtService --> ProductRepository
    ProductMgmtService --> StoreRepository
    DashboardService --> DashboardRepository[DashboardQueryRepository]
    ActivityLogQueryService --> ActivityLogQueryRepository[ActivityLogQueryRepository]
```

### Controller Routes

```text
Public API:
  AuthController              POST   /api/v1/auth/login
  ProductsController          GET    /api/v1/products
  OrdersController            POST   /api/v1/orders
  OrdersController            GET    /api/v1/orders
  OrdersController            GET    /api/v1/orders/{id}
  OrdersController            PATCH  /api/v1/orders/{id}/status
  OrdersController            POST   /api/v1/orders/{id}/cancel
  PaymentsController          POST   /api/v1/orders/{orderId}/payments
  PaymentsController          GET    /api/v1/orders/{orderId}/payments
  StoresController            POST   /api/v1/stores
  StoresController            GET    /api/v1/stores
  StoresController            GET    /api/v1/stores/{storeId}
  StoresController            PUT    /api/v1/stores/{storeId}
  StoreOperatorsController    GET    /api/v1/stores/{storeId}/operators
  StoreOperatorsController    POST   /api/v1/stores/{storeId}/operators
  StoreOperatorsController    PATCH  /api/v1/stores/{storeId}/operators/{userId}/status

Backoffice API (Admin/Ops):
  BackofficeOrdersController  GET    /api/v1/backoffice/orders
  BackofficeOrdersController  GET    /api/v1/backoffice/orders/{id}
  BackofficeOrdersController  PATCH  /api/v1/backoffice/orders/{id}/status
  BackofficeOrdersController  POST   /api/v1/backoffice/orders/{id}/cancel
  BackofficeProductsController GET   /api/v1/backoffice/products
  BackofficeProductsController GET   /api/v1/backoffice/products/{id}
  BackofficeProductsController POST  /api/v1/backoffice/products
  BackofficeProductsController PUT   /api/v1/backoffice/products/{id}
  BackofficeProductsController PATCH /api/v1/backoffice/products/{id}/status
  BackofficeProductsController POST  /api/v1/backoffice/products/{id}/image
  BackofficeProductsController POST  /api/v1/backoffice/products/{id}/stock/adjust
  BackofficeDashboardController GET  /api/v1/backoffice/dashboard

Diagnostic/Demo API:
  DiagnosticsController       GET    /api/v1/diagnostics/ok
  DiagnosticsController       GET    /api/v1/diagnostics/app-error
  DiagnosticsController       GET    /api/v1/diagnostics/unhandled-error
  DemoController              POST   /api/v1/demo/concurrent-stock-deduction

Internal Operational API (Admin/Ops):
  ActivityLogsController      GET    /api/v1/internal/activity-logs
  InternalActivityLogsTestController POST /api/v1/internal/activity-logs/test

Pages:
  Internal Logs Page          GET    /internal/activity-logs
```

---

## 6. Database Schema Overview

```mermaid
erDiagram
    users ||--o{ orders : customer
    users ||--o{ order_status_history : changed_by
    users ||--o{ inventory_movements : created_by
    users ||--o{ idempotency_keys : owns
    users ||--o{ stores : owns
    users ||--o{ store_members : member_of
    users ||--o{ payments : created_by

    stores ||--o{ store_members : has
    stores ||--o{ products : contains
    stores ||--o{ orders : placed_at

    orders ||--o{ order_items : contains
    orders ||--o{ order_status_history : has
    orders ||--o{ inventory_movements : causes
    orders ||--o{ payments : has

    products ||--o{ order_items : referenced_by
    products ||--o{ inventory_movements : tracked_by

    users {
        uuid id PK
        varchar username
        text password_hash
        varchar display_name
        varchar role
        boolean is_active
        timestamptz created_at
        timestamptz updated_at
    }

    stores {
        uuid id PK
        uuid owner_user_id FK
        varchar store_name
        varchar slug
        text description
        text logo_url
        boolean is_active
        timestamptz created_at
        timestamptz updated_at
    }

    store_members {
        uuid id PK
        uuid store_id FK
        uuid user_id FK
        varchar role
        boolean is_active
        uuid created_by FK
        timestamptz created_at
        timestamptz updated_at
    }

    products {
        uuid id PK
        uuid store_id FK
        varchar sku
        varchar name
        text description
        text primary_image_url
        int stock_quantity
        numeric price
        bigint row_version
        boolean is_active
        timestamptz created_at
        timestamptz updated_at
    }

    orders {
        uuid id PK
        uuid store_id FK
        varchar order_number
        uuid customer_id FK
        varchar status
        text shipping_address
        numeric total_amount
        bigint row_version
        uuid created_by FK
        uuid updated_by FK
        timestamptz created_at
        timestamptz updated_at
    }

    order_items {
        uuid id PK
        uuid order_id FK
        uuid product_id FK
        varchar product_name_snapshot
        numeric price
        numeric unit_price_snapshot
        int quantity
        numeric subtotal
        numeric line_total
        timestamptz created_at
    }

    inventory_movements {
        uuid id PK
        uuid product_id FK
        uuid order_id FK
        varchar movement_type
        int quantity
        int stock_before
        int stock_after
        text reason
        uuid created_by FK
        timestamptz created_at
    }

    order_status_history {
        uuid id PK
        uuid order_id FK
        varchar from_status
        varchar to_status
        text reason
        uuid changed_by FK
        timestamptz created_at
    }

    idempotency_keys {
        uuid id PK
        varchar key
        uuid user_id FK
        varchar endpoint
        text request_hash
        varchar status
        int response_status_code
        jsonb response_body
        varchar resource_type
        uuid resource_id
        timestamptz locked_until
        timestamptz created_at
        timestamptz updated_at
    }

    payments {
        uuid id PK
        uuid order_id FK
        numeric amount
        varchar status
        varchar provider
        varchar payment_reference
        uuid created_by FK
        timestamptz created_at
        timestamptz updated_at
    }
```

### Domain Entities Map

```text
Table                 Domain Entity         Base Class
users                 User                  AuditableEntity
stores                Store                 AuditableEntity
store_members         StoreMember           AuditableEntity
products              Product               AuditableEntity
orders                Order                 AuditableEntity
order_items           OrderItem             Entity
inventory_movements   InventoryMovement     Entity
order_status_history  OrderStatusHistory    Entity
idempotency_keys      IdempotencyRecord     AuditableEntity
payments              Payment               Entity

Base classes:
  Entity             — Id
  AuditableEntity    — Id, CreatedAt, UpdatedAt
```

---

## 7. Activity Logs Schema Overview

```mermaid
erDiagram
    activity_logs {
        uuid id PK
        varchar correlation_id
        varchar activity_type
        uuid actor_user_id
        varchar actor_username
        varchar actor_role
        uuid order_id
        varchar order_number
        uuid product_id
        uuid payment_id
        varchar request_path
        varchar http_method
        int status_code
        bigint elapsed_ms
        varchar error_code
        jsonb before_state
        jsonb after_state
        jsonb metadata
        timestamptz created_at
    }
```

### Activity Log Indexes

```text
idx_activity_logs_correlation_id
idx_activity_logs_activity_type
idx_activity_logs_actor_user_id
idx_activity_logs_order_id
idx_activity_logs_order_number
idx_activity_logs_product_id
idx_activity_logs_payment_id
idx_activity_logs_created_at
idx_activity_logs_error_code
```

---

## 8. Authentication Flow

```mermaid
sequenceDiagram
    participant Client as Client
    participant AuthController as AuthController
    participant AuthService as AuthService
    participant UserRepo as UserRepository
    participant BCrypt as BCryptPasswordHasher
    participant Jwt as JwtTokenGenerator
    participant DB as PostgreSQL
    participant Log as ActivityLogQueue

    Client->>AuthController: POST /api/v1/auth/login
    AuthController->>AuthService: LoginCommand
    AuthService->>AuthService: Validate command
    AuthService->>UserRepo: GetByUsernameAsync
    UserRepo->>DB: SELECT user
    DB-->>UserRepo: user row
    UserRepo-->>AuthService: User
    AuthService->>BCrypt: VerifyPassword
    BCrypt-->>AuthService: valid / invalid

    alt valid credentials
        AuthService->>Jwt: GenerateAccessToken
        Jwt-->>AuthService: JWT token
        AuthService->>Log: LoginSucceeded
        AuthService-->>AuthController: LoginResult
        AuthController-->>Client: 200 OK + token
    else invalid credentials
        AuthService->>Log: LoginFailed
        AuthService-->>Client: 401 INVALID_CREDENTIALS
    end
```

---

## 9. Create Order with Idempotency and Stock Locking

```mermaid
sequenceDiagram
    participant Client as Client
    participant Controller as OrdersController
    participant Service as OrderService
    participant Idem as IdempotencyService
    participant Repo as OrderRepository
    participant DB as PostgreSQL
    participant Log as ActivityLogQueue

    Client->>Controller: POST /api/v1/orders + Idempotency-Key
    Controller->>Service: CreateOrderCommand
    Service->>Service: Validate command
    Service->>Service: Verify current user can create order
    Service->>Log: OrderCreateStarted
    Service->>Service: Compute normalized request hash
    Service->>Idem: BeginAsync(key, userId, endpoint, hash)

    Idem->>DB: INSERT idempotency_keys InProgress
    alt Insert success
        Idem->>Log: IdempotencyAccepted
        Idem-->>Service: ProcessRequest(recordId)
        Service->>Repo: CreateAsync
        Repo->>DB: BEGIN
        Repo->>DB: SELECT nextval(order_number_seq)
        Repo->>DB: SELECT products FOR UPDATE ORDER BY id
        Repo->>DB: Validate stock
        Repo->>DB: INSERT orders
        Repo->>DB: UPDATE products stock_quantity
        Repo->>DB: INSERT inventory_movements
        Repo->>DB: INSERT order_items
        Repo->>DB: INSERT order_status_history
        Repo->>DB: COMMIT
        Repo->>Log: OrderCreated
        Repo->>Log: StockDeducted
        Service->>Idem: MarkCompleted(response)
        Service-->>Controller: CreateOrderResult
        Controller-->>Client: 201 Created
    else Existing completed
        Idem->>Log: IdempotencyReplayReturned
        Idem-->>Service: Stored response
        Controller-->>Client: Stored response
    else Existing in progress
        Idem->>Log: IdempotencyConflict
        Controller-->>Client: 409 REQUEST_ALREADY_IN_PROGRESS
    else Different payload
        Idem->>Log: IdempotencyConflict
        Controller-->>Client: 409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
    end
```

---

## 10. Order Status Update Flow

```mermaid
sequenceDiagram
    participant Client as Admin/Ops Client
    participant Controller as OrdersController / BackofficeOrdersController
    participant Service as OrderService / BackofficeOrderService
    participant Repo as OrderRepository
    participant Rules as NRulesOrderRulesService
    participant DB as PostgreSQL
    participant Log as ActivityLogQueue

    Client->>Controller: PATCH /api/v1/orders/{id}/status
    Controller->>Service: UpdateOrderStatusCommand
    Service->>Service: Validate command
    Service->>Service: Verify Admin/Ops
    Service->>Log: OrderStatusChangeStarted
    Service->>Repo: UpdateStatusAsync
    Repo->>DB: BEGIN
    Repo->>DB: SELECT order FOR UPDATE
    Repo->>Repo: Check expectedRowVersion
    Repo->>Rules: Validate transition with latest status

    alt Rule allowed
        Repo->>DB: UPDATE orders status + row_version
        Repo->>DB: INSERT order_status_history
        Repo->>DB: COMMIT
        Repo->>Log: OrderStatusChanged
        Repo-->>Service: UpdateOrderStatusResult
        Service-->>Controller: Result
        Controller-->>Client: 200 OK
    else Rule rejected
        Repo->>Log: OrderStatusRejected
        Repo->>DB: ROLLBACK
        Controller-->>Client: 422 INVALID_ORDER_STATUS_TRANSITION
    else Row version mismatch
        Repo->>DB: ROLLBACK
        Controller-->>Client: 409 CONCURRENT_UPDATE_CONFLICT
    end
```

---

## 11. Cancel Order Flow

```mermaid
sequenceDiagram
    participant Client as Customer/Admin/Ops Client
    participant Controller as OrdersController / BackofficeOrdersController
    participant Service as OrderService / BackofficeOrderService
    participant Policy as OrderCancellationPolicy
    participant Repo as OrderRepository
    participant Rules as NRulesOrderRulesService
    participant DB as PostgreSQL
    participant Log as ActivityLogQueue

    Client->>Controller: POST /api/v1/orders/{id}/cancel
    Controller->>Service: CancelOrderCommand
    Service->>Service: Validate command
    Service->>Policy: Resolve cancellation reason
    Policy-->>Service: RestoreStock true/false
    Service->>Log: OrderCancelStarted
    Service->>Repo: CancelAsync
    Repo->>DB: BEGIN
    Repo->>DB: SELECT order FOR UPDATE
    Repo->>Repo: Check expectedRowVersion
    Repo->>Rules: Validate cancel with latest status

    alt Cancel allowed
        Repo->>DB: SELECT order_items
        Repo->>DB: SELECT products FOR UPDATE ORDER BY id

        alt Restore stock
            Repo->>DB: UPDATE products stock_quantity + qty
            Repo->>DB: INSERT inventory_movements OrderCancelledRestore
            Repo->>Log: StockRestored
        else Do not restore stock
            Repo->>DB: INSERT inventory_movements OrderCancelledNoRestore
            Repo->>Log: StockNotRestored
        end

        Repo->>DB: UPDATE paid payments to RefundRequired if any
        Repo->>DB: UPDATE orders status Cancelled + row_version
        Repo->>DB: INSERT order_status_history
        Repo->>DB: COMMIT
        Repo->>Log: OrderCancelled
        Repo-->>Service: CancelOrderResult
        Controller-->>Client: 200 OK
    else Cancel rejected
        Repo->>Log: OrderStatusRejected
        Repo->>DB: ROLLBACK
        Controller-->>Client: 422 / 409
    end
```

---

## 12. Payment Flow

```mermaid
sequenceDiagram
    participant Client as Customer/Admin/Ops Client
    participant Controller as PaymentsController
    participant Service as PaymentService
    participant Repo as PaymentRepository
    participant Rules as NRulesOrderRulesService
    participant DB as PostgreSQL
    participant Log as ActivityLogQueue

    Client->>Controller: POST /api/v1/orders/{id}/payments
    Controller->>Service: CreatePaymentCommand
    Service->>Service: Validate command
    Service->>Log: PaymentCreateStarted
    Service->>Repo: CreateAsync
    Repo->>DB: BEGIN
    Repo->>DB: SELECT order FOR UPDATE
    Repo->>Repo: Verify owner/Admin/Ops
    Repo->>DB: Check existing Paid payment
    Repo->>Rules: Validate payment with latest order status

    alt Payment allowed and simulate Success
        Repo->>DB: INSERT payments Paid
        Repo->>DB: UPDATE orders Pending -> Confirmed
        Repo->>DB: INSERT order_status_history
        Repo->>DB: COMMIT
        Repo->>Log: PaymentCreated
        Repo->>Log: PaymentPaid
        Controller-->>Client: 200 OK
    else Payment allowed and simulate Failed
        Repo->>DB: INSERT payments Failed
        Repo->>DB: COMMIT
        Repo->>Log: PaymentCreated
        Repo->>Log: PaymentFailed
        Controller-->>Client: 200 OK
    else Payment rejected
        Repo->>Log: PaymentRejected
        Repo->>DB: ROLLBACK
        Controller-->>Client: 422 PAYMENT_NOT_ALLOWED
    end
```

---

## 13. Payment vs Cancel Race

```mermaid
sequenceDiagram
    participant Pay as Payment Request
    participant Cancel as Cancel Request
    participant DB as PostgreSQL
    participant Rules as NRules

    alt Payment locks order first
        Pay->>DB: BEGIN
        Pay->>DB: SELECT order FOR UPDATE
        Cancel->>DB: BEGIN
        Cancel->>DB: SELECT order FOR UPDATE waits
        Pay->>Rules: Validate payment Pending
        Rules-->>Pay: Allowed
        Pay->>DB: INSERT payment Paid
        Pay->>DB: UPDATE order Confirmed
        Pay->>DB: COMMIT

        Cancel->>DB: lock acquired, reads Confirmed
        Cancel->>Rules: Validate cancel Confirmed
        Rules-->>Cancel: Allowed
        Cancel->>DB: UPDATE payment RefundRequired
        Cancel->>DB: UPDATE order Cancelled
        Cancel->>DB: COMMIT
    else Cancel locks order first
        Cancel->>DB: BEGIN
        Cancel->>DB: SELECT order FOR UPDATE
        Pay->>DB: BEGIN
        Pay->>DB: SELECT order FOR UPDATE waits
        Cancel->>Rules: Validate cancel Pending
        Rules-->>Cancel: Allowed
        Cancel->>DB: UPDATE order Cancelled
        Cancel->>DB: COMMIT

        Pay->>DB: lock acquired, reads Cancelled
        Pay->>Rules: Validate payment Cancelled
        Rules-->>Pay: Rejected
        Pay->>DB: ROLLBACK
    end
```

Expected:

```text
The final state is always consistent.
No paid payment remains on cancelled order without refund marker.
No payment succeeds after order is already cancelled.
```

---

## 14. Internal Activity Logs Query Flow

```mermaid
sequenceDiagram
    participant User as Admin/Ops
    participant Page as Internal Logs Page
    participant API as ActivityLogsController
    participant Service as ActivityLogQueryService
    participant Repo as ActivityLogQueryRepository
    participant DB as PostgreSQL

    User->>Page: Open /internal/activity-logs
    User->>Page: Paste Admin/Ops JWT
    Page->>API: GET /api/v1/internal/activity-logs?correlationId=...
    API->>API: Authorize Admin/Ops
    API->>Service: ActivityLogQueryDto
    Service->>Service: Validate filters
    Service->>Repo: ListAsync
    Repo->>DB: SELECT activity_logs with filters
    DB-->>Repo: rows
    Repo-->>Service: PagedResult
    Service-->>API: PagedResult
    API-->>Page: JSON
    Page->>Page: Render timeline
```

---

## 15. Deployment/Runtime Components

```mermaid
flowchart TD
    AppHost[ASP.NET Core Host] --> ApiApp[OrderManagement.Api]
    ApiApp --> MigrationRunner[Startup Migration Runner]
    MigrationRunner --> Pg[(PostgreSQL)]

    ApiApp --> BackgroundServices[Hosted Services]
    BackgroundServices --> ActivityWorker[ActivityLogBackgroundWorker]
    ActivityWorker --> Pg

    ApiApp --> HttpPipeline[HTTP Pipeline]
    HttpPipeline --> Controllers[Controllers]
    Controllers --> AppServices[Application Services]
    AppServices --> Infra[Infrastructure Services]
    Infra --> Pg
```

Runtime startup sequence:

```text
1. Build host.
2. Register services and options.
3. Apply database migrations.
4. Start HTTP pipeline.
5. Start hosted background services.
6. Serve API requests.
```

---

## 16. Key Production-Grade Design Points

```text
- Idempotency-Key prevents duplicate order creation.
- PostgreSQL row lock prevents stock race.
- Product lock ordering reduces deadlock risk.
- row_version prevents lost update on order mutation.
- NRules validates lifecycle using latest locked state.
- Cancel endpoint owns all cancellation side effects.
- Payment/cancel race serialized by order row lock.
- Activity logs are async and searchable.
- Internal logs API is Admin/Ops only.
- Sensitive data is not logged.
- Store isolation: operators/scoped to their stores via StoreAuthorizationService.
- Product image uploads validated for size, extension, content-type; stored with GUID filename.
- Request hash comparison detects idempotency key reuse with different payloads.
```

---

## 17. Known Limitations Shown in Architecture

```text
- Payment provider is mocked.
- No distributed message broker yet.
- No outbox pattern yet.
- Idempotency Begin/Create/MarkCompleted are not yet one shared UnitOfWork transaction.
- Activity logs are async best-effort for non-critical tracing.
```

Future improvements:

```text
- Shared UnitOfWork transaction for idempotency + order creation.
- Outbox pattern for integration events.
- OpenTelemetry distributed tracing.
- Activity logs retention and partitioning.
- Rate limiting for login and create order.
```

---

## 18. Domain Base Classes and Value Objects

```mermaid
classDiagram
    class Entity {
        <<abstract>>
        +Guid Id
        +Equals() bool
        +GetHashCode() int
    }

    class AuditableEntity {
        <<abstract>>
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +SetCreatedAt()
        +SetUpdatedAt()
    }

    class Money {
        <<record struct>>
        +decimal Amount
        +Zero Money
        +From(decimal) Money
        +operator +()
        +operator -()
        +operator *()
    }

    class OrderNumber {
        <<record struct>>
        +string Value
        +From(string) OrderNumber
        +Generate(DateTimeOffset, long) OrderNumber
    }

    class Sku {
        <<record struct>>
        +string Value
        +From(string) Sku
    }

    Entity <|-- AuditableEntity
    AuditableEntity <|-- User
    AuditableEntity <|-- Store
    AuditableEntity <|-- StoreMember
    AuditableEntity <|-- Product
    AuditableEntity <|-- Order
    AuditableEntity <|-- IdempotencyRecord
    Entity <|-- OrderItem
    Entity <|-- Payment
    Entity <|-- InventoryMovement
    Entity <|-- OrderStatusHistory
```

---

## 1. System Context

```mermaid
flowchart TD
    %% Actors
    User[User / Customer / Operator / Admin] --> ClientApps[Client Applications]

    %% Client Applications
    ClientApps --> MVC[ASP.NET MVC Client]
    ClientApps --> Angular[Angular Client]
    ClientApps --> React[React Client]
    ClientApps --> Svelte[Svelte Client]
    ClientApps --> Vue[Vue Client]
    ClientApps --> Postman[Postman / Swagger Client]

    %% API Entry
    MVC --> API[Order Management API<br/>ASP.NET Core Web API]
    Angular --> API
    React --> API
    Svelte --> API
    Vue --> API
    Postman --> API

    %% API Public Capabilities
    API --> Auth[JWT Authentication<br/>Login / Bearer Token]
    API --> Products[Products API<br/>List / Detail]
    API --> Orders[Orders API<br/>Create / Get / List / Status / Cancel]
    API --> Payments[Payments API<br/>Create / List Payments]
    API --> Stores[Stores API<br/>Open / List / Update]
    API --> StoreOperators[Store Operators API<br/>Manage Store Operators]
    API --> Health[Health Check<br/>/health]
    API --> Swagger[Swagger UI<br/>/swagger]

    %% Backoffice API Capabilities
    API --> BackofficeOrders[Backoffice Orders API<br/>List / Detail / Status / Cancel]
    API --> BackofficeProducts[Backoffice Products API<br/>CRUD / Image Upload / Stock Adjust]
    API --> BackofficeDashboard[Backoffice Dashboard API<br/>Summary Statistics]

    %% Demo / Diagnostic Capabilities
    API --> Demo[Demo API<br/>Concurrent Stock Deduction]
    API --> Diagnostics[Diagnostics API<br/>OK / App Error / Unhandled Error]

    %% Internal Operational Capability
    API --> InternalLogsApi[Internal Activity Logs API<br/>Admin/Ops Only]
    InternalLogsPage[Internal Activity Logs Page<br/>/internal/activity-logs] --> InternalLogsApi

    %% Cross-cutting Runtime Concerns
    API --> Correlation[Correlation ID<br/>X-Correlation-ID]
    API --> ErrorHandling[Global Exception Handling<br/>Consistent Error Response]
    API --> RequestLogging[Request Logging<br/>RequestCompleted / RequestFailed]
    API --> ActivityQueue[Activity Log Queue<br/>Bounded Channel]

    %% Background Processing
    ActivityQueue --> ActivityWorker[ActivityLogBackgroundWorker<br/>Batch Insert]

    %% Database
    API --> PostgreSQL[(PostgreSQL Database)]
    ActivityWorker --> PostgreSQL
    InternalLogsApi --> PostgreSQL

    %% Database Tables
    PostgreSQL --> Users[(users)]
    PostgreSQL --> StoresTable[(stores)]
    PostgreSQL --> StoreMembers[(store_members)]
    PostgreSQL --> ProductsTable[(products)]
    PostgreSQL --> OrdersTable[(orders)]
    PostgreSQL --> OrderItems[(order_items)]
    PostgreSQL --> InventoryMovements[(inventory_movements)]
    PostgreSQL --> StatusHistory[(order_status_history)]
    PostgreSQL --> IdempotencyKeys[(idempotency_keys)]
    PostgreSQL --> PaymentsTable[(payments)]
    PostgreSQL --> ActivityLogs[(activity_logs)]
    PostgreSQL --> SchemaMigrations[(schema_migrations)]

    %% Styling
    classDef client fill:#e0f2fe,stroke:#0284c7,color:#0f172a;
    classDef api fill:#ede9fe,stroke:#7c3aed,color:#0f172a;
    classDef internal fill:#fef3c7,stroke:#d97706,color:#0f172a;
    classDef db fill:#dcfce7,stroke:#16a34a,color:#0f172a;
    classDef cross fill:#fce7f3,stroke:#db2777,color:#0f172a;

    class MVC,Angular,React,Svelte,Vue,Postman,ClientApps client;
    class API,Auth,Products,Orders,Payments,Stores,StoreOperators,Health,Swagger api;
    class BackofficeOrders,BackofficeProducts,BackofficeDashboard,Demo,Diagnostics api;
    class InternalLogsApi,InternalLogsPage internal;
    class PostgreSQL,Users,StoresTable,StoreMembers,ProductsTable,OrdersTable,OrderItems,InventoryMovements,StatusHistory,IdempotencyKeys,PaymentsTable,ActivityLogs,SchemaMigrations db;
    class Correlation,ErrorHandling,RequestLogging,ActivityQueue,ActivityWorker cross;
```

### Explanation

System context menggambarkan siapa saja yang berinteraksi dengan sistem dan komponen eksternal/internal apa saja yang terlibat.

```text
Users:
  Customer, Operator, Admin.

Client Applications:
  Bisa berupa ASP.NET MVC, Angular, React, Svelte, Vue, Postman, atau Swagger.

Order Management API:
  Entry point utama untuk semua use case:
  - Login
  - Product list/detail
  - Create order
  - Get/list orders
  - Update status
  - Cancel order
  - Create/list payments
  - Open/manage stores and store operators
  - Backoffice order management (Admin/Ops)
  - Backoffice product CRUD, image upload, stock adjustment
  - Backoffice dashboard summary
  - Demo: concurrent stock deduction scenario
  - Diagnostics: verify API pipeline health
  - Internal activity log tracing

PostgreSQL:
  System of record untuk users, stores, store members, products, orders, order items, payments,
  idempotency records, inventory movements, status history, migrations, dan activity logs.

Internal Activity Logs:
  Digunakan oleh Admin/Ops untuk tracing operasional berdasarkan correlation ID, order ID,
  order number, activity type, actor, dan date range.
```

### Key Runtime Concerns

```text
Authentication:
  API menggunakan JWT Bearer token.

Authorization:
  Internal logs hanya bisa diakses Admin/Ops.
  Customer hanya bisa melihat/mengelola order miliknya sendiri.
  Store operators/scoped ke store masing-masing melalui StoreAuthorizationService.

Correlation:
  Semua request memiliki X-Correlation-ID untuk tracing end-to-end.

Error Handling:
  Semua exception dikonversi ke error response yang konsisten.

Activity Logging:
  Business activity logs dikirim ke bounded in-memory queue dan dipersist oleh background worker.

Persistence:
  PostgreSQL digunakan untuk transactional data, idempotency, audit trail, dan activity logs.
```

### Important Boundaries

```text
Public API:
  /api/v1/auth
  /api/v1/products
  /api/v1/orders
  /api/v1/orders/{id}/payments
  /api/v1/stores
  /api/v1/stores/{storeId}/operators
  /health
  /swagger

Backoffice API (Admin/Ops):
  /api/v1/backoffice/orders
  /api/v1/backoffice/products
  /api/v1/backoffice/dashboard

Demo / Diagnostics:
  /api/v1/demo
  /api/v1/diagnostics

Internal Operational API:
  /api/v1/internal/activity-logs
  /api/v1/internal/activity-logs/test
  /internal/activity-logs

Database Boundary:
  API adalah satu-satunya komponen aplikasi yang mengakses PostgreSQL secara langsung.
```
