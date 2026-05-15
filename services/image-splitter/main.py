"""
NSCIM Container Image Splitting Service

A microservice that automatically splits dual-container X-ray scan images
into individual per-container images using multiple detection strategies.

Runs independently from the main NSCIM app on port 5310.
"""

import logging
import asyncio
import os
from datetime import datetime, timezone, timedelta
from uuid import UUID
from typing import Optional
from contextlib import asynccontextmanager
from urllib.parse import urlparse

from fastapi import FastAPI, Depends, HTTPException, BackgroundTasks, UploadFile, File, Form, Query
from fastapi.responses import Response, FileResponse, JSONResponse
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, func

from config import (
    SERVICE_HOST,
    SERVICE_PORT,
    MAX_IMAGE_SIZE_MB,
    ALLOW_IMAGE_URL_FETCHES,
    ALLOWED_IMAGE_URL_HOSTS,
)
from models.database import (
    get_db, create_tables, ImageSplitJob, ImageSplitResult, ImageSplitAssignment,
    RemoteVisionRun
)
from models.schemas import (
    SplitJobRequest, SplitJobResponse, SplitResultResponse,
    ManualSplitRequest, ApproveRequest, RejectRequest, HealthResponse
)
from pipeline.orchestrator import run_pipeline, get_best_result, get_all_strategies
from pipeline.image_utils import (
    base64_to_bytes,
    crop_and_encode,
    crop_side_and_encode,
    detect_image_media_type,
    get_image_dimensions,
)
from pipeline.visual_eligibility import classify_visual_eligibility
from strategies.base import SplitResult

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("image-splitter")


async def resume_pending_jobs():
    """On startup, process any jobs left in 'pending' or stuck 'processing' state.

    A 'processing' job that is older than a short grace window is assumed to be
    orphaned from a previous crash or force-stop, and reset back to 'pending'
    so this startup task can pick it up.
    """
    from datetime import datetime, timedelta, timezone
    from models.database import AsyncSessionLocal
    # Small delay so uvicorn's event loop is fully running before we start
    await asyncio.sleep(1)

    stale_minutes = max(1, int(os.environ.get("SPLITTER_PROCESSING_STALE_MINUTES", "2")))
    stuck_threshold = datetime.now(timezone.utc) - timedelta(minutes=stale_minutes)
    async with AsyncSessionLocal() as db:
        # Reset any orphaned 'processing' jobs back to 'pending'
        stuck_result = await db.execute(
            select(ImageSplitJob).where(
                ImageSplitJob.status == "processing",
                ImageSplitJob.created_at < stuck_threshold
            )
        )
        stuck = stuck_result.scalars().all()
        if stuck:
            logger.warning(
                "Found %s orphaned 'processing' jobs older than %s minute(s) — resetting to pending",
                len(stuck),
                stale_minutes,
            )
            for job in stuck:
                job.status = "pending"
                job.error_message = (job.error_message or "") + " | Auto-reset from stuck 'processing' state"
            await db.commit()

        result = await db.execute(
            select(ImageSplitJob).where(ImageSplitJob.status == "pending")
        )
        pending = result.scalars().all()
        if not pending:
            logger.info("No pending jobs to resume.")
            return
        logger.info(f"Resuming {len(pending)} pending jobs from previous session...")
    # Process all pending jobs concurrently (gather keeps references alive)
    await asyncio.gather(
        *[process_job(job.id) for job in pending],
        return_exceptions=True
    )
    logger.info("Finished resuming pending jobs.")


def run_startup_validation():
    """Validate all dependencies on startup. Logs SUCCESS/FAIL for each."""
    import os
    checks = []

    # 1. PostgreSQL (sync engine)
    try:
        from models.database import sync_engine
        from sqlalchemy import text
        with sync_engine.connect() as conn:
            conn.execute(text("SELECT 1"))
        checks.append(("PostgreSQL (sync)", True, ""))
        logger.info("  [OK] PostgreSQL (sync) — connected")
    except Exception as e:
        checks.append(("PostgreSQL (sync)", False, str(e)))
        logger.error("  [FAIL] PostgreSQL (sync) — %s", e)

    # 2. PostgreSQL (async engine)
    try:
        from models.database import async_engine
        checks.append(("PostgreSQL (async)", True, "pool configured"))
        logger.info("  [OK] PostgreSQL (async) — pool configured")
    except Exception as e:
        checks.append(("PostgreSQL (async)", False, str(e)))
        logger.error("  [FAIL] PostgreSQL (async) — %s", e)

    # 3. FS6000 network share
    fs6000_share = os.environ.get("NICKSCAN_FS6000_SHARE", r"Z:\23301FS01")
    if os.path.isdir(fs6000_share):
        checks.append(("FS6000 share", True, fs6000_share))
        logger.info("  [OK] FS6000 share — %s", fs6000_share)
    else:
        checks.append(("FS6000 share", False, f"not accessible: {fs6000_share}"))
        logger.warning("  [WARN] FS6000 share not accessible: %s (DB blob fallback available)", fs6000_share)

    # 4. Anthropic API key
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if api_key:
        checks.append(("Anthropic API key", True, "set"))
        logger.info("  [OK] Anthropic API key — set")
    else:
        checks.append(("Anthropic API key", False, "not set"))
        logger.warning("  [WARN] ANTHROPIC_API_KEY not set — Claude Vision strategy disabled")

    # 5. Tesseract OCR
    try:
        import shutil
        tess = shutil.which("tesseract")
        if tess:
            checks.append(("Tesseract OCR", True, tess))
            logger.info("  [OK] Tesseract OCR — %s", tess)
        else:
            checks.append(("Tesseract OCR", False, "not in PATH"))
            logger.warning("  [WARN] Tesseract not in PATH — OCR strategy disabled")
    except Exception:
        checks.append(("Tesseract OCR", False, "check failed"))

    # 6. psutil (for health endpoint)
    try:
        import psutil
        checks.append(("psutil", True, "available"))
        logger.info("  [OK] psutil — available")
    except ImportError:
        checks.append(("psutil", False, "not installed"))
        logger.warning("  [WARN] psutil not installed — health endpoint will have limited metrics")

    failed = [c for c in checks if not c[1] and "PostgreSQL" in c[0]]
    if failed:
        logger.error("FATAL: PostgreSQL connection failed — service cannot operate")
        raise SystemExit(1)

    return checks


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("=" * 60)
    logger.info("NSCIM Raw Image Engine starting...")
    logger.info("=" * 60)
    logger.info("Startup validation:")
    run_startup_validation()
    logger.info("Creating database tables...")
    create_tables()
    logger.info("Database tables ready.")
    app.state.resume_task = asyncio.create_task(resume_pending_jobs())
    logger.info("NSCIM Raw Image Engine ready on port %s", os.environ.get("SPLITTER_PORT", "5320"))
    logger.info("=" * 60)
    yield
    logger.info("NSCIM Raw Image Engine shutting down.")


app = FastAPI(
    title="NSCIM Raw Image Engine",
    description="Raw X-ray image decoding (ASE/FS6000), 16-bit rendering, analysis tools, and dual-container splitting",
    version="2.7.0",
    lifespan=lifespan
)

# X-Ray Inspector blueprint — pure-Python decoders (ASE + FS6000), analysis
# tools, PDF reports. See inspector/ for implementation.
from inspector.routes import router as inspector_router  # noqa: E402
app.include_router(inspector_router)


# -- Ingress validation -------------------------------------------------------

MAX_IMAGE_SIZE_BYTES = MAX_IMAGE_SIZE_MB * 1024 * 1024
UPLOAD_READ_CHUNK_BYTES = 1024 * 1024


def _raise_image_too_large(source: str) -> None:
    raise HTTPException(413, f"{source} exceeds maximum image size of {MAX_IMAGE_SIZE_MB} MB")


def _enforce_image_size(image_data: bytes, source: str) -> None:
    if len(image_data) > MAX_IMAGE_SIZE_BYTES:
        _raise_image_too_large(source)


def _estimate_base64_payload_size(b64_string: str) -> int:
    payload = b64_string.split(",", 1)[1] if "," in b64_string else b64_string
    payload = "".join(payload.split())
    padding = payload.count("=")
    return max(0, (len(payload) * 3) // 4 - padding)


def _validate_image_url(image_url: str) -> None:
    if not ALLOW_IMAGE_URL_FETCHES:
        raise HTTPException(400, "image_url fetches are disabled; upload image data directly")

    parsed = urlparse(image_url)
    if parsed.scheme not in {"http", "https"} or not parsed.hostname:
        raise HTTPException(400, "image_url must be an absolute http(s) URL")
    if parsed.username or parsed.password:
        raise HTTPException(400, "image_url must not include embedded credentials")

    hostname = parsed.hostname.lower().rstrip(".")
    if ALLOWED_IMAGE_URL_HOSTS and hostname not in ALLOWED_IMAGE_URL_HOSTS:
        raise HTTPException(400, "image_url host is not allowed")


async def _fetch_image_url(image_url: str) -> bytes:
    import httpx

    _validate_image_url(image_url)
    try:
        async with httpx.AsyncClient(timeout=30, follow_redirects=False) as client:
            async with client.stream("GET", image_url) as resp:
                resp.raise_for_status()
                content_length = resp.headers.get("content-length")
                if content_length:
                    try:
                        if int(content_length) > MAX_IMAGE_SIZE_BYTES:
                            _raise_image_too_large("image_url response")
                    except ValueError:
                        pass

                chunks = []
                total = 0
                async for chunk in resp.aiter_bytes():
                    total += len(chunk)
                    if total > MAX_IMAGE_SIZE_BYTES:
                        _raise_image_too_large("image_url response")
                    chunks.append(chunk)
    except httpx.HTTPStatusError as exc:
        raise HTTPException(502, f"image_url fetch failed with status {exc.response.status_code}") from exc
    except httpx.HTTPError as exc:
        raise HTTPException(502, "image_url fetch failed") from exc

    return b"".join(chunks)


async def _read_upload_with_limit(file: UploadFile) -> bytes:
    chunks = []
    total = 0
    while True:
        chunk = await file.read(UPLOAD_READ_CHUNK_BYTES)
        if not chunk:
            break
        total += len(chunk)
        if total > MAX_IMAGE_SIZE_BYTES:
            _raise_image_too_large("uploaded image")
        chunks.append(chunk)
    return b"".join(chunks)


# ── Background processing ─────────────────────────────────────────────

def ensure_minimum_split_candidates(results: list[SplitResult], image_data: bytes, image_width: Optional[int]) -> list[SplitResult]:
    """Return at least two candidate splits when the image can be cropped."""
    if len(results) >= 2:
        return results

    try:
        width = image_width or get_image_dimensions(image_data)[0]
    except Exception:
        logger.warning("Could not determine image width for fallback split candidates", exc_info=True)
        return results

    if width < 4:
        return results

    existing = [r.split_x for r in results]
    best = get_best_result(results)
    base_x = best.split_x if best else width // 2
    base_confidence = best.confidence if best else 0.33
    min_separation = max(12, int(width * 0.01))
    offset = max(24, int(width * 0.03))

    candidate_points = [
        base_x - offset,
        base_x + offset,
        width // 2,
        (width // 2) - offset,
        (width // 2) + offset,
    ]

    for split_x in candidate_points:
        if len(results) >= 2:
            break

        split_x = max(1, min(int(split_x), width - 1))
        if any(abs(split_x - existing_x) < min_separation for existing_x in existing):
            continue

        try:
            left_bytes, right_bytes = crop_and_encode(image_data, split_x)
        except Exception:
            logger.warning("Could not crop fallback split candidate at x=%s", split_x, exc_info=True)
            continue

        strategy_name = "fallback_midpoint" if not best else "fallback_offset"
        if any(r.strategy_name == strategy_name for r in results):
            strategy_name = f"{strategy_name}_{len(results) + 1}"

        results.append(SplitResult(
            strategy_name=strategy_name,
            split_x=split_x,
            confidence=max(0.05, min(0.95, base_confidence * 0.75)),
            processing_ms=0,
            metadata={
                "fallback": True,
                "basis_strategy": best.strategy_name if best else "geometric_midpoint",
                "basis_split_x": base_x,
                "reasoning": "Generated to provide a second analyst-review candidate when only one strategy result was available.",
            },
            left_image=left_bytes,
            right_image=right_bytes
        ))
        existing.append(split_x)

    return results


def _remote_vision_run_from_openai_advisory(job_id: UUID, result: SplitResult) -> Optional[RemoteVisionRun]:
    """Build an append-only audit row from OpenAI advisory metadata."""
    metadata = result.metadata if isinstance(result.metadata, dict) else {}
    advisory = metadata.get("openai_vision_advisory")
    if not isinstance(advisory, dict):
        return None

    usage = advisory.get("usage") if isinstance(advisory.get("usage"), dict) else {}
    return RemoteVisionRun(
        job_id=job_id,
        provider=str(advisory.get("provider") or "openai"),
        model_name=str(advisory.get("model") or "unknown"),
        model_version=str(advisory.get("model_version")) if advisory.get("model_version") else None,
        run_purpose="shadow",
        status="completed",
        prompt_version=str(advisory.get("prompt_version")) if advisory.get("prompt_version") else None,
        split_x=advisory.get("split_x") if isinstance(advisory.get("split_x"), int) else None,
        confidence=advisory.get("confidence") if isinstance(advisory.get("confidence"), (int, float)) else None,
        reasoning=str(advisory.get("reasoning") or "")[:2000] or None,
        input_tokens=usage.get("input_tokens") if isinstance(usage.get("input_tokens"), int) else None,
        output_tokens=usage.get("output_tokens") if isinstance(usage.get("output_tokens"), int) else None,
        total_tokens=usage.get("total_tokens") if isinstance(usage.get("total_tokens"), int) else None,
        latency_ms=advisory.get("latency_ms") if isinstance(advisory.get("latency_ms"), int) else None,
        raw_response={"assessment": advisory},
        run_metadata={
            "source_result_strategy": result.strategy_name,
            "source_result_split_x": result.split_x,
            "source_result_confidence": result.confidence,
            "deterministic_selected_strategy": advisory.get("deterministic_selected_strategy"),
            "deterministic_split_x": advisory.get("deterministic_split_x"),
            "purpose": advisory.get("purpose"),
            "advisory_only": True,
        },
        completed_at=datetime.now(timezone.utc),
    )


async def process_job(job_id: UUID):
    """Background task: run the splitting pipeline on a job."""
    from models.database import AsyncSessionLocal

    async with AsyncSessionLocal() as db:
        job = await db.get(ImageSplitJob, job_id)
        if not job or not job.image_data:
            logger.error(f"Job {job_id} not found or has no image data")
            return

        try:
            job.status = "processing"
            await db.commit()

            # FS6000 metadata can legitimately list two container numbers even
            # when the rendered image is visually single-container or too
            # ambiguous to split. Gate on pixels before creating any crop
            # assignments so downstream analysts do not receive bogus halves.
            container_count = len([
                token for token in job.container_numbers.replace(";", ",").split(",")
                if token.strip() and token.strip().upper() != "UNKNOWN"
            ])
            if (job.scanner_type or "").upper() == "FS6000" and container_count >= 2:
                gate = classify_visual_eligibility(job.image_data, job.scanner_type)
                if not gate.should_split:
                    metadata = gate.to_metadata()
                    job.best_strategy = "visual_eligibility"
                    job.best_score = gate.confidence
                    job.split_x = None
                    job.status = "visual_single" if gate.label == "single_container" else "uncertain"
                    job.error_message = (
                        f"Visual eligibility gate classified image as {gate.label}; "
                        f"reasons={','.join(gate.reason_codes)}; "
                        f"metadata={metadata}"
                    )
                    job.completed_at = datetime.now(timezone.utc)
                    await db.commit()
                    logger.info(
                        "Job %s skipped by visual eligibility gate: label=%s confidence=%.3f reasons=%s",
                        job_id,
                        gate.label,
                        gate.confidence,
                        ",".join(gate.reason_codes),
                    )
                    return

            # Run all strategies
            results = await run_pipeline(job.image_data)
            results = ensure_minimum_split_candidates(results, job.image_data, job.image_width)

            # Store results
            for r in results:
                db_result = ImageSplitResult(
                    job_id=job.id,
                    strategy_name=r.strategy_name,
                    split_x=r.split_x,
                    confidence=r.confidence,
                    processing_ms=r.processing_ms,
                    strategy_metadata=r.metadata,
                    left_image=r.left_image,
                    right_image=r.right_image
                )
                db.add(db_result)

                # 1.19.0 — If this is a Claude Vision result, also denormalise it onto
                # the job row so we can query "all jobs that Claude has processed" and
                # compute accuracy metrics without joining to results every time.
                if r.strategy_name == "claude_vision":
                    job.claude_vision_split_x = r.split_x
                    job.claude_vision_confidence = r.confidence
                    job.claude_vision_reasoning = (r.metadata or {}).get("reasoning")
                    usage = (r.metadata or {}).get("usage", {}) or {}
                    job.claude_vision_input_tokens = usage.get("input_tokens")
                    job.claude_vision_output_tokens = usage.get("output_tokens")
                    job.claude_vision_latency_ms = r.processing_ms
                    job.claude_vision_model = (r.metadata or {}).get("model")
                    job.claude_vision_ran_at = datetime.now(timezone.utc)

                openai_run = _remote_vision_run_from_openai_advisory(job.id, r)
                if openai_run is not None:
                    db.add(openai_run)

            # Select best result
            best = get_best_result(results)
            if best:
                job.best_strategy = best.strategy_name
                job.best_score = best.confidence
                job.split_x = best.split_x
                job.status = "completed"

                # Auto-assign container numbers to halves
                containers = [c.strip() for c in job.container_numbers.split(",")]
                if len(containers) >= 2:
                    # Find the result DB record for the best strategy
                    await db.flush()
                    best_db = await db.execute(
                        select(ImageSplitResult).where(
                            ImageSplitResult.job_id == job.id,
                            ImageSplitResult.strategy_name == best.strategy_name
                        )
                    )
                    best_record = best_db.scalar_one_or_none()

                    if best_record:
                        db.add(ImageSplitAssignment(
                            job_id=job.id,
                            result_id=best_record.id,
                            container_number=containers[0],
                            position="left",
                            image_data=best.left_image
                        ))
                        db.add(ImageSplitAssignment(
                            job_id=job.id,
                            result_id=best_record.id,
                            container_number=containers[1],
                            position="right",
                            image_data=best.right_image
                        ))
            else:
                job.status = "failed"
                job.error_message = "No strategy produced a valid split"

            job.completed_at = datetime.now(timezone.utc)
            await db.commit()
            logger.info(f"Job {job_id} completed: {job.status} (best={job.best_strategy}, score={job.best_score})")

        except Exception as e:
            logger.error(f"Job {job_id} failed: {e}", exc_info=True)
            job.status = "failed"
            job.error_message = str(e)
            job.completed_at = datetime.now(timezone.utc)
            await db.commit()


# ── API Endpoints ──────────────────────────────────────────────────────

@app.get("/api/health", response_model=HealthResponse)
async def health_check(db: AsyncSession = Depends(get_db)):
    """Health check endpoint."""
    try:
        await db.execute(select(func.count()).select_from(ImageSplitJob))
        db_ok = True
    except Exception:
        db_ok = False

    return HealthResponse(
        status="healthy" if db_ok else "degraded",
        version="1.0.0",
        strategies_available=[s.name for s in get_all_strategies()],
        db_connected=db_ok
    )


@app.post("/api/split", response_model=SplitJobResponse)
async def create_split_job(
    request: SplitJobRequest,
    background_tasks: BackgroundTasks,
    db: AsyncSession = Depends(get_db)
):
    """Submit a new image splitting job."""
    image_data = None

    if request.image_base64:
        if _estimate_base64_payload_size(request.image_base64) > MAX_IMAGE_SIZE_BYTES:
            _raise_image_too_large("image_base64")
        try:
            image_data = base64_to_bytes(request.image_base64)
        except Exception as exc:
            raise HTTPException(400, "Invalid image_base64 payload") from exc
        _enforce_image_size(image_data, "image_base64")
    elif request.image_url:
        image_data = await _fetch_image_url(request.image_url)

    if not image_data:
        raise HTTPException(400, "Either image_base64 or image_url must be provided")

    w, h = get_image_dimensions(image_data)

    job = ImageSplitJob(
        container_numbers=request.container_numbers,
        source_image_id=UUID(request.source_image_id) if request.source_image_id else None,
        scanner_type=request.scanner_type,
        image_data=image_data,
        image_width=w,
        image_height=h,
        status="pending"
    )
    db.add(job)
    await db.commit()
    await db.refresh(job)

    # Process in background
    background_tasks.add_task(process_job, job.id)

    return SplitJobResponse(
        id=job.id,
        container_numbers=job.container_numbers,
        scanner_type=job.scanner_type,
        status=job.status,
        created_at=job.created_at,
        result_count=0
    )


@app.post("/api/split/upload", response_model=SplitJobResponse)
async def create_split_job_upload(
    background_tasks: BackgroundTasks,
    file: UploadFile = File(...),
    container_numbers: str = Form(...),
    scanner_type: Optional[str] = Form(None),
    source_image_id: Optional[str] = Form(None),
    db: AsyncSession = Depends(get_db)
):
    """Submit a split job with direct file upload."""
    image_data = await _read_upload_with_limit(file)
    w, h = get_image_dimensions(image_data)

    parsed_source_image_id = None
    if source_image_id:
        try:
            parsed_source_image_id = UUID(source_image_id)
        except ValueError as exc:
            raise HTTPException(400, "Invalid source_image_id") from exc

    job = ImageSplitJob(
        container_numbers=container_numbers,
        source_image_id=parsed_source_image_id,
        scanner_type=scanner_type,
        image_data=image_data,
        image_width=w,
        image_height=h,
        status="pending"
    )
    db.add(job)
    await db.commit()
    await db.refresh(job)

    background_tasks.add_task(process_job, job.id)

    return SplitJobResponse(
        id=job.id,
        container_numbers=job.container_numbers,
        scanner_type=job.scanner_type,
        status=job.status,
        created_at=job.created_at,
        result_count=0
    )


@app.get("/api/split/search")
async def search_jobs_by_containers(container_numbers: str, db: AsyncSession = Depends(get_db)):
    """Find split jobs by container_numbers (exact match, ignoring spaces)."""
    normalized = container_numbers.replace(" ", "")
    result = await db.execute(
        select(ImageSplitJob)
        .where(func.replace(ImageSplitJob.container_numbers, " ", "") == normalized)
        .order_by(ImageSplitJob.created_at.desc())
        .limit(5)
    )
    jobs = result.scalars().all()
    if not jobs:
        return []

    async def _result_count(job_id):
        r = await db.execute(select(func.count()).where(ImageSplitResult.job_id == job_id))
        return r.scalar() or 0

    out = []
    for j in jobs:
        rc = await _result_count(j.id)
        out.append(SplitJobResponse(
            id=j.id,
            container_numbers=j.container_numbers,
            scanner_type=j.scanner_type,
            status=j.status,
            best_strategy=j.best_strategy,
            best_score=j.best_score,
            split_x=j.split_x,
            image_width=j.image_width,
            image_height=j.image_height,
            analyst_verdict=j.analyst_verdict,
            correct_split_x=j.correct_split_x,
            created_at=j.created_at,
            completed_at=j.completed_at,
            error_message=j.error_message,
            result_count=rc
        ))
    return out


@app.get("/api/split/pending")
async def list_pending_jobs(
    mode: str = Query(
        "legacy",
        pattern="^(legacy|active|recent|reviewable|backlog)$",
        description=(
            "legacy returns the historical 200-row backlog; active returns only "
            "pending/processing; recent/reviewable return actionable recent jobs; "
            "backlog returns all completed unreviewed jobs."
        ),
    ),
    limit: int = Query(200, ge=1, le=500),
    since_hours: int | None = Query(None, ge=1, le=24 * 31),
    scanner_type: str | None = Query(None, description="Optional scanner filter such as ASE or FS6000."),
    db: AsyncSession = Depends(get_db)
):
    """List split jobs for operational review.

    The original endpoint returned a hard-coded 200-row historical backlog.
    The split-review UI now calls mode=recent so operators see the current queue,
    while legacy remains the default for older helper paths that use this route
    as a broad lookup fallback.
    """
    filters = [
        ImageSplitJob.analyst_verdict.is_(None),
    ]
    if scanner_type and scanner_type.strip().lower() not in {"all", "*"}:
        filters.append(func.upper(ImageSplitJob.scanner_type) == scanner_type.strip().upper())

    mode_norm = mode.lower()
    if mode_norm == "active":
        filters.append(ImageSplitJob.status.in_(["pending", "processing"]))
    elif mode_norm == "backlog":
        filters.append(ImageSplitJob.status == "completed")
    elif mode_norm in ("recent", "reviewable"):
        filters.append(ImageSplitJob.status.in_(["pending", "processing", "completed"]))
        cutoff_hours = since_hours or 72
        filters.append(
            ImageSplitJob.created_at >= datetime.now(timezone.utc) - timedelta(hours=cutoff_hours)
        )
    else:
        filters.append(ImageSplitJob.status.in_(["pending", "processing", "completed", "failed"]))

    result = await db.execute(
        select(ImageSplitJob)
        .where(*filters)
        .order_by(ImageSplitJob.created_at.desc())
        .limit(limit)
    )
    jobs = result.scalars().all()

    async def _result_count(job_id):
        r = await db.execute(select(func.count()).where(ImageSplitResult.job_id == job_id))
        return r.scalar() or 0

    out = []
    for j in jobs:
        rc = await _result_count(j.id)
        out.append(SplitJobResponse(
            id=j.id,
            container_numbers=j.container_numbers,
            scanner_type=j.scanner_type,
            status=j.status,
            best_strategy=j.best_strategy,
            best_score=j.best_score,
            split_x=j.split_x,
            image_width=j.image_width,
            image_height=j.image_height,
            analyst_verdict=j.analyst_verdict,
            correct_split_x=j.correct_split_x,
            created_at=j.created_at,
            completed_at=j.completed_at,
            error_message=j.error_message,
            result_count=rc
        ))
    return out


@app.get("/api/split/{job_id}", response_model=SplitJobResponse)
async def get_split_job(job_id: UUID, db: AsyncSession = Depends(get_db)):
    """Get status and summary of a split job."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")

    result_count = await db.execute(
        select(func.count()).where(ImageSplitResult.job_id == job_id)
    )

    return SplitJobResponse(
        id=job.id,
        container_numbers=job.container_numbers,
        scanner_type=job.scanner_type,
        status=job.status,
        best_strategy=job.best_strategy,
        best_score=job.best_score,
        split_x=job.split_x,
        created_at=job.created_at,
        completed_at=job.completed_at,
        error_message=job.error_message,
        result_count=result_count.scalar() or 0
    )


@app.get("/api/split/{job_id}/results")
async def get_split_results(job_id: UUID, db: AsyncSession = Depends(get_db)):
    """Get all strategy results for a job."""
    results = await db.execute(
        select(ImageSplitResult)
        .where(ImageSplitResult.job_id == job_id)
        .order_by(ImageSplitResult.confidence.desc())
    )
    rows = results.scalars().all()

    out = []
    print(f"[FLATTEN_V2] {len(rows)} results for job {job_id}", flush=True)
    for r in rows:
        meta = r.strategy_metadata if isinstance(r.strategy_metadata, dict) else {}
        out.append({
            "id": str(r.id),
            "strategy_name": r.strategy_name,
            "split_x": r.split_x,
            "confidence": r.confidence,
            "processing_ms": r.processing_ms,
            "metadata": meta,
            "has_left_image": r.left_image is not None,
            "has_right_image": r.right_image is not None,
            "reasoning": meta.get("reasoning"),
            "c1_right_peak_x": meta.get("c1_right_peak_x"),
            "c2_left_peak_x": meta.get("c2_left_peak_x"),
            "gap_width_px": meta.get("gap_width_px"),
            "verifier_picked": meta.get("verifier_picked"),
            "verifier_reasoning": meta.get("verifier_reasoning"),
            "consensus_with_steel_wall": meta.get("consensus_with_steel_wall"),
        })
    return out


@app.get("/api/split/{job_id}/results/{result_id}/image/{side}")
async def get_result_image(job_id: UUID, result_id: UUID, side: str, db: AsyncSession = Depends(get_db)):
    """Get the left or right cropped image from a result."""
    if side not in ("left", "right"):
        raise HTTPException(400, "Side must be 'left' or 'right'")

    result = await db.get(ImageSplitResult, result_id)
    if not result or result.job_id != job_id:
        raise HTTPException(404, "Result not found")

    img_data = result.left_image if side == "left" else result.right_image
    if not img_data:
        raise HTTPException(404, "Image not available")

    return Response(content=img_data, media_type="image/jpeg")


@app.get("/api/split/{job_id}/results/{result_id}/lossless/{side}")
async def get_result_lossless_image(job_id: UUID, result_id: UUID, side: str, db: AsyncSession = Depends(get_db)):
    """Render a left or right crop losslessly from the original dual-container scan bytes."""
    if side not in ("left", "right"):
        raise HTTPException(400, "Side must be 'left' or 'right'")

    result = await db.get(ImageSplitResult, result_id)
    if not result or result.job_id != job_id:
        raise HTTPException(404, "Result not found")

    job = await db.get(ImageSplitJob, job_id)
    if not job or not job.image_data:
        raise HTTPException(404, "Job or original image not found")

    try:
        img_data = crop_side_and_encode(bytes(job.image_data), result.split_x, side, format="png")
    except Exception as exc:
        logger.warning(
            "Failed to render lossless split crop job=%s result=%s side=%s",
            job_id,
            result_id,
            side,
            exc_info=True,
        )
        raise HTTPException(500, f"Could not render lossless split crop: {exc}") from exc

    return Response(
        content=img_data,
        media_type="image/png",
        headers={
            "X-Split-X": str(result.split_x),
            "X-Crop-Encoding": "png",
            "Cache-Control": "private, max-age=3600",
        },
    )


@app.post("/api/split/{job_id}/manual")
async def submit_manual_split(
    job_id: UUID,
    request: ManualSplitRequest,
    background_tasks: BackgroundTasks,
    db: AsyncSession = Depends(get_db)
):
    """Submit a manual split point from an analyst."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")

    left_bytes, right_bytes = crop_and_encode(job.image_data, request.split_x)

    result = ImageSplitResult(
        job_id=job.id,
        strategy_name="manual",
        split_x=request.split_x,
        confidence=1.0,
        processing_ms=0,
        metadata={"submitted_by": request.submitted_by or "analyst"},
        left_image=left_bytes,
        right_image=right_bytes
    )
    db.add(result)

    # Update job with manual result as best
    job.best_strategy = "manual"
    job.best_score = 1.0
    job.split_x = request.split_x
    job.status = "completed"
    job.completed_at = datetime.now(timezone.utc)

    await db.commit()
    return {"status": "ok", "split_x": request.split_x}


@app.post("/api/split/{job_id}/approve")
async def approve_split(
    job_id: UUID,
    request: ApproveRequest,
    db: AsyncSession = Depends(get_db)
):
    """Approve a split result and assign container numbers to halves."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")

    result = await db.get(ImageSplitResult, request.result_id)
    if not result or result.job_id != job_id:
        raise HTTPException(404, "Result not found")

    # Update or create assignments
    for pos, container in [("left", request.container_left), ("right", request.container_right)]:
        img_data = result.left_image if pos == "left" else result.right_image
        assignment = ImageSplitAssignment(
            job_id=job.id,
            result_id=result.id,
            container_number=container,
            position=pos,
            image_data=img_data,
            approved=True,
            approved_by=request.approved_by,
            approved_at=datetime.now(timezone.utc)
        )
        db.add(assignment)

    # Record analyst verdict
    job.analyst_verdict = "approved"
    job.correct_split_x = result.split_x   # the approved split IS the ground truth
    job.reviewed_by = request.approved_by
    job.reviewed_at = datetime.now(timezone.utc)
    job.best_strategy = result.strategy_name
    job.best_score = result.confidence
    job.split_x = result.split_x
    await db.commit()

    return {"status": "approved", "strategy": result.strategy_name, "split_x": result.split_x}




@app.post("/api/split/{job_id}/reject")
async def reject_split(
    job_id: UUID,
    request: RejectRequest,
    db: AsyncSession = Depends(get_db)
):
    """Reject a split result. Optionally provide the correct split X for feedback."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")

    raw_label = (request.review_label or request.split_outcome or "rejected").strip().lower().replace("-", "_")
    analyst_verdict = raw_label if raw_label in {"rejected", "single_container", "bad_image", "uncertain"} else "rejected"

    job.analyst_verdict = analyst_verdict
    job.correct_split_x = request.correct_split_x if analyst_verdict == "rejected" else None
    job.reviewed_by = request.rejected_by
    job.reviewed_at = datetime.now(timezone.utc)
    await db.commit()

    return {
        "status": analyst_verdict,
        "correct_split_x": request.correct_split_x,
        "review_label": analyst_verdict,
        "message": "Feedback recorded. If you provided a correct split point, it will be used for algorithm improvement."
    }


# ── Annotation tool endpoints ─────────────────────────────────────────

@app.get("/annotate")
async def annotate_page():
    """Serve the interactive split annotation page."""
    import os
    html_path = os.path.join(os.path.dirname(__file__), "static", "annotate.html")
    return FileResponse(html_path, media_type="text/html")


@app.get("/api/split/jobs/all")
async def list_all_jobs(db: AsyncSession = Depends(get_db)):
    """List all jobs with verdict info and wall positions for the annotation tool."""
    import json as _json
    result = await db.execute(
        select(ImageSplitJob).order_by(ImageSplitJob.created_at.desc())
    )
    jobs = result.scalars().all()

    out = []
    for j in jobs:
        # Get wall positions from the steel_wall_midpoint result metadata
        lw = None
        rw = None
        res = await db.execute(
            select(ImageSplitResult).where(
                ImageSplitResult.job_id == j.id,
                ImageSplitResult.strategy_name == "steel_wall_midpoint"
            )
        )
        swm = res.scalar_one_or_none()
        if swm and swm.strategy_metadata:
            meta = swm.strategy_metadata if isinstance(swm.strategy_metadata, dict) else {}
            lw = meta.get("left_wall_x")
            rw = meta.get("right_wall_x")

        out.append({
            "id": str(j.id),
            "container_numbers": j.container_numbers,
            "split_x": j.split_x,
            "correct_split_x": j.correct_split_x,
            "analyst_verdict": j.analyst_verdict,
            "image_width": j.image_width,
            "image_height": j.image_height,
            "status": j.status,
            "left_wall_x": lw,
            "right_wall_x": rw,
        })
    return out


@app.get("/api/split/{job_id}/original")
async def get_original_image(job_id: UUID, db: AsyncSession = Depends(get_db)):
    """Return the full original dual-container scan image."""
    job = await db.get(ImageSplitJob, job_id)
    if not job or not job.image_data:
        raise HTTPException(404, "Job or image not found")
    image_data = bytes(job.image_data)
    return Response(content=image_data, media_type=detect_image_media_type(image_data))


from pydantic import BaseModel as _AnnotateBase

class SetCorrectRequest(_AnnotateBase):
    correct_split_x: int

class WallVerdictRequest(_AnnotateBase):
    wall_verdict: str

@app.post("/api/split/{job_id}/wall-verdict")
async def set_wall_verdict(job_id: UUID, request: WallVerdictRequest, db: AsyncSession = Depends(get_db)):
    """Store wall detection verdict (walls_correct / walls_wrong)."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")
    job.analyst_verdict = request.wall_verdict
    await db.commit()
    return {"status": "ok", "wall_verdict": request.wall_verdict}

@app.post("/api/split/{job_id}/set-correct")
async def set_correct_split(job_id: UUID, request: SetCorrectRequest, db: AsyncSession = Depends(get_db)):
    """Set the correct_split_x from the annotation tool."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")
    job.correct_split_x = request.correct_split_x
    await db.commit()
    return {"status": "ok", "correct_split_x": request.correct_split_x}


@app.get("/groundtruth")
async def groundtruth_page():
    """1.20.0 — interactive ground-truth annotation tool.

    Displays a set of jobs with all strategy split_x values overlaid on the
    image and lets the operator click to mark the TRUE split position. Used to
    build a calibration set for validating/improving the strategies (especially
    Claude Vision) on asymmetric trailers (20ft+40ft) where steel_wall_midpoint
    geometrically misplaces the split.
    """
    import os
    html_path = os.path.join(os.path.dirname(__file__), "static", "groundtruth.html")
    return FileResponse(html_path, media_type="text/html")


@app.get("/api/split/jobs/groundtruth-bundle")
async def groundtruth_bundle(limit: int = 20, db: AsyncSession = Depends(get_db)):
    """1.20.0 — return N jobs with all strategy split_x values for calibration.

    Prioritises jobs that have both claude_vision AND steel_wall_midpoint
    results so the annotator can compare methods side-by-side. Skips the
    TEST001 synthetic jobs.
    """
    # Fetch jobs ordered by most-recent first (so the 10 smoke-test jobs come
    # up first; backfill will add more over time)
    result = await db.execute(
        select(ImageSplitJob)
        .where(ImageSplitJob.container_numbers != "TEST001,TEST002")
        .where(ImageSplitJob.image_data.isnot(None))
        .order_by(ImageSplitJob.claude_vision_ran_at.desc().nulls_last(),
                  ImageSplitJob.created_at.desc())
        .limit(limit)
    )
    jobs = result.scalars().all()

    out = []
    for j in jobs:
        strat_rows = await db.execute(
            select(ImageSplitResult).where(ImageSplitResult.job_id == j.id)
        )
        strategies = []
        for r in strat_rows.scalars().all():
            strategies.append({
                "strategy_name": r.strategy_name,
                "split_x": r.split_x,
                "confidence": r.confidence,
            })
        out.append({
            "id": str(j.id),
            "container_numbers": j.container_numbers,
            "image_width": j.image_width,
            "image_height": j.image_height,
            "best_split_x": j.split_x,
            "best_strategy": j.best_strategy,
            "ground_truth_split_x": j.ground_truth_split_x,
            "ground_truth_set_by": j.ground_truth_set_by,
            "strategies": strategies,
        })
    return out


class SetGroundTruthRequest(_AnnotateBase):
    ground_truth_split_x: int
    set_by: Optional[str] = None
    notes: Optional[str] = None


@app.post("/api/split/{job_id}/ground-truth")
async def set_ground_truth(
    job_id: UUID,
    request: SetGroundTruthRequest,
    db: AsyncSession = Depends(get_db)
):
    """1.20.0 — record the operator's click-set ground-truth split_x."""
    job = await db.get(ImageSplitJob, job_id)
    if not job:
        raise HTTPException(404, "Job not found")
    if job.image_width and (request.ground_truth_split_x < 0 or request.ground_truth_split_x > job.image_width):
        raise HTTPException(400, f"split_x {request.ground_truth_split_x} out of range [0,{job.image_width}]")
    job.ground_truth_split_x = request.ground_truth_split_x
    job.ground_truth_set_by = request.set_by or "anonymous"
    job.ground_truth_set_at = datetime.now(timezone.utc)
    if request.notes:
        job.ground_truth_notes = request.notes
    await db.commit()
    return {
        "status": "ok",
        "ground_truth_split_x": request.ground_truth_split_x,
        "set_by": job.ground_truth_set_by,
        "set_at": job.ground_truth_set_at.isoformat(),
    }


@app.get("/diagnose")
async def diagnose_page():
    """1.20.x — diagnostic viewer where Claude describes what it sees on an image."""
    import os as _os
    html_path = _os.path.join(_os.path.dirname(__file__), "static", "diagnose.html")
    return FileResponse(html_path, media_type="text/html")


@app.get("/api/split/jobs/diagnose-bundle")
async def diagnose_bundle(limit: int = 30, db: AsyncSession = Depends(get_db)):
    """Return N pending jobs (newest first) for the diagnostic tool."""
    result = await db.execute(
        select(ImageSplitJob)
        .where(ImageSplitJob.container_numbers != "TEST001,TEST002")
        .where(ImageSplitJob.image_data.isnot(None))
        .where(ImageSplitJob.analyst_verdict.is_(None))
        .order_by(ImageSplitJob.created_at.desc())
        .limit(limit)
    )
    jobs = result.scalars().all()
    out = []
    for j in jobs:
        strat_rows = await db.execute(
            select(ImageSplitResult).where(ImageSplitResult.job_id == j.id)
        )
        strategies = [
            {"strategy_name": r.strategy_name, "split_x": r.split_x, "confidence": r.confidence}
            for r in strat_rows.scalars().all()
        ]
        out.append({
            "id": str(j.id),
            "container_numbers": j.container_numbers,
            "image_width": j.image_width,
            "image_height": j.image_height,
            "best_split_x": j.split_x,
            "best_strategy": j.best_strategy,
            "strategies": strategies,
        })
    return out


@app.post("/api/split/{job_id}/describe")
async def describe_image(job_id: UUID, db: AsyncSession = Depends(get_db)):
    """Ask Claude Vision to produce a verbal description of what it sees on
    an image. Returns the full text + token usage. No DB writes.
    """
    job = await db.get(ImageSplitJob, job_id)
    if not job or not job.image_data:
        raise HTTPException(404, "Job or image not found")
    try:
        from anthropic import Anthropic
    except ImportError:
        raise HTTPException(500, "anthropic package not installed")
    import os as _os, base64 as _b64, io as _io, time as _time
    from PIL import Image as _Image

    api_key = _os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        raise HTTPException(500, "ANTHROPIC_API_KEY not configured")
    MAX_RES = int(_os.environ.get("CLAUDE_VISION_MAX_RES", "1568"))
    image_bytes = bytes(job.image_data)
    w = job.image_width or 0
    h = job.image_height or 0
    scale = 1.0
    if max(w, h) > MAX_RES:
        scale = MAX_RES / max(w, h)
        img = _Image.open(_io.BytesIO(image_bytes))
        if img.mode not in ("RGB", "L"):
            img = img.convert("RGB")
        new_size = (int(round(w * scale)), int(round(h * scale)))
        img = img.resize(new_size, _Image.LANCZOS)
        buf = _io.BytesIO()
        img.save(buf, format="JPEG", quality=88)
        image_bytes = buf.getvalue()
    disp_w = int(round((w or 0) * scale))
    disp_h = int(round((h or 0) * scale))
    top_strip_y_end = int(round(disp_h * 0.25))

    prompt = (
        "This is a CARGO X-RAY SCAN of a freight trailer (NOT a photograph — "
        "a 2D projection of a 3D scene). Describe what you see with maximum "
        "precision using both x AND y axes to anchor every feature.\n\n"
        f"Image: {disp_w}×{disp_h}px. Origin (x=0,y=0) top-left.\n\n"
        f"Scan the TOP STRIP first (y=0 to y={top_strip_y_end}, upper 25%). "
        "The container boundary is defined by the OUTER EDGE of the corner "
        "casting (a solid steel block 20–30 px wide). If the right casting "
        "of container 1 is (x_start=637, x_end=661), container 1 ENDS at "
        "661, not 637.\n\n"
        "## Sections (markdown headings)\n"
        "1. Trailer layout — bed x range, tires, tractor if visible\n"
        "2. Container 1 (leftmost) — corner castings as 4-tuples, roof y, "
        "   cargo geometry (NO semantic labels like 'drums' or 'humans')\n"
        "3. Container 2 (rightmost if present) — same structure\n"
        "4. Inter-container gap — gap_start (c1_right OUTER edge), gap_end "
        "   (c2_left OUTER edge), gap_width, split_x = midpoint\n"
        "5. Visual ambiguities — features in the middle y that are NOT "
        "   container boundaries\n"
        "6. Confidence (0–10) — strict rubric, prefer lower over wrong\n\n"
        "Plain prose with markdown headings. Under 700 words."
    )

    b64 = _b64.standard_b64encode(image_bytes).decode("ascii")
    client = Anthropic(api_key=api_key)
    model_name = _os.environ.get("CLAUDE_VISION_MODEL", "claude-sonnet-4-5")
    started = _time.monotonic()
    try:
        response = await asyncio.to_thread(
            client.messages.create,
            model=model_name,
            max_tokens=1800,
            messages=[{
                "role": "user",
                "content": [
                    {"type": "image", "source": {"type": "base64", "media_type": "image/jpeg", "data": b64}},
                    {"type": "text", "text": prompt},
                ],
            }],
        )
    except Exception as e:
        raise HTTPException(502, f"Anthropic API call failed: {e}")

    elapsed_ms = int((_time.monotonic() - started) * 1000)
    text = ""
    for block in response.content:
        if getattr(block, "type", None) == "text":
            text += block.text
    usage = {}
    try:
        if response.usage is not None:
            usage = {
                "input_tokens": response.usage.input_tokens,
                "output_tokens": response.usage.output_tokens,
            }
    except Exception:
        pass

    return {
        "job_id": str(job.id),
        "container_numbers": job.container_numbers,
        "image_width": job.image_width,
        "image_height": job.image_height,
        "downsampled_width": disp_w,
        "downsampled_height": disp_h,
        "downsample_scale": round(scale, 4),
        "model": model_name,
        "latency_ms": elapsed_ms,
        "usage": usage,
        "description": text,
    }


@app.post("/api/split/{job_id}/verify-candidates")
async def verify_candidates_endpoint(job_id: UUID, db: AsyncSession = Depends(get_db)):
    """1.20.x — run the claude_verifier against a job's existing strategy
    results. Returns the ranking + annotated image for /diagnose display.
    """
    import base64 as _b64
    from pipeline.claude_verifier import verify_candidates_with_claude

    job = await db.get(ImageSplitJob, job_id)
    if not job or not job.image_data:
        raise HTTPException(404, "Job or image not found")
    result_rows = await db.execute(
        select(ImageSplitResult).where(ImageSplitResult.job_id == job_id)
    )
    results = result_rows.scalars().all()
    if not results:
        raise HTTPException(404, "No strategy results for this job")
    try:
        pick = await verify_candidates_with_claude(bytes(job.image_data), results)
    except Exception as e:
        raise HTTPException(502, f"Verifier failed: {e}")
    if pick is None:
        raise HTTPException(422, "Verifier did not return a pick (insufficient candidates or API failure)")
    return {
        "job_id": str(job.id),
        "picked_strategy": pick.picked_strategy,
        "picked_split_x": pick.picked_split_x,
        "picked_label": pick.picked_label,
        "ranking": pick.ranking,
        "reasoning": pick.reasoning,
        "claude_confidence": pick.claude_confidence,
        "few_shot_count": pick.few_shot_count,
        "candidates_offered": [
            {"strategy_name": c[0], "split_x": c[1], "label": c[2]}
            for c in pick.candidates_offered
        ],
        "annotated_image_base64": _b64.standard_b64encode(pick.annotated_image_bytes).decode("ascii"),
        "latency_ms": pick.latency_ms,
        "usage": pick.usage,
    }


if __name__ == "__main__":
    import uvicorn
    # SECURITY/RELIABILITY: reload=True is dev-only. It disables graceful shutdown,
    # orphans workers, and bypasses SIGTERM handling. Enable via env var for local dev.
    _reload = os.getenv("SPLITTER_RELOAD", "false").lower() == "true"
    uvicorn.run("main:app", host=SERVICE_HOST, port=SERVICE_PORT, reload=_reload)
