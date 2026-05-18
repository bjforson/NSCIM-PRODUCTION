import uuid
from datetime import datetime, timezone
from sqlalchemy import Column, String, Integer, Float, Boolean, DateTime, Text, LargeBinary, ForeignKey, create_engine
from sqlalchemy.dialects.postgresql import UUID, JSONB
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession, async_sessionmaker
from sqlalchemy.orm import declarative_base, relationship, sessionmaker
from config import DATABASE_URL, DATABASE_URL_SYNC

Base = declarative_base()

# Async engine for FastAPI
async_engine = create_async_engine(DATABASE_URL, pool_size=10, max_overflow=5, echo=False)
AsyncSessionLocal = async_sessionmaker(async_engine, class_=AsyncSession, expire_on_commit=False)

# Sync engine for migrations and background worker
sync_engine = create_engine(DATABASE_URL_SYNC, pool_size=5, echo=False)
SyncSessionLocal = sessionmaker(bind=sync_engine)


async def get_db():
    async with AsyncSessionLocal() as session:
        try:
            yield session
        finally:
            await session.close()


class ImageSplitJob(Base):
    __tablename__ = "image_split_jobs"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    container_numbers = Column(Text, nullable=False)
    source_image_id = Column(UUID(as_uuid=True), nullable=True)
    scanner_type = Column(String(50), nullable=True)
    image_display_profile = Column(String(64), nullable=True)
    image_data = Column(LargeBinary, nullable=True)
    image_width = Column(Integer, nullable=True)
    image_height = Column(Integer, nullable=True)
    status = Column(String(20), default="pending")  # pending/processing/completed/failed
    best_strategy = Column(String(50), nullable=True)
    best_score = Column(Float, nullable=True)
    split_x = Column(Integer, nullable=True)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))
    completed_at = Column(DateTime(timezone=True), nullable=True)
    error_message = Column(Text, nullable=True)
    analyst_verdict = Column(String(20), nullable=True)   # 'approved', 'rejected', None
    correct_split_x = Column(Integer, nullable=True)       # analyst's ground truth split point
    reviewed_by = Column(String(100), nullable=True)
    reviewed_at = Column(DateTime(timezone=True), nullable=True)

    # 1.20.0 — Ground truth annotation (human click-to-set via /groundtruth UI)
    ground_truth_split_x = Column(Integer, nullable=True)
    ground_truth_set_by = Column(Text, nullable=True)
    ground_truth_set_at = Column(DateTime(timezone=True), nullable=True)
    ground_truth_notes = Column(Text, nullable=True)

    # 1.19.0 — Claude Vision tracking columns
    claude_vision_split_x = Column(Integer, nullable=True)
    claude_vision_confidence = Column(Float, nullable=True)
    claude_vision_reasoning = Column(Text, nullable=True)
    claude_vision_input_tokens = Column(Integer, nullable=True)
    claude_vision_output_tokens = Column(Integer, nullable=True)
    claude_vision_latency_ms = Column(Integer, nullable=True)
    claude_vision_model = Column(String(64), nullable=True)
    claude_vision_ran_at = Column(DateTime(timezone=True), nullable=True)

    results = relationship("ImageSplitResult", back_populates="job", cascade="all, delete-orphan")
    assignments = relationship("ImageSplitAssignment", back_populates="job", cascade="all, delete-orphan")
    remote_vision_runs = relationship("RemoteVisionRun", back_populates="job", cascade="all, delete-orphan")
    local_model_prediction_runs = relationship("LocalModelPredictionRun", back_populates="job", cascade="all, delete-orphan")


class ImageSplitResult(Base):
    __tablename__ = "image_split_results"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    job_id = Column(UUID(as_uuid=True), ForeignKey("image_split_jobs.id"), nullable=False)
    strategy_name = Column(String(50), nullable=False)
    split_x = Column(Integer, nullable=False)
    confidence = Column(Float, nullable=True)
    processing_ms = Column(Integer, nullable=True)
    strategy_metadata = Column("metadata", JSONB, nullable=True)
    left_image = Column(LargeBinary, nullable=True)
    right_image = Column(LargeBinary, nullable=True)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))

    job = relationship("ImageSplitJob", back_populates="results")


class ImageSplitAssignment(Base):
    __tablename__ = "image_split_assignments"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    job_id = Column(UUID(as_uuid=True), ForeignKey("image_split_jobs.id"), nullable=False)
    result_id = Column(UUID(as_uuid=True), ForeignKey("image_split_results.id"), nullable=True)
    container_number = Column(String(20), nullable=False)
    position = Column(String(10), nullable=False)  # 'left' or 'right'
    image_data = Column(LargeBinary, nullable=True)
    approved = Column(Boolean, default=False)
    approved_by = Column(String(100), nullable=True)
    approved_at = Column(DateTime(timezone=True), nullable=True)

    job = relationship("ImageSplitJob", back_populates="assignments")


class RemoteVisionRun(Base):
    __tablename__ = "image_split_remote_vision_runs"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    job_id = Column(UUID(as_uuid=True), ForeignKey("image_split_jobs.id", ondelete="CASCADE"), nullable=False)
    provider = Column(String(64), nullable=False)
    model_name = Column(String(128), nullable=False)
    model_version = Column(String(128), nullable=True)
    run_purpose = Column(String(32), nullable=False, default="shadow")
    status = Column(String(20), nullable=False, default="completed")
    prompt_version = Column(String(64), nullable=True)
    request_id = Column(String(128), nullable=True)
    split_x = Column(Integer, nullable=True)
    confidence = Column(Float, nullable=True)
    reasoning = Column(Text, nullable=True)
    input_tokens = Column(Integer, nullable=True)
    output_tokens = Column(Integer, nullable=True)
    total_tokens = Column(Integer, nullable=True)
    latency_ms = Column(Integer, nullable=True)
    error_message = Column(Text, nullable=True)
    raw_request = Column(JSONB, nullable=True)
    raw_response = Column(JSONB, nullable=True)
    run_metadata = Column("metadata", JSONB, nullable=True)
    completed_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))

    job = relationship("ImageSplitJob", back_populates="remote_vision_runs")


class LocalModelPredictionRun(Base):
    __tablename__ = "image_split_local_model_prediction_runs"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    job_id = Column(UUID(as_uuid=True), ForeignKey("image_split_jobs.id", ondelete="CASCADE"), nullable=False)
    model_name = Column(String(128), nullable=False)
    model_version = Column(String(128), nullable=True)
    model_artifact_uri = Column(Text, nullable=True)
    model_artifact_sha256 = Column(String(64), nullable=True)
    training_run_id = Column(String(128), nullable=True)
    dataset_version = Column(String(128), nullable=True)
    run_purpose = Column(String(32), nullable=False, default="shadow")
    status = Column(String(20), nullable=False, default="completed")
    split_x = Column(Integer, nullable=True)
    confidence = Column(Float, nullable=True)
    uncertainty = Column(Float, nullable=True)
    latency_ms = Column(Integer, nullable=True)
    error_message = Column(Text, nullable=True)
    prediction = Column(JSONB, nullable=True)
    run_metadata = Column("metadata", JSONB, nullable=True)
    completed_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))

    job = relationship("ImageSplitJob", back_populates="local_model_prediction_runs")


def create_tables():
    """Create all tables using sync engine (for migrations)."""
    Base.metadata.create_all(sync_engine)
    # Add new columns if upgrading from older schema
    from sqlalchemy import text
    with sync_engine.connect() as conn:
        for col_def in [
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS analyst_verdict VARCHAR(20)",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS correct_split_x INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS reviewed_by VARCHAR(100)",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS ground_truth_split_x INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS ground_truth_set_by TEXT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS ground_truth_set_at TIMESTAMPTZ",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS ground_truth_notes TEXT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_split_x INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_confidence FLOAT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_reasoning TEXT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_input_tokens INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_output_tokens INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_latency_ms INT",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_model VARCHAR(64)",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS claude_vision_ran_at TIMESTAMPTZ",
            "ALTER TABLE image_split_jobs ADD COLUMN IF NOT EXISTS image_display_profile VARCHAR(64)",
            """
            CREATE TABLE IF NOT EXISTS image_split_remote_vision_runs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                job_id UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
                provider VARCHAR(64) NOT NULL,
                model_name VARCHAR(128) NOT NULL,
                model_version VARCHAR(128),
                run_purpose VARCHAR(32) NOT NULL DEFAULT 'shadow',
                status VARCHAR(20) NOT NULL DEFAULT 'completed',
                prompt_version VARCHAR(64),
                request_id VARCHAR(128),
                split_x INT,
                confidence FLOAT,
                reasoning TEXT,
                input_tokens INT,
                output_tokens INT,
                total_tokens INT,
                latency_ms INT,
                error_message TEXT,
                raw_request JSONB,
                raw_response JSONB,
                metadata JSONB,
                completed_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ DEFAULT NOW()
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS image_split_local_model_prediction_runs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                job_id UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
                model_name VARCHAR(128) NOT NULL,
                model_version VARCHAR(128),
                model_artifact_uri TEXT,
                model_artifact_sha256 VARCHAR(64),
                training_run_id VARCHAR(128),
                dataset_version VARCHAR(128),
                run_purpose VARCHAR(32) NOT NULL DEFAULT 'shadow',
                status VARCHAR(20) NOT NULL DEFAULT 'completed',
                split_x INT,
                confidence FLOAT,
                uncertainty FLOAT,
                latency_ms INT,
                error_message TEXT,
                prediction JSONB,
                metadata JSONB,
                completed_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ DEFAULT NOW()
            )
            """,
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS job_id UUID REFERENCES image_split_jobs(id) ON DELETE CASCADE",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS provider VARCHAR(64)",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS model_name VARCHAR(128)",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS model_version VARCHAR(128)",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS run_purpose VARCHAR(32) DEFAULT 'shadow'",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'completed'",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS prompt_version VARCHAR(64)",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS request_id VARCHAR(128)",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS split_x INT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS confidence FLOAT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS reasoning TEXT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS input_tokens INT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS output_tokens INT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS total_tokens INT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS latency_ms INT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS error_message TEXT",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS raw_request JSONB",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS raw_response JSONB",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS metadata JSONB",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS completed_at TIMESTAMPTZ",
            "ALTER TABLE image_split_remote_vision_runs ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW()",
            "ALTER TABLE image_split_remote_vision_runs ALTER COLUMN run_purpose SET DEFAULT 'shadow'",
            "ALTER TABLE image_split_remote_vision_runs ALTER COLUMN status SET DEFAULT 'completed'",
            "ALTER TABLE image_split_remote_vision_runs ALTER COLUMN created_at SET DEFAULT NOW()",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS job_id UUID REFERENCES image_split_jobs(id) ON DELETE CASCADE",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS model_name VARCHAR(128)",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS model_version VARCHAR(128)",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS model_artifact_uri TEXT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS model_artifact_sha256 VARCHAR(64)",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS training_run_id VARCHAR(128)",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS dataset_version VARCHAR(128)",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS run_purpose VARCHAR(32) DEFAULT 'shadow'",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'completed'",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS split_x INT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS confidence FLOAT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS uncertainty FLOAT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS latency_ms INT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS error_message TEXT",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS prediction JSONB",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS metadata JSONB",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS completed_at TIMESTAMPTZ",
            "ALTER TABLE image_split_local_model_prediction_runs ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW()",
            "ALTER TABLE image_split_local_model_prediction_runs ALTER COLUMN run_purpose SET DEFAULT 'shadow'",
            "ALTER TABLE image_split_local_model_prediction_runs ALTER COLUMN status SET DEFAULT 'completed'",
            "ALTER TABLE image_split_local_model_prediction_runs ALTER COLUMN created_at SET DEFAULT NOW()",
            "CREATE INDEX IF NOT EXISTS ix_image_split_jobs_claude_vision_ran_at ON image_split_jobs (claude_vision_ran_at) WHERE claude_vision_ran_at IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_job_created ON image_split_remote_vision_runs (job_id, created_at DESC)",
            "CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_provider_model ON image_split_remote_vision_runs (provider, model_name)",
            "CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_purpose_created ON image_split_remote_vision_runs (run_purpose, created_at DESC)",
            "CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_status ON image_split_remote_vision_runs (status)",
            "CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_job_created ON image_split_local_model_prediction_runs (job_id, created_at DESC)",
            "CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_model_version ON image_split_local_model_prediction_runs (model_name, model_version)",
            "CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_purpose_created ON image_split_local_model_prediction_runs (run_purpose, created_at DESC)",
            "CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_artifact_sha ON image_split_local_model_prediction_runs (model_artifact_sha256)",
            "CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_status ON image_split_local_model_prediction_runs (status)",
        ]:
            conn.execute(text(col_def))
        conn.commit()
    print("Tables created/updated successfully.")
