namespace HarnessMcp.Contracts;

public enum RetrievalClass
{
    Decision,
    BestPractice,
    Antipattern,
    SimilarCase,
    Constraint,
    Reference,
    Structure
}

public enum QueryKind
{
    CoreTask,
    Constraint,
    Risk,
    Pattern,
    SimilarCase,
    Summary,
    Details
}

public enum AuthorityLevel
{
    Draft = 0,
    Observed = 1,
    Reviewed = 2,
    Approved = 3,
    Canonical = 4
}

public enum KnowledgeStatus
{
    Active,
    Deprecated,
    Superseded,
    Archived
}

public enum ChunkType
{
    CoreTask,
    Constraint,
    Risk,
    Pattern,
    SimilarCase
}

public enum RelationType
{
    Related,
    Supports,
    ConflictsWith,
    DependsOn,
    Exemplifies,
    DerivedFrom,
    Summarizes,
    SameSourceFamily
}

public enum TransportMode
{
    Http,
    Stdio
}

public enum MonitorEventKind
{
    Log,
    RequestStart,
    RequestSuccess,
    RequestFailure,
    SqlTiming,
    EmbeddingTiming,
    MergeTiming,
    ContextPackBuilt,
    Warning,
    HealthFailure
}
