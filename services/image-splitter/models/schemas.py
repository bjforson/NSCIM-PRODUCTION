from pydantic import BaseModel, ConfigDict
from typing import Any, Optional
from datetime import datetime
from uuid import UUID


class SplitJobRequest(BaseModel):
    container_numbers: str  # comma-separated: "CONT001,CONT002"
    source_image_id: Optional[str] = None
    scanner_type: Optional[str] = None
    image_base64: Optional[str] = None  # Base64-encoded image data
    image_url: Optional[str] = None  # URL to fetch image from


class SplitJobResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: UUID
    container_numbers: str
    scanner_type: Optional[str] = None
    status: str
    best_strategy: Optional[str] = None
    best_score: Optional[float] = None
    split_x: Optional[int] = None
    image_width: Optional[int] = None
    image_height: Optional[int] = None
    analyst_verdict: Optional[str] = None
    correct_split_x: Optional[int] = None
    created_at: datetime
    completed_at: Optional[datetime] = None
    error_message: Optional[str] = None
    result_count: int = 0


class SplitResultResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: UUID
    strategy_name: str
    split_x: int
    confidence: Optional[float] = None
    processing_ms: Optional[int] = None
    metadata: Optional[dict] = None
    has_left_image: bool = False
    has_right_image: bool = False
    # Flattened metadata fields for Blazor frontend consumption
    reasoning: Optional[str] = None
    c1_right_peak_x: Optional[int] = None
    c2_left_peak_x: Optional[int] = None
    gap_width_px: Optional[int] = None
    verifier_picked: Optional[bool] = None
    verifier_reasoning: Optional[str] = None
    consensus_with_steel_wall: Optional[bool] = None


class ManualSplitRequest(BaseModel):
    split_x: int
    submitted_by: Optional[str] = None


class ApproveRequest(BaseModel):
    result_id: UUID
    container_left: str  # container number for left half
    container_right: str  # container number for right half
    approved_by: str


class RejectRequest(BaseModel):
    correct_split_x: Optional[int] = None   # analyst's correct split point (feedback)
    rejected_by: str
    reason: Optional[str] = None
    review_label: Optional[str] = None
    split_outcome: Optional[str] = None


class RemoteVisionRunBase(BaseModel):
    model_config = ConfigDict(protected_namespaces=())

    provider: str
    model_name: str
    model_version: Optional[str] = None
    run_purpose: str = "shadow"
    status: str = "completed"
    prompt_version: Optional[str] = None
    request_id: Optional[str] = None
    split_x: Optional[int] = None
    confidence: Optional[float] = None
    reasoning: Optional[str] = None
    input_tokens: Optional[int] = None
    output_tokens: Optional[int] = None
    total_tokens: Optional[int] = None
    latency_ms: Optional[int] = None
    error_message: Optional[str] = None
    raw_request: Optional[dict[str, Any]] = None
    raw_response: Optional[dict[str, Any]] = None
    run_metadata: Optional[dict[str, Any]] = None
    completed_at: Optional[datetime] = None


class RemoteVisionRunCreate(RemoteVisionRunBase):
    job_id: UUID


class RemoteVisionRunResponse(RemoteVisionRunBase):
    model_config = ConfigDict(from_attributes=True, protected_namespaces=())

    id: UUID
    job_id: UUID
    created_at: datetime


class LocalModelPredictionRunBase(BaseModel):
    model_config = ConfigDict(protected_namespaces=())

    model_name: str
    model_version: Optional[str] = None
    model_artifact_uri: Optional[str] = None
    model_artifact_sha256: Optional[str] = None
    training_run_id: Optional[str] = None
    dataset_version: Optional[str] = None
    run_purpose: str = "shadow"
    status: str = "completed"
    split_x: Optional[int] = None
    confidence: Optional[float] = None
    uncertainty: Optional[float] = None
    latency_ms: Optional[int] = None
    error_message: Optional[str] = None
    prediction: Optional[dict[str, Any]] = None
    run_metadata: Optional[dict[str, Any]] = None
    completed_at: Optional[datetime] = None


class LocalModelPredictionRunCreate(LocalModelPredictionRunBase):
    job_id: UUID


class LocalModelPredictionRunResponse(LocalModelPredictionRunBase):
    model_config = ConfigDict(from_attributes=True, protected_namespaces=())

    id: UUID
    job_id: UUID
    created_at: datetime


class HealthResponse(BaseModel):
    status: str
    version: str
    strategies_available: list[str]
    db_connected: bool
