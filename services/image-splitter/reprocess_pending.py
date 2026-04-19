"""
Directly reprocess all pending jobs using the current strategies.
Run this while the splitter service is stopped.
"""
import asyncio
import sys
import os
sys.path.insert(0, os.path.dirname(__file__))

from models.database import create_tables, AsyncSessionLocal, ImageSplitJob, ImageSplitResult, ImageSplitAssignment
from pipeline.orchestrator import run_pipeline, get_best_result
from pipeline.image_utils import crop_and_encode
from sqlalchemy import select, delete
from datetime import datetime, timezone


async def process_one(job_id):
    async with AsyncSessionLocal() as db:
        job = await db.get(ImageSplitJob, job_id)
        if not job or not job.image_data:
            print(f"  SKIP {job_id}: no image data")
            return

        try:
            job.status = "processing"
            await db.commit()

            # Clean up old results/assignments from previous runs
            old_results = await db.execute(
                select(ImageSplitResult.id).where(ImageSplitResult.job_id == job.id)
            )
            old_result_ids = [r[0] for r in old_results.fetchall()]
            if old_result_ids:
                await db.execute(delete(ImageSplitAssignment).where(ImageSplitAssignment.job_id == job.id))
                await db.execute(delete(ImageSplitResult).where(ImageSplitResult.job_id == job.id))
                await db.commit()

            results = await run_pipeline(job.image_data)

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

            best = get_best_result(results)
            if best:
                job.best_strategy = best.strategy_name
                job.best_score = best.confidence
                job.split_x = best.split_x
                job.status = "completed"
                containers = [c.strip() for c in job.container_numbers.split(",")]
                if len(containers) >= 2:
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
                            job_id=job.id, result_id=best_record.id,
                            container_number=containers[0], position="left",
                            image_data=best.left_image
                        ))
                        db.add(ImageSplitAssignment(
                            job_id=job.id, result_id=best_record.id,
                            container_number=containers[1], position="right",
                            image_data=best.right_image
                        ))
                print(f"  OK  {job.container_numbers} | strategy={best.strategy_name} split_x={best.split_x} score={best.confidence:.3f}")
            else:
                job.status = "failed"
                job.error_message = "No strategy produced a valid split"
                print(f"  FAIL {job.container_numbers}: no strategy succeeded")

            job.completed_at = datetime.now(timezone.utc)
            await db.commit()

        except Exception as e:
            print(f"  ERROR {job_id}: {e}")
            job.status = "failed"
            job.error_message = str(e)
            job.completed_at = datetime.now(timezone.utc)
            await db.commit()


async def main():
    create_tables()
    async with AsyncSessionLocal() as db:
        result = await db.execute(
            select(ImageSplitJob).where(ImageSplitJob.status == "pending")
        )
        pending = result.scalars().all()

    print(f"Found {len(pending)} pending jobs")
    print("Strategies available:")
    from pipeline.orchestrator import get_all_strategies
    for s in get_all_strategies():
        print(f"  - {s.name}")
    print()

    # Process concurrently in batches of 4 to avoid overwhelming memory
    batch_size = 4
    for i in range(0, len(pending), batch_size):
        batch = pending[i:i+batch_size]
        print(f"Batch {i//batch_size + 1}: processing {len(batch)} jobs...")
        await asyncio.gather(*[process_one(j.id) for j in batch])

    print("\nDone.")


if __name__ == "__main__":
    asyncio.run(main())
