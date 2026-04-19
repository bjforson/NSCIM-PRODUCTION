from pydantic import BaseModel
from typing import Optional
from datetime import datetime
from uuid import UUID


class SplitJobRequest(BaseModel):
    container_numbers: str  # comma-separated: "CONT001,CONT002"
    source_image_id: Optional[str] = None
    scanner_type: Optional[str] = None
    image_base64: Optional[str] = None  # Base64-encoded image data
    image_url: Optional[str] = None  # URL to fetch image from


class SplitJobResponse(BaseModel):
    id: UUID
    container_numbers: str
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

    class Config:
        from_attributes = True


class SplitResultResponse(BaseModel):
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

    class Config:
        from_attributes = True


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


class HealthResponse(BaseModel):
    status: str
    version: str
    strategies_available: list[str]
    db_connected: bool
