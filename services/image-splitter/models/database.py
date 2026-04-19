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
        ]:
            conn.execute(text(col_def))
        conn.commit()
    print("Tables created/updated successfully.")
